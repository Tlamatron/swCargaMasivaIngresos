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
	/// Clase ProcesadorPadronDBF que implementa la interfaz IProcesadorFormato. Este procesador se encarga de leer archivos heredados en formato DBF (dBase/FoxPro) que contienen datos de padrón, mapearlos, limpiarlos y validarlos, y finalmente insertar los registros válidos en la base de datos mediante una operación de inserción masiva.
	/// </summary>
	public class ProcesadorPadronDBF : IProcesadorFormato
	{
		private readonly string CadenaConexion = ConfiguracionApp.ObtenerCadenaConexion();

		/// <summary>
		/// Procesa un archivo DBF de padrón, mapea sus columnas a una estructura interna, limpia y valida los datos, y realiza la inserción masiva en la base de datos.
		/// </summary>
		/// <param name="rutaArchivo"></param>
		/// <param name="param"></param>
		/// <returns></returns>
		public async Task<ResultadoProceso> ProcesarAsync(string rutaArchivo, ParametrosCarga param)
		{
			var resultadoFinal = new ResultadoProceso { ErroresDetalle = new List<string>() };

			LogService.WriteLogAsync("INFO", param.UsuarioLogin, "ProcesadorPadronDBF", $"Iniciando Lectura de Padrón DBF. Folio: {param.FolioCarga}").Wait();

			try
			{
				using (var stream = File.Open(rutaArchivo, FileMode.Open, FileAccess.Read))
				using (var table = NDbfReader.Table.Open(stream))
				{
					var columnNames = table.Columns.Select(c => c.Name.ToUpper()).ToList();

					// 🚀 VALIDACIÓN FRONTAL
					if (!columnNames.Contains("TIPO_PRED") && !columnNames.Contains("TIPO") && !columnNames.Contains("T_PREDIO"))
					{
						resultadoFinal.ErroresDetalle.Add("Rechazo Total: El archivo DBF de padrón no contiene la columna de 'Tipo de Predio'.");
						resultadoFinal.RegistrosFallidos = 1;
						return resultadoFinal;
					}

					// Mapeo dinámico de columnas típicas de DBF
					string colCuenta = columnNames.FirstOrDefault(c => c == "NO_CONTROL" || c == "CUENTA") ?? "";
					string colTipoPredio = columnNames.FirstOrDefault(c => c == "TIPO_PRED" || c == "TIPO" || c == "T_PREDIO") ?? "";
					string colNombre = columnNames.FirstOrDefault(c => c == "NOMBRE" || c == "PROPIETARI") ?? "";
					string colCalle = columnNames.FirstOrDefault(c => c == "CALLE" || c == "DIRECCION") ?? "";
					string colColonia = columnNames.FirstOrDefault(c => c == "COLONIA" || c == "UBICACION") ?? "";
					string colBaseGrav = columnNames.FirstOrDefault(c => c == "BASE_GRAV" || c == "VALOR_CAT") ?? "";
					string colBimEmi = columnNames.FirstOrDefault(c => c == "BIM_EMI" || c == "BIMESTRE") ?? "";
					string colTotal = columnNames.FirstOrDefault(c => c == "TOTAL" || c == "IMPTO") ?? "";

					if (string.IsNullOrEmpty(colCuenta))
					{
						resultadoFinal.ErroresDetalle.Add("Rechazo Total: No se encontró la columna de Cuenta Predial (NO_CONTROL o CUENTA).");
						return resultadoFinal;
					}

					DataTable tablaCrudos = CrearEstructuraPadronCompleta();
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

						string clasePago = "1";
						string bimestre = "99";
						string bimEmi = !string.IsNullOrEmpty(colBimEmi) ? (reader.GetString(colBimEmi)?.Trim() ?? "0") : "0";
						if (bimEmi != "0" && bimEmi != "") { clasePago = "2"; bimestre = bimEmi; }

						string baseGravableStr = !string.IsNullOrEmpty(colBaseGrav) ? (reader.GetString(colBaseGrav)?.Trim() ?? "0") : "0";
						string impuestoStr = !string.IsNullOrEmpty(colTotal) ? (reader.GetString(colTotal)?.Trim() ?? "0") : "0";

						DataRow nuevaFila = tablaCrudos.NewRow();
						nuevaFila["ClaveMunicipio"] = param.ClaveMunicipioDestino > 0 ? param.ClaveMunicipioDestino.ToString() : param.OficinaId.ToString();
						nuevaFila["TipoPredio"] = tipoPredio;
						nuevaFila["CuentaPredial"] = cuentaPredial;
						nuevaFila["FolioUnico"] = "";
						nuevaFila["Localidad"] = "";
						nuevaFila["Calle"] = !string.IsNullOrEmpty(colCalle) ? (reader.GetString(colCalle)?.Trim() ?? "") : "";
						nuevaFila["NumExterior"] = "";
						nuevaFila["NumInterior"] = "";
						nuevaFila["Letra"] = "";
						nuevaFila["Colonia"] = !string.IsNullOrEmpty(colColonia) ? (reader.GetString(colColonia)?.Trim() ?? "") : "";
						nuevaFila["CP"] = "";
						nuevaFila["Nombre"] = !string.IsNullOrEmpty(colNombre) ? (reader.GetString(colNombre)?.Trim() ?? "") : "";
						nuevaFila["PrimerApellido"] = "";
						nuevaFila["SegundoApellido"] = "";
						nuevaFila["TipoPersona"] = "";
						nuevaFila["RFC"] = "";
						nuevaFila["ClaveRegimenSAT"] = "";
						nuevaFila["ClaveUsoSAT"] = "";
						nuevaFila["CPFiscalSAT"] = "";
						nuevaFila["BaseGravable"] = baseGravableStr;
						nuevaFila["ClasePago"] = clasePago;
						nuevaFila["Bimestre"] = bimestre;
						nuevaFila["ImpuestoDeterminado"] = impuestoStr;
						nuevaFila["FechaVigencia"] = DateTime.Now.ToString("yyyy-MM-dd");
						nuevaFila["FolioCarga"] = param.FolioCarga.ToString();

						tablaCrudos.Rows.Add(nuevaFila);
					}

					var resultadoLimpieza = LimpiadorDatos.LimpiarYValidar(tablaCrudos, "DBF", param);

					if (resultadoLimpieza.TablaValidos.Rows.Count > 0)
					{
						List<string> erroresLogicos = await InsertarBulkPadronCompletoAsync(resultadoLimpieza.TablaValidos, param);
						if (erroresLogicos.Any()) resultadoFinal.ErroresDetalle.AddRange(erroresLogicos);
					}

					resultadoFinal.RegistrosExitosos += resultadoLimpieza.TablaValidos.Rows.Count;
					resultadoFinal.RegistrosFallidos += resultadoLimpieza.TablaRechazados.Rows.Count;
					if (resultadoLimpieza.DetallesErrores != null && resultadoLimpieza.DetallesErrores.Any()) resultadoFinal.ErroresDetalle.AddRange(resultadoLimpieza.DetallesErrores);
				}
			}
			catch (Exception ex)
			{
				LogService.WriteLogAsync("ERROR", param.UsuarioLogin, "ProcesadorPadronDBF", $"Fallo crítico al leer DBF: {ex.Message}").Wait();
				resultadoFinal.ErroresDetalle.Add("Error al intentar abrir el archivo DBF de Padrón.");
			}

			return resultadoFinal;
		}

		/// <summary>
		/// Crea la estructura de un DataTable para almacenar temporalmente los datos del padrón antes de ser insertados en la base de datos. Esta estructura incluye todas las columnas necesarias para representar un registro completo del padrón, incluyendo información de ubicación, propietario, impuestos y metadatos de carga.
		/// </summary>
		/// <returns></returns>
		private DataTable CrearEstructuraPadronCompleta()
		{
			var tabla = new DataTable();
			tabla.Columns.Add("ClaveMunicipio", typeof(string));
			tabla.Columns.Add("TipoPredio", typeof(string));
			tabla.Columns.Add("CuentaPredial", typeof(string));
			tabla.Columns.Add("FolioUnico", typeof(string));
			tabla.Columns.Add("Localidad", typeof(string));
			tabla.Columns.Add("Calle", typeof(string));
			tabla.Columns.Add("NumExterior", typeof(string));
			tabla.Columns.Add("NumInterior", typeof(string));
			tabla.Columns.Add("Letra", typeof(string));
			tabla.Columns.Add("Colonia", typeof(string));
			tabla.Columns.Add("CP", typeof(string));
			tabla.Columns.Add("Nombre", typeof(string));
			tabla.Columns.Add("PrimerApellido", typeof(string));
			tabla.Columns.Add("SegundoApellido", typeof(string));
			tabla.Columns.Add("TipoPersona", typeof(string));
			tabla.Columns.Add("RFC", typeof(string));
			tabla.Columns.Add("ClaveRegimenSAT", typeof(string));
			tabla.Columns.Add("ClaveUsoSAT", typeof(string));
			tabla.Columns.Add("CPFiscalSAT", typeof(string));
			tabla.Columns.Add("BaseGravable", typeof(string));
			tabla.Columns.Add("ClasePago", typeof(string));
			tabla.Columns.Add("Bimestre", typeof(string));
			tabla.Columns.Add("ImpuestoDeterminado", typeof(string));
			tabla.Columns.Add("FechaVigencia", typeof(string));
			tabla.Columns.Add("FolioCarga", typeof(string));
			return tabla;
		}
		
		/// <summary>
		/// Realiza la inserción masiva de los registros del padrón en la base de datos utilizando SqlBulkCopy. Después de insertar los registros en la tabla de staging, ejecuta un procedimiento almacenado para procesar y consolidar los datos en las tablas finales. Este método es asíncrono y permite manejar grandes volúmenes de datos de manera eficiente.
		/// </summary>
		/// <param name="lote"></param>
		/// <param name="param"></param>
		/// <returns></returns>
		private async Task<List<string>> InsertarBulkPadronCompletoAsync(DataTable lote, ParametrosCarga param)
		{
			var erroresConsolidacion = new List<string>();
			using (SqlConnection conn = new SqlConnection(CadenaConexion))
			{
				await conn.OpenAsync();
				using (SqlBulkCopy bulkCopy = new SqlBulkCopy(conn))
				{
					bulkCopy.DestinationTableName = "pred_Operacion.Staging_Predial";
					bulkCopy.BatchSize = 10000;
					foreach (DataColumn col in lote.Columns) bulkCopy.ColumnMappings.Add(col.ColumnName, col.ColumnName);
					await bulkCopy.WriteToServerAsync(lote);
				}

				using (SqlCommand cmd = new SqlCommand("pred_Operacion.sp_ProcesarMergePadron", conn))
				{
					cmd.CommandType = CommandType.StoredProcedure;
					cmd.Parameters.AddWithValue("@FolioCarga", param.FolioCarga);
					await cmd.ExecuteNonQueryAsync();
				}

				using (SqlCommand cmdConsolidacion = new SqlCommand("pred_Operacion.sp_ConsolidarAdeudos", conn))
				{
					cmdConsolidacion.CommandType = CommandType.StoredProcedure;
					cmdConsolidacion.Parameters.AddWithValue("@FolioCarga", param.FolioCarga);
					using (var reader = await cmdConsolidacion.ExecuteReaderAsync())
					{
						while (await reader.ReadAsync()) erroresConsolidacion.Add($"[Consolidación] Cuenta {reader["CuentaPredial"]}: {reader["MensajeError"]}");
					}
				}
			}
			return erroresConsolidacion;
		}
	}
}