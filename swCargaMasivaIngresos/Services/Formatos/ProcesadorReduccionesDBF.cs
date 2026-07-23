using NDbfReader;
using swCargaMasivaIngresos.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace swCargaMasivaIngresos.Services.Formatos
{
	/// <summary>
	/// Clase ProcesadorReduccionesDBF que implementa la interfaz IProcesadorFormato. Este procesador se encarga de leer archivos heredados en formato DBF (dBase/FoxPro) que contienen datos de reducciones, mapearlos, limpiarlos y validarlos, y finalmente insertar los registros válidos en la base de datos mediante una operación de inserción masiva.
	/// </summary>
	public class ProcesadorReduccionesDBF : IProcesadorFormato
	{
		private readonly string CadenaConexion = ConfiguracionApp.ObtenerCadenaConexion();

		/// <summary>
		/// Procesa un archivo DBF de reducciones de manera asíncrona. Lee el archivo, valida su estructura y contenido, y realiza la inserción masiva de los registros válidos en la base de datos. Devuelve un objeto ResultadoProceso que contiene información sobre los registros procesados, incluyendo los exitosos, fallidos y detalles de errores.
		/// </summary>
		/// <param name="rutaArchivo"></param>
		/// <param name="param"></param>
		/// <returns></returns>
		public async Task<ResultadoProceso> ProcesarAsync(string rutaArchivo, ParametrosCarga param)
		{
			var resultadoFinal = new ResultadoProceso { ErroresDetalle = new List<string>() };

			LogService.WriteLogAsync("INFO", param.UsuarioLogin, "ProcesadorReduccionesDBF", $"Iniciando Lectura de Reducciones DBF. Folio: {param.FolioCarga}").Wait();

			try
			{
				using (var stream = File.Open(rutaArchivo, FileMode.Open, FileAccess.Read))
				using (var table = NDbfReader.Table.Open(stream))
				{
					var columnNames = table.Columns.Select(c => c.Name.ToUpper()).ToList();

					// 🚀 VALIDACIÓN FRONTAL
					if (!columnNames.Contains("TIPO_PRED") && !columnNames.Contains("TIPO") && !columnNames.Contains("T_PREDIO"))
					{
						resultadoFinal.ErroresDetalle.Add("Rechazo Total: El DBF de Reducciones no contiene 'Tipo de Predio'.");
						resultadoFinal.RegistrosFallidos = 1;
						return resultadoFinal;
					}

					string colCuenta = columnNames.FirstOrDefault(c => c == "NO_CONTROL" || c == "CUENTA") ?? "";
					string colTipoPredio = columnNames.FirstOrDefault(c => c == "TIPO_PRED" || c == "TIPO" || c == "T_PREDIO") ?? "";
					string colReduccion = columnNames.FirstOrDefault(c => c.Contains("REDUCCION") || c.Contains("DESC") || c == "TIPO_RED") ?? "";

					if (string.IsNullOrEmpty(colCuenta) || string.IsNullOrEmpty(colReduccion))
					{
						resultadoFinal.ErroresDetalle.Add("Rechazo Total: No se encontró la columna de Cuenta o Tipo de Reducción en el DBF.");
						return resultadoFinal;
					}

					DataTable tablaCrudos = CrearEstructuraReducciones();
					var reader = table.OpenReader();

					while (reader.Read())
					{
						string cuentaPredial = reader.GetString(colCuenta)?.Trim() ?? "";
						if (string.IsNullOrWhiteSpace(cuentaPredial)) continue;

						string tipoPredioCrudo = !string.IsNullOrEmpty(colTipoPredio) ? (reader.GetString(colTipoPredio)?.Trim() ?? "") : "";
						string tipoPredio = "1";
						if (tipoPredioCrudo.ToUpper().StartsWith("U")) tipoPredio = "1";
						else if (tipoPredioCrudo.ToUpper().StartsWith("R")) tipoPredio = "2";
						else if (tipoPredioCrudo.ToUpper().StartsWith("S")) tipoPredio = "3";

						string tipoReduccionStr = reader.GetString(colReduccion)?.Trim() ?? "";

						// Validamos regla de negocio (Debe ser numérico)
						if (!byte.TryParse(tipoReduccionStr, out byte tipoRed) || tipoRed < 1)
						{
							resultadoFinal.RegistrosFallidos++;
							resultadoFinal.ErroresDetalle.Add($"Cuenta '{cuentaPredial}': Tipo de Reducción inválido ('{tipoReduccionStr}').");
							continue;
						}

						string claveMunicipio = param.ClaveMunicipioDestino > 0 ? param.ClaveMunicipioDestino.ToString() : param.OficinaId.ToString();

						DataRow nuevaFila = tablaCrudos.NewRow();
						nuevaFila["ClaveMunicipio"] = claveMunicipio;
						nuevaFila["TipoPredio"] = tipoPredio;
						nuevaFila["CuentaPredial"] = cuentaPredial;
						nuevaFila["FolioUnico"] = "";
						nuevaFila["TipoReduccion"] = tipoRed.ToString();
						nuevaFila["FolioCarga"] = param.FolioCarga;

						tablaCrudos.Rows.Add(nuevaFila);
					}

					if (tablaCrudos.Rows.Count > 0)
					{
						await InsertarBulkAsync(tablaCrudos, param);
						resultadoFinal.RegistrosExitosos += tablaCrudos.Rows.Count;
					}
				}
			}
			catch (Exception ex)
			{
				LogService.WriteLogAsync("ERROR", param.UsuarioLogin, "ProcesadorReduccionesDBF", $"Fallo crítico: {ex.Message}").Wait();
				resultadoFinal.ErroresDetalle.Add("Error al intentar abrir el archivo DBF de Reducciones.");
			}

			return resultadoFinal;
		}

		/// <summary>
		/// Crea la estructura de la tabla en memoria para almacenar temporalmente los registros de reducciones antes de insertarlos en la base de datos. La tabla contiene columnas para ClaveMunicipio, TipoPredio, CuentaPredial, FolioUnico, TipoReduccion y FolioCarga.
		/// </summary>
		/// <returns></returns>
		private DataTable CrearEstructuraReducciones()
		{
			var tabla = new DataTable();
			tabla.Columns.Add("ClaveMunicipio", typeof(string));
			tabla.Columns.Add("TipoPredio", typeof(string));
			tabla.Columns.Add("CuentaPredial", typeof(string));
			tabla.Columns.Add("FolioUnico", typeof(string));
			tabla.Columns.Add("TipoReduccion", typeof(string));
			tabla.Columns.Add("FolioCarga", typeof(int));
			return tabla;
		}

		/// <summary>
		/// Realiza la inserción masiva de los registros de reducciones en la base de datos utilizando SqlBulkCopy. Después de insertar los registros en la tabla de staging, ejecuta un procedimiento almacenado para procesar y consolidar los datos en las tablas finales. Este método es asíncrono y permite manejar grandes volúmenes de datos de manera eficiente.
		/// </summary>
		/// <param name="lote"></param>
		/// <param name="param"></param>
		/// <returns></returns>
		private async Task InsertarBulkAsync(DataTable lote, ParametrosCarga param)
		{
			using (SqlConnection conn = new SqlConnection(CadenaConexion))
			{
				await conn.OpenAsync();
				using (SqlBulkCopy bulkCopy = new SqlBulkCopy(conn))
				{
					bulkCopy.DestinationTableName = "pred_Operacion.Staging_Reducciones";
					bulkCopy.BatchSize = 10000;
					foreach (DataColumn col in lote.Columns) bulkCopy.ColumnMappings.Add(col.ColumnName, col.ColumnName);
					await bulkCopy.WriteToServerAsync(lote);
				}

				using (SqlCommand cmd = new SqlCommand("pred_Operacion.sp_ProcesarMergeReducciones", conn))
				{
					cmd.CommandType = CommandType.StoredProcedure;
					cmd.Parameters.AddWithValue("@FolioCarga", param.FolioCarga);
					await cmd.ExecuteNonQueryAsync();
				}
			}
		}
	}
}