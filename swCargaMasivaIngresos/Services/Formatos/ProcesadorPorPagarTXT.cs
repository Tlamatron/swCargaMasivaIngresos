using swCargaMasivaIngresos.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Linq;

namespace swCargaMasivaIngresos.Services.Formatos
{
	/// <summary>
	/// Clase encargada de procesar archivos de texto plano (TXT o CSV) de exclusión "Por Pagar".
	/// </summary>
	public class ProcesadorPorPagarTXT : IProcesadorFormato
	{
		private readonly string CadenaConexion = ConfiguracionApp.ObtenerCadenaConexion();

		/// <summary>
		/// Procesa un archivo de texto plano (TXT o CSV) que contiene cuentas "Por Pagar" y las inserta en la base de datos.
		/// </summary>
		/// <param name="rutaArchivo"></param>
		/// <param name="param"></param>
		/// <returns></returns>
		public async Task<ResultadoProceso> ProcesarAsync(string rutaArchivo, ParametrosCarga param)
		{
			var resultado = new ResultadoProceso { ErroresDetalle = new List<string>() };
			DataTable tablaLote = CrearEstructuraPorPagar();

			LogService.WriteLogAsync("INFO", param.UsuarioLogin, "ProcesadorPorPagarTXT", $"Inicia lectura de archivo Por Pagar (TXT/CSV). Folio: {param.FolioCarga}").Wait();

			using (var reader = new StreamReader(rutaArchivo, Encoding.UTF8))
			{
				string linea;
				int numeroLinea = 0;
				char delimitador = '|';
				bool delimitadorDetectado = false;

				while ((linea = await reader.ReadLineAsync()) != null)
				{
					numeroLinea++;
					if (string.IsNullOrWhiteSpace(linea)) continue;

					if (!delimitadorDetectado)
					{
						if (linea.Contains("|")) delimitador = '|';
						else if (linea.Contains(",")) delimitador = ',';
						else if (linea.Contains("\t")) delimitador = '\t';
						delimitadorDetectado = true;
					}

					string[] col = linea.Split(delimitador);

					// Validar Layout (Permitir el estricto de 24 columnas o el reducido de mínimo 3 columnas)
					if (col.Length != 24 && col.Length < 3)
					{
						MarcarError(resultado, numeroLinea, $"Columnas incorrectas. Se esperaban 24 o al menos 3, llegaron {col.Length} usando el separador '{delimitador}'.");
						continue;
					}

					string claveMunicipio = col[0].Trim();
					string tipoPredio = col[1].Trim();
					string cuentaPredial = col[2].Trim();

					string clasePagoStr = "1";
					string strBimestre = "0";
					string impuestoDeterminadoStr = "0";

					// Extracción dinámica si vienen más columnas
					if (col.Length == 24)
					{
						clasePagoStr = col[20].Trim();
						strBimestre = col[21].Trim();
						impuestoDeterminadoStr = col[22].Trim();
					}
					else if (col.Length >= 5 && col.Length < 24)
					{
						clasePagoStr = col[3].Trim();
						strBimestre = col[4].Trim();
						if (col.Length >= 6) impuestoDeterminadoStr = col[5].Trim();
					}

					if (string.IsNullOrEmpty(cuentaPredial))
					{
						MarcarError(resultado, numeroLinea, "La Cuenta Predial es obligatoria.");
						continue;
					}

					if (!short.TryParse(claveMunicipio, out short claveMun) || claveMun < 1 || claveMun > 217)
					{
						if (param.ClaveMunicipioDestino > 0) claveMun = (short)param.ClaveMunicipioDestino;
						else
						{
							MarcarError(resultado, numeroLinea, "Clave de municipio inválida (1 a 217).");
							continue;
						}
					}

					if (!byte.TryParse(tipoPredio, out byte tipoPre) || tipoPre < 1 || tipoPre > 3)
					{
						MarcarError(resultado, numeroLinea, "Tipo de predio inválido (1=Urbano, 2=Rústico, 3=Suburbano).");
						continue;
					}

					byte clasePago = 1;
					byte bimestre = 0;

					if (!string.IsNullOrWhiteSpace(clasePagoStr))
					{
						string cpUpper = clasePagoStr.ToUpper();
						if (cpUpper == "ANUAL" || cpUpper == "A") clasePagoStr = "1";
						else if (cpUpper == "BIMESTRAL" || cpUpper == "B" || cpUpper.StartsWith("BIM")) clasePagoStr = "2";

						if (byte.TryParse(clasePagoStr, out byte cp) && (cp == 1 || cp == 2)) clasePago = cp;
					}

					if (!string.IsNullOrWhiteSpace(strBimestre))
					{
						if (byte.TryParse(strBimestre, out byte bim) && bim <= 6) bimestre = bim;
					}

					decimal impuestoDeterminadoDec = 0m;
					if (!string.IsNullOrWhiteSpace(impuestoDeterminadoStr)) decimal.TryParse(impuestoDeterminadoStr, out impuestoDeterminadoDec);

					tablaLote.Rows.Add(
						claveMun.ToString(),
						tipoPre.ToString(),
						cuentaPredial,
						bimestre.ToString(),
						param.FolioCarga,
						impuestoDeterminadoDec,
						clasePago.ToString(),
						DateTime.Now.ToString("yyyy-MM-dd"),
						DBNull.Value,
						DBNull.Value
					);

					resultado.RegistrosExitosos++;

					if (tablaLote.Rows.Count >= 10000)
					{
						List<string> alertasSP = await InsertarLoteEnBDAsync(tablaLote, param);
						ProcesarAlertas(alertasSP, resultado, tablaLote.Rows.Count);
						tablaLote.Clear();
					}
				}

				if (tablaLote.Rows.Count > 0)
				{
					List<string> alertasSP = await InsertarLoteEnBDAsync(tablaLote, param);
					ProcesarAlertas(alertasSP, resultado, tablaLote.Rows.Count);
				}
			}
			return resultado;
		}

		private void MarcarError(ResultadoProceso res, int linea, string msg)
		{
			res.RegistrosFallidos++;
			res.ErroresDetalle.Add($"Línea {linea}: {msg}");
		}

		private void ProcesarAlertas(List<string> alertasSP, ResultadoProceso resultado, int regsEnLote)
		{
			if (alertasSP.Any())
			{
				int erroresReales = 0;
				foreach (var msg in alertasSP)
				{
					resultado.ErroresDetalle.Add(msg);
					if (msg.Contains("Error") || msg.Contains("Aviso") || msg.Contains("Bloqueo"))
					{
						erroresReales++;
					}
				}
				resultado.RegistrosFallidos += erroresReales;
				resultado.RegistrosExitosos -= erroresReales;
			}
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

		private async Task<List<string>> InsertarLoteEnBDAsync(DataTable lote, ParametrosCarga param)
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
					bulkCopy.BulkCopyTimeout = 120;

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