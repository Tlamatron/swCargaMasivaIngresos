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
	public class ProcesadorPagosDBF : IProcesadorFormato
	{
		private readonly string CadenaConexion = ConfiguracionApp.ObtenerCadenaConexion();

		public async Task<ResultadoProceso> ProcesarAsync(string rutaArchivo, ParametrosCarga param)
		{
			var resultadoFinal = new ResultadoProceso { ErroresDetalle = new List<string>() };
			LogService.WriteLogAsync("INFO", param.UsuarioLogin, "ProcesadorPagosDBF", $"Iniciando Lectura de archivo DBF. Folio: {param.FolioCarga}").Wait();

			try
			{
				using (var stream = File.Open(rutaArchivo, FileMode.Open, FileAccess.Read))
				using (var table = NDbfReader.Table.Open(stream))
				{
					var columnNames = table.Columns.Select(c => c.Name.ToUpper()).ToList();

					// 🚀 1. VALIDACIÓN FRONTAL (EARLY EXIT)
					if (!columnNames.Contains("TIPO_PRED") && !columnNames.Contains("TIPO") && !columnNames.Contains("T_PREDIO"))
					{
						resultadoFinal.ErroresDetalle.Add("Rechazo Total: El archivo DBF no contiene la columna de 'Tipo de Predio' (Urbano, Rústico, etc.), obligatoria para clasificar las cuentas.");
						resultadoFinal.RegistrosFallidos = 1;
						return resultadoFinal;
					}

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

					while (reader.Read())
					{
						string cuentaPredial = reader.GetString(colCuenta)?.Trim() ?? "";
						if (string.IsNullOrWhiteSpace(cuentaPredial)) continue;

						string tipoPredioCrudo = !string.IsNullOrEmpty(colTipoPredio) ? (reader.GetString(colTipoPredio)?.Trim() ?? "") : "";
						string tipoPredio = "1";
						if (tipoPredioCrudo.ToUpper().StartsWith("U")) tipoPredio = "1";
						else if (tipoPredioCrudo.ToUpper().StartsWith("R")) tipoPredio = "2";
						else if (tipoPredioCrudo.ToUpper().StartsWith("S")) tipoPredio = "3";

						string bimEmi = !string.IsNullOrEmpty(colBimEmi) ? (reader.GetString(colBimEmi)?.Trim() ?? "0") : "0";
						string clasePago = "1";
						string bimestre = "0";

						if (bimEmi != "0" && bimEmi != "")
						{
							clasePago = "2";
							bimestre = bimEmi;
						}

						string impuestoStr = !string.IsNullOrEmpty(colTotal) ? (reader.GetString(colTotal)?.Trim() ?? "0") : "0";
						decimal.TryParse(impuestoStr, out decimal impuestoDecimal);

						string fechaVigencia = DateTime.Now.ToString("yyyy-MM-dd");
						if (!string.IsNullOrEmpty(colFecha))
						{
							try
							{
								var fechaObj = reader.GetValue(colFecha);
								if (fechaObj is DateTime dt) fechaVigencia = dt.ToString("yyyy-MM-dd");
							}
							catch { }
						}

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

					var resultadoLimpieza = LimpiadorDatos.LimpiarYValidar(tablaCrudos, "DBF", param);

					if (resultadoLimpieza.TablaValidos.Rows.Count > 0)
					{
						List<string> erroresLogicos = await InsertarBulkAsync(resultadoLimpieza.TablaValidos, param);
						if (erroresLogicos.Any()) 
						{ 
							resultadoFinal.ErroresDetalle.AddRange(erroresLogicos);
							
							// 🚀 MATEMÁTICAS HONESTAS: Convertimos los éxitos falsos en fallos reales
							resultadoFinal.RegistrosFallidos += erroresLogicos.Count;
							resultadoFinal.RegistrosExitosos -= erroresLogicos.Count;
						}
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
				LogService.WriteLogAsync("ERROR", param.UsuarioLogin, "ProcesadorPagosDBF", $"Fallo al leer DBF: {ex.Message}").Wait();
				resultadoFinal.ErroresDetalle.Add("Error al intentar abrir el archivo DBF.");
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
					foreach (DataColumn col in lote.Columns) bulkCopy.ColumnMappings.Add(col.ColumnName, col.ColumnName);
					await bulkCopy.WriteToServerAsync(lote);
				}

				using (SqlCommand cmd = new SqlCommand("pred_Operacion.sp_ProcesarMergeEtiquetado", conn))
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