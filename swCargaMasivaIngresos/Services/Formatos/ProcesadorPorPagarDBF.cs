using NDbfReader;
using swCargaMasivaIngresos.Models;
using swCargaMasivaIngresos.Services.Comunes;
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
	/// Clase encargada de procesar archivos de exclusión "Por Pagar" en formato DBF.
	/// </summary>
	public class ProcesadorPorPagarDBF : IProcesadorFormato
	{
		private readonly string CadenaConexion = ConfiguracionApp.ObtenerCadenaConexion();

		/// <summary>
		/// Procesa un archivo DBF de exclusión "Por Pagar" y lo inserta en la base de datos.
		/// </summary>
		/// <param name="rutaArchivo"></param>
		/// <param name="param"></param>
		/// <returns></returns>
		public async Task<ResultadoProceso> ProcesarAsync(string rutaArchivo, ParametrosCarga param)
		{
			var resultadoFinal = new ResultadoProceso { ErroresDetalle = new List<string>() };
			LogService.WriteLogAsync("INFO", param.UsuarioLogin, "ProcesadorPorPagarDBF", $"Iniciando Lectura de archivo DBF Por Pagar. Folio: {param.FolioCarga}").Wait();

			try
			{
				using (var stream = File.Open(rutaArchivo, FileMode.Open, FileAccess.Read))
				using (var table = NDbfReader.Table.Open(stream))
				{
					var columnNames = table.Columns.Select(c => c.Name.ToUpper()).ToList();

					if (!columnNames.Contains("TIPO_PRED") && !columnNames.Contains("TIPO") && !columnNames.Contains("T_PREDIO"))
					{
						resultadoFinal.ErroresDetalle.Add("Rechazo Total: El archivo DBF no contiene la columna de 'Tipo de Predio'.");
						resultadoFinal.RegistrosFallidos = 1;
						return resultadoFinal;
					}

					string colCuenta = columnNames.FirstOrDefault(c => c == "NO_CONTROL" || c.Contains("CUENTA")) ?? "";
					string colTipoPredio = columnNames.FirstOrDefault(c => c == "TIPO_PRED" || c == "TIPO" || c == "T_PREDIO") ?? "";

					if (string.IsNullOrEmpty(colCuenta))
					{
						resultadoFinal.ErroresDetalle.Add("Rechazo Total: No se encontró la columna de Cuenta Predial (NO_CONTROL) en el archivo DBF.");
						return resultadoFinal;
					}

					DataTable tablaCrudos = CrearEstructuraPorPagar();
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

						DataRow nuevaFila = tablaCrudos.NewRow();
						nuevaFila["ClaveMunicipio"] = param.ClaveMunicipioDestino > 0 ? param.ClaveMunicipioDestino.ToString() : param.OficinaId.ToString();
						nuevaFila["TipoPredio"] = tipoPredio;
						nuevaFila["CuentaPredial"] = cuentaPredial;
						nuevaFila["Bimestre"] = "0"; // Asumimos anual por defecto en exclusión si no lo indican
						nuevaFila["FolioCarga"] = param.FolioCarga;
						nuevaFila["ImpuestoDeterminado"] = 0m;
						nuevaFila["ClasePago"] = "1";
						nuevaFila["FechaVigencia"] = DateTime.Now.ToString("yyyy-MM-dd");
						nuevaFila["IdControl"] = DBNull.Value;
						nuevaFila["FolioEmision"] = DBNull.Value;

						tablaCrudos.Rows.Add(nuevaFila);
					}

					if (tablaCrudos.Rows.Count > 0)
					{
						List<string> alertasSP = await InsertarBulkAsync(tablaCrudos, param);
						if (alertasSP.Any())
						{
							int erroresReales = 0;
							foreach (var msg in alertasSP)
							{
								resultadoFinal.ErroresDetalle.Add(msg);
								if (msg.Contains("Error") || msg.Contains("Aviso") || msg.Contains("Bloqueo"))
								{
									erroresReales++;
								}
							}
							resultadoFinal.RegistrosFallidos += erroresReales;
							resultadoFinal.RegistrosExitosos += (tablaCrudos.Rows.Count - erroresReales);
						}
						else
						{
							resultadoFinal.RegistrosExitosos += tablaCrudos.Rows.Count;
						}
					}
				}
			}
			catch (Exception ex)
			{
				LogService.WriteLogAsync("ERROR", param.UsuarioLogin, "ProcesadorPorPagarDBF", $"Fallo al leer DBF: {ex.Message}").Wait();
				resultadoFinal.ErroresDetalle.Add("Error al intentar abrir el archivo DBF.");
			}
			return resultadoFinal;
		}

		private DataTable CrearEstructuraPorPagar()
		{
			var tabla = new DataTable();
			tabla.Columns.Add("ClaveMunicipio", typeof(string));
			tabla.Columns.Add("TipoPredio", typeof(string));
			tabla.Columns.Add("CuentaPredial", typeof(string));
			tabla.Columns.Add("Bimestre", typeof(string));
			tabla.Columns.Add("FolioCarga", typeof(int));
			tabla.Columns.Add("ImpuestoDeterminado", typeof(decimal));
			tabla.Columns.Add("ClasePago", typeof(string));
			tabla.Columns.Add("FechaVigencia", typeof(string));
			tabla.Columns.Add("IdControl", typeof(int));
			tabla.Columns.Add("FolioEmision", typeof(int));
			return tabla;
		}

		private async Task<List<string>> InsertarBulkAsync(DataTable lote, ParametrosCarga param)
		{
			var alertas = new List<string>();
			string usuarioLogin = param.UsuarioLogin;
			using (SqlConnection conn = new SqlConnection(CadenaConexion))
			{
				await conn.OpenAsync();
				using (SqlBulkCopy bulkCopy = new SqlBulkCopy(conn))
				{
					bulkCopy.DestinationTableName = "pred.p_staging_porpagar";
					bulkCopy.BatchSize = 10000;
					bulkCopy.ColumnMappings.Add("ClaveMunicipio", "ClaveMunicipio");
					bulkCopy.ColumnMappings.Add("TipoPredio", "TipoPredio");
					bulkCopy.ColumnMappings.Add("CuentaPredial", "CuentaPredial");
					bulkCopy.ColumnMappings.Add("Bimestre", "Bimestre");
					bulkCopy.ColumnMappings.Add("FolioCarga", "FolioCarga");
					bulkCopy.ColumnMappings.Add("ImpuestoDeterminado", "ImpuestoDeterminado");
					bulkCopy.ColumnMappings.Add("ClasePago", "ClasePago");
					bulkCopy.ColumnMappings.Add("FechaVigencia", "FechaVigencia");
					bulkCopy.ColumnMappings.Add("IdControl", "IdControl");
					bulkCopy.ColumnMappings.Add("FolioEmision", "FolioEmision");

					await bulkCopy.WriteToServerAsync(lote);
				}

				using (SqlCommand cmd = new SqlCommand("pred.sp_ProcesarMergePorPagar", conn))
				{
					cmd.CommandType = CommandType.StoredProcedure;
					cmd.CommandTimeout = 180;
					cmd.Parameters.AddWithValue("@FolioCarga", param.FolioCarga);
					using (var reader = await cmd.ExecuteReaderAsync())
					{
						while (await reader.ReadAsync())
						{
							string cuenta = reader["CuentaPredial"].ToString();
							string mensaje = reader["MensajeError"].ToString();
							alertas.Add($"[Exclusión] Cuenta {cuenta}: {mensaje}");
						}
					}
				}
			}
			return alertas;
		}
	}
}