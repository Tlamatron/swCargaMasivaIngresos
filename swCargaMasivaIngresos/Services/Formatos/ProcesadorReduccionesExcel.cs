using ExcelDataReader;
using swCargaMasivaIngresos.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace swCargaMasivaIngresos.Services
{
	/// <summary>
	/// Clase encargada de procesar archivos Excel que contienen información de reducciones (descuentos).
	/// Implementa IProcesadorFormato de manera asíncrona.
	/// </summary>
	public class ProcesadorReduccionesExcel : IProcesadorFormato
	{
		private readonly string CadenaConexion = ConfiguracionApp.ObtenerCadenaConexion();

		/// <summary>
		/// Método principal asíncrono para procesar un archivo Excel de reducciones. 
		/// Extrae los datos, aplica las reglas de validación y realiza la inserción masiva.
		/// </summary>
		/// <param name="rutaArchivo"></param>
		/// <param name="param"></param>
		/// <returns></returns>
		public async Task<ResultadoProceso> ProcesarAsync(string rutaArchivo, ParametrosCarga param)
		{
			var resultadoFinal = new ResultadoProceso { ErroresDetalle = new List<string>() };

			LogService.WriteLogAsync("INFO", param.UsuarioLogin, "ProcesadorReduccionesExcel", $"Iniciando Lectura Inteligente de Descuentos. Folio: {param.FolioCarga}").Wait();

			try
			{
				using (var stream = File.Open(rutaArchivo, FileMode.Open, FileAccess.Read))
				using (var reader = ExcelReaderFactory.CreateReader(stream))
				{
					var dataSet = reader.AsDataSet(new ExcelDataSetConfiguration
					{
						ConfigureDataTable = _ => new ExcelDataTableConfiguration { UseHeaderRow = false }
					});

					foreach (DataTable tablaExcel in dataSet.Tables)
					{
						int filaInicioDatos;
						var mapaCrudo = MapeadorInteligente.ObtenerMapaPorRegiones(tablaExcel, out filaInicioDatos);

						if (mapaCrudo.Count == 0) continue;

						var mapaBloqueado = MapeadorInteligente.ProcesarEncabezadosConMemoria(mapaCrudo);
						DataTable tablaCrudos = CrearEstructuraReducciones();

						for (int i = filaInicioDatos; i < tablaExcel.Rows.Count; i++)
						{
							var fila = tablaExcel.Rows[i];
							if (string.IsNullOrWhiteSpace(string.Join("", fila.ItemArray))) continue;

							// 🚀 EXTRACCIÓN SEGURA (No importa el orden ni si faltan columnas como Folio Único)
							string cuentaPredial = ExtraerSeguro(fila, mapaBloqueado, "CuentaPredial", "");
							if (string.IsNullOrWhiteSpace(cuentaPredial) || cuentaPredial.Equals("Cuenta", StringComparison.OrdinalIgnoreCase)) continue;
							if (cuentaPredial.EndsWith(".0")) cuentaPredial = cuentaPredial.Replace(".0", "");

							string tipoPredio = ExtraerSeguro(fila, mapaBloqueado, "TipoPredio", "").ToUpper().Trim();
							if (tipoPredio == "U" || tipoPredio.StartsWith("URBANO")) tipoPredio = "1";
							else if (tipoPredio == "R" || tipoPredio.StartsWith("RUSTICO") || tipoPredio.StartsWith("RÚSTICO")) tipoPredio = "2";
							else if (tipoPredio == "S" || tipoPredio.StartsWith("SUBURBANO") || tipoPredio.StartsWith("SUB")) tipoPredio = "3";
							if (string.IsNullOrWhiteSpace(tipoPredio)) tipoPredio = "1";

							// 🚀 FALLBACK SEGURO DE MUNICIPIO (Manejado como cadena para parseo posterior)
							string claveMunicipio = ExtraerSeguro(fila, mapaBloqueado, "ClaveMunicipio", "");
							if (string.IsNullOrWhiteSpace(claveMunicipio) && param != null)
							{
								claveMunicipio = param.ClaveMunicipioDestino > 0 ? param.ClaveMunicipioDestino.ToString() : param.OficinaId.ToString();
							}

							string folioUnico = ExtraerSeguro(fila, mapaBloqueado, "FolioUnico", "");
							string tipoReduccion = ExtraerSeguro(fila, mapaBloqueado, "TipoReduccion", "").Trim();
							if (tipoReduccion.EndsWith(".0")) tipoReduccion = tipoReduccion.Replace(".0", "");

							// 🚀 VALIDACIONES DE NEGOCIO
							if (!short.TryParse(claveMunicipio, out short claveMun) || claveMun < 1 || claveMun > 217)
							{
								resultadoFinal.RegistrosFallidos++;
								resultadoFinal.ErroresDetalle.Add($"Fila {i + 1}: Clave de municipio '{claveMunicipio}' inválida.");
								continue;
							}

							if (!byte.TryParse(tipoReduccion, out byte tipoRed) || tipoRed < 1)
							{
								resultadoFinal.RegistrosFallidos++;
								resultadoFinal.ErroresDetalle.Add($"Fila {i + 1}: Tipo de Reducción inválido. Valor encontrado: '{tipoReduccion}'. Debe ser numérico.");
								continue;
							}

							DataRow nuevaFila = tablaCrudos.NewRow();
							nuevaFila["ClaveMunicipio"] = claveMun.ToString();
							nuevaFila["TipoPredio"] = tipoPredio;
							nuevaFila["CuentaPredial"] = cuentaPredial;
							nuevaFila["FolioUnico"] = Utilerias.LimpiarCadena(folioUnico, 50);
							nuevaFila["TipoReduccion"] = tipoRed.ToString();
							nuevaFila["FolioCarga"] = param.FolioCarga;

							tablaCrudos.Rows.Add(nuevaFila);
						}

						// 🚀 EJECUCIÓN ASÍNCRONA A BASE DE DATOS
						if (tablaCrudos.Rows.Count > 0)
						{
							await InsertarBulkAsync(tablaCrudos, param);
							resultadoFinal.RegistrosExitosos += tablaCrudos.Rows.Count;
						}
					}
				}
			}
			catch (Exception ex)
			{
				LogService.WriteLogAsync("ERROR", param.UsuarioLogin, "ProcesadorReduccionesExcel", $"Fallo crítico: {ex.Message}").Wait();
				throw;
			}

			return resultadoFinal;
		}

		/// <summary>
		/// Método auxiliar para extraer valores de una fila de datos de manera segura.
		/// </summary>
		private string ExtraerSeguro(DataRow fila, MapeadorInteligente.MapaOficial mapa, string columna, string valorPorDefecto = "")
		{
			try
			{
				string valor = MapeadorInteligente.Extraer(fila, mapa, columna);
				return string.IsNullOrWhiteSpace(valor) ? valorPorDefecto : valor.Trim();
			}
			catch
			{
				return valorPorDefecto;
			}
		}

		/// <summary>
		/// Estructura en memoria para organizar los datos extraídos de reducciones antes del volcado a la BD.
		/// </summary>
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
		/// Método asíncrono que realiza la inserción masiva a Staging y ejecuta la ingesta de las reducciones.
		/// Nota: La consolidación final de reducciones queda pendiente de reglas de negocio futuras.
		/// </summary>
		private async Task InsertarBulkAsync(DataTable lote, ParametrosCarga param)
		{
			using (SqlConnection conn = new SqlConnection(CadenaConexion))
			{
				await conn.OpenAsync();

				// PASO 1: Inserción Masiva a Staging
				using (SqlBulkCopy bulkCopy = new SqlBulkCopy(conn))
				{
					bulkCopy.DestinationTableName = "pred_Operacion.Staging_Reducciones";
					bulkCopy.BatchSize = 10000;
					bulkCopy.BulkCopyTimeout = 120;

					bulkCopy.ColumnMappings.Add("ClaveMunicipio", "ClaveMunicipio");
					bulkCopy.ColumnMappings.Add("TipoPredio", "TipoPredio");
					bulkCopy.ColumnMappings.Add("CuentaPredial", "CuentaPredial");
					bulkCopy.ColumnMappings.Add("FolioUnico", "FolioUnico");
					bulkCopy.ColumnMappings.Add("TipoReduccion", "TipoReduccion");
					bulkCopy.ColumnMappings.Add("FolioCarga", "FolioCarga");

					await bulkCopy.WriteToServerAsync(lote);
				}

				// PASO 2: Ingesta (Merge Original)
				using (SqlCommand cmd = new SqlCommand("pred_Operacion.sp_ProcesarMergeReducciones", conn))
				{
					cmd.CommandType = CommandType.StoredProcedure;
					cmd.CommandTimeout = 180;
					cmd.Parameters.AddWithValue("@FolioCarga", param.FolioCarga);

					await cmd.ExecuteNonQueryAsync();
				}

				// PASO 3: Consolidación (Omitido intencionalmente hasta definir reglas de negocio)
			}
		}
	}
}