using NDbfReader;
using swCargaMasivaIngresos.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Web.UI.WebControls;

namespace swCargaMasivaIngresos.Services.Formatos
{
	/// <summary>
	/// Clase encargada de procesar archivos heredados DBF (dBase/FoxPro).
	/// Implementa un Rechazo Frontal si el archivo carece de las columnas obligatorias como el Tipo de Predio.
	/// </summary>
	public class ProcesadorPagosDBF : IProcesadorFormato
	{
		private readonly string CadenaConexion = ConfiguracionApp.ObtenerCadenaConexion();

		/// <summary>
		/// Procesa un archivo DBF, extrayendo los datos relevantes y realizando validaciones.
		/// </summary>
		/// <param name="rutaArchivo"></param>
		/// <param name="param"></param>
		/// <returns></returns>
		public async Task<ResultadoProceso> ProcesarAsync(string rutaArchivo, ParametrosCarga param)
		{
			var resultadoFinal = new ResultadoProceso { ErroresDetalle = new List<string>() };

			LogService.WriteLogAsync("INFO", param.UsuarioLogin, "ProcesadorPagosDBF", $"Iniciando Lectura de archivo DBF. Folio: {param.FolioCarga}").Wait();

			try
			{
				using (var stream = File.Open(rutaArchivo, FileMode.Open, FileAccess.Read))
				using (var table = NDbfReader.Table.Open(stream))
				{
					// Obtenemos los nombres de todas las columnas en mayúsculas para mapeo fácil
					var columnNames = table.Columns.Select(c => c.Name.ToUpper()).ToList();

					// 🚀 1. VALIDACIÓN FRONTAL (EARLY EXIT): Búsqueda del Tipo de Predio
					bool tieneTipoPredio = columnNames.Contains("TIPO_PRED") ||
										   columnNames.Contains("TIPO") ||
										   columnNames.Contains("T_PREDIO");

					if (!tieneTipoPredio)
					{
						// Inyectamos el error exacto para el correo de notificación
						resultadoFinal.ErroresDetalle.Add("Rechazo Total: El archivo DBF no contiene la columna de 'Tipo de Predio' (Urbano, Rústico, etc.), la cual es obligatoria para poder clasificar las cuentas.");

						// Marcamos los registros como fallidos preventivamente para la bitácora
						resultadoFinal.RegistrosFallidos = 1;

						LogService.WriteLogAsync("WARN", param.UsuarioLogin, "ProcesadorPagosDBF", $"Archivo DBF rechazado por falta de Tipo de Predio. Folio: {param.FolioCarga}").Wait();

						// Abortamos la misión, regresando directamente al controlador
						return resultadoFinal;
					}

					// Identificadores dinámicos de columnas
					string colCuenta = columnNames.FirstOrDefault(c => c == "NO_CONTROL" || c.Contains("CUENTA")) ?? "";
					string colBimEmi = columnNames.FirstOrDefault(c => c == "BIM_EMI" || c == "BIMESTRE") ?? "";
					string colTotal = columnNames.FirstOrDefault(c => c == "TOTAL" || c == "IMPTO" || c == "IMPTO_ORI") ?? "";
					string colFecha = columnNames.FirstOrDefault(c => c == "FECHA_PAGO" || c == "FECHA") ?? "";
					string colTipoPredio = columnNames.FirstOrDefault(c => c == "TIPO_PRED" || c == "TIPO" || c == "T_PREDIO") ?? "";

					if (string.IsNullOrEmpty(colCuenta))
					{
						resultadoFinal.ErroresDetalle.Add("Rechazo Total: No se encontró la columna de Cuenta Predial (NO_CONTROL) en el archivo DBF.");
						return resultadoFinal;
					}

					DataTable tablaCrudos = CrearEstructuraRaw();
					var reader = table.OpenReader();

					// 🚀 2. EXTRACCIÓN DE DATOS FILA POR FILA
					while (reader.Read())
					{
						string cuentaPredial = reader.GetString(colCuenta)?.Trim() ?? "";
						if (string.IsNullOrWhiteSpace(cuentaPredial)) continue;

						// Tipo de Predio (Homologación a 1, 2 y 3)
						string tipoPredioCrudo = !string.IsNullOrEmpty(colTipoPredio) ? (reader.GetString(colTipoPredio)?.Trim() ?? "") : "";
						string tipoPredio = "1"; // Default Urbano
						if (tipoPredioCrudo.ToUpper().StartsWith("U")) tipoPredio = "1";
						else if (tipoPredioCrudo.ToUpper().StartsWith("R")) tipoPredio = "2";
						else if (tipoPredioCrudo.ToUpper().StartsWith("S")) tipoPredio = "3";

						// Clase de Pago y Bimestre
						string bimEmi = !string.IsNullOrEmpty(colBimEmi) ? (reader.GetString(colBimEmi)?.Trim() ?? "0") : "0";
						string clasePago = "1"; // Asumimos 1 (Anual) si BimEmi es 0
						string bimestre = "99";

						if (bimEmi != "0" && bimEmi != "")
						{
							clasePago = "2"; // 2 (Bimestral)
							bimestre = bimEmi;
						}

						// Impuesto
						string impuestoStr = !string.IsNullOrEmpty(colTotal) ? (reader.GetString(colTotal)?.Trim() ?? "0") : "0";
						decimal.TryParse(impuestoStr, out decimal impuestoDecimal);

						// Fecha de Vigencia
						string fechaVigencia = DateTime.Now.ToString("yyyy-MM-dd");
						if (!string.IsNullOrEmpty(colFecha))
						{
							try
							{
								var fechaObj = reader.GetValue(colFecha);
								if (fechaObj is DateTime dt) fechaVigencia = dt.ToString("yyyy-MM-dd");
								else if (fechaObj != null && DateTime.TryParse(fechaObj.ToString(), out DateTime dtStr))
								{
									fechaVigencia = dtStr.ToString("yyyy-MM-dd");
								}
							}
							catch { /* Si falla la conversión de fecha, conservamos la fecha actual por defecto */ }
						}

						// 🚀 3. GENERACIÓN DE FILA EN MEMORIA
						DataRow nuevaFila = tablaCrudos.NewRow();
						nuevaFila["ClaveMunicipio"] = param.ClaveMunicipioDestino > 0 ? param.ClaveMunicipioDestino.ToString() : param.OficinaId.ToString();
						nuevaFila["TipoPredio"] = tipoPredio;
						nuevaFila["CuentaPredial"] = cuentaPredial;
						nuevaFila["ClasePago"] = clasePago;
						nuevaFila["Bimestre"] = bimestre;
						nuevaFila["ImpuestoDeterminado"] = impuestoDecimal;
						nuevaFila["FechaVigencia"] = fechaVigencia;
						nuevaFila["FolioCarga"] = param.FolioCarga.ToString();

						tablaCrudos.Rows.Add(nuevaFila);
					}

					// 🚀 4. LIMPIEZA Y EJECUCIÓN (Idéntico al procesador de Excel)
					LogService.WriteLogAsync("WARN", "SISTEMA_DEBUG", "ProcesadorPagosDBF", $"[TRACE] Lectura DBF finalizada. Filas extraídas: {tablaCrudos.Rows.Count}").Wait();

					var resultadoLimpieza = LimpiadorDatos.LimpiarYValidar(tablaCrudos, "DBF", param);

					if (resultadoLimpieza.TablaValidos.Rows.Count > 0)
					{
						List<string> erroresLogicos = await InsertarBulkAsync(resultadoLimpieza.TablaValidos, param);
						if (erroresLogicos.Any()) resultadoFinal.ErroresDetalle.AddRange(erroresLogicos);
					}

					resultadoFinal.RegistrosExitosos += resultadoLimpieza.TablaValidos.Rows.Count;
					resultadoFinal.RegistrosFallidos += resultadoLimpieza.TablaRechazados.Rows.Count;

					if (resultadoLimpieza.DetallesErrores != null && resultadoLimpieza.DetallesErrores.Any())
					{
						resultadoFinal.ErroresDetalle.AddRange(resultadoLimpieza.DetallesErrores);
					}
				}
			}
			catch (Exception ex)
			{
				LogService.WriteLogAsync("ERROR", param.UsuarioLogin, "ProcesadorPagosDBF", $"Fallo crítico al leer DBF: {ex.Message}").Wait();
				resultadoFinal.ErroresDetalle.Add("Error al intentar abrir el archivo DBF. Verifique que no esté corrupto y que esté cerrado en su computadora.");
			}

			return resultadoFinal;
		}

		private DataTable CrearEstructuraRaw()
		{
			DataTable dt = new DataTable();
			dt.Columns.Add("ClaveMunicipio", typeof(string));
			dt.Columns.Add("TipoPredio", typeof(string));
			dt.Columns.Add("CuentaPredial", typeof(string));
			dt.Columns.Add("ClasePago", typeof(string));
			dt.Columns.Add("Bimestre", typeof(string));
			dt.Columns.Add("ImpuestoDeterminado", typeof(decimal));
			dt.Columns.Add("FechaVigencia", typeof(string));
			dt.Columns.Add("FolioCarga", typeof(string));
			return dt;
		}

		private async Task<List<string>> InsertarBulkAsync(DataTable lote, ParametrosCarga param)
		{
			var erroresConsolidacion = new List<string>();

			using (SqlConnection conn = new SqlConnection(CadenaConexion))
			{
				await conn.OpenAsync();

				using (SqlBulkCopy bulkCopy = new SqlBulkCopy(conn))
				{
					bulkCopy.DestinationTableName = "pred_Operacion.Staging_Etiquetado";
					bulkCopy.BatchSize = 10000;
					bulkCopy.BulkCopyTimeout = 120;

					bulkCopy.ColumnMappings.Add("ClaveMunicipio", "ClaveMunicipio");
					bulkCopy.ColumnMappings.Add("TipoPredio", "TipoPredio");
					bulkCopy.ColumnMappings.Add("CuentaPredial", "CuentaPredial");
					bulkCopy.ColumnMappings.Add("FolioCarga", "FolioCarga");
					bulkCopy.ColumnMappings.Add("Bimestre", "Bimestre");
					bulkCopy.ColumnMappings.Add("ClasePago", "ClasePago");
					bulkCopy.ColumnMappings.Add("ImpuestoDeterminado", "ImpuestoDeterminado");
					bulkCopy.ColumnMappings.Add("FechaVigencia", "FechaVigencia");

					await bulkCopy.WriteToServerAsync(lote);
				}

				using (SqlCommand cmd = new SqlCommand("pred_Operacion.sp_ProcesarMergeEtiquetado", conn))
				{
					cmd.CommandType = CommandType.StoredProcedure;
					cmd.CommandTimeout = 180;
					cmd.Parameters.AddWithValue("@FolioCarga", param.FolioCarga);
					await cmd.ExecuteNonQueryAsync();
				}

				using (SqlCommand cmdConsolidacion = new SqlCommand("pred_Operacion.sp_ConsolidarAdeudos", conn))
				{
					cmdConsolidacion.CommandType = CommandType.StoredProcedure;
					cmdConsolidacion.CommandTimeout = 180;
					cmdConsolidacion.Parameters.AddWithValue("@FolioCarga", param.FolioCarga);

					using (var reader = await cmdConsolidacion.ExecuteReaderAsync())
					{
						while (await reader.ReadAsync())
						{
							string cuenta = reader["CuentaPredial"].ToString();
							string mensaje = reader["MensajeError"].ToString();
							erroresConsolidacion.Add($"[Consolidación] Cuenta {cuenta}: {mensaje}");
						}
					}
				}
			}

			return erroresConsolidacion;
		}
	}
}