using ExcelDataReader;
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
	/// Clase encargada de procesar archivos de exclusión "Por Pagar".
	/// Extrae las cuentas pendientes reportadas por el municipio para que el sistema asuma que el resto ya fue pagado.
	/// </summary>
	public class ProcesadorPorPagarExcel : IProcesadorFormato
	{
		private readonly string CadenaConexion = ConfiguracionApp.ObtenerCadenaConexion();

		/// <summary>
		/// Procesa un archivo Excel de exclusión "Por Pagar" y lo inserta en la base de datos.
		/// </summary>
		/// <param name="rutaArchivo"></param>
		/// <param name="param"></param>
		/// <returns></returns>
		public async Task<ResultadoProceso> ProcesarAsync(string rutaArchivo, ParametrosCarga param)
		{
			var resultadoFinal = new ResultadoProceso { ErroresDetalle = new List<string>() };

			LogService.WriteLogAsync("INFO", param.UsuarioLogin, "ProcesadorPorPagarExcel", $"Iniciando Lectura de archivo Por Pagar. Folio: {param.FolioCarga}").Wait();

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
						DataTable tablaCrudos = CrearEstructuraPorPagar();

						for (int i = filaInicioDatos; i < tablaExcel.Rows.Count; i++)
						{
							var fila = tablaExcel.Rows[i];
							if (string.IsNullOrWhiteSpace(string.Join("", fila.ItemArray))) continue;

							// 1. Extracción de Llaves Primarias
							string cuentaPredial = ExtraerSeguro(fila, mapaBloqueado, "CuentaPredial", "");
							if (string.IsNullOrWhiteSpace(cuentaPredial) || cuentaPredial.Equals("Cuenta", StringComparison.OrdinalIgnoreCase)) continue;
							if (cuentaPredial.EndsWith(".0")) cuentaPredial = cuentaPredial.Replace(".0", "");

							string tipoPredio = ExtraerSeguro(fila, mapaBloqueado, "TipoPredio", "").ToUpper().Trim();
							if (tipoPredio == "U" || tipoPredio.StartsWith("URBANO")) tipoPredio = "1";
							else if (tipoPredio == "R" || tipoPredio.StartsWith("RUSTICO") || tipoPredio.StartsWith("RÚSTICO")) tipoPredio = "2";
							else if (tipoPredio == "S" || tipoPredio.StartsWith("SUBURBANO") || tipoPredio.StartsWith("SUB")) tipoPredio = "3";
							if (string.IsNullOrWhiteSpace(tipoPredio)) tipoPredio = "1";

							string claveMunicipio = ExtraerSeguro(fila, mapaBloqueado, "ClaveMunicipio", "");
							if (string.IsNullOrWhiteSpace(claveMunicipio) && param != null)
							{
								claveMunicipio = param.ClaveMunicipioDestino > 0 ? param.ClaveMunicipioDestino.ToString() : param.OficinaId.ToString();
							}

							if (!short.TryParse(claveMunicipio, out short claveMun) || claveMun < 1 || claveMun > 217)
							{
								resultadoFinal.RegistrosFallidos++;
								resultadoFinal.ErroresDetalle.Add($"Fila {i + 1}: Clave de municipio '{claveMunicipio}' inválida.");
								continue;
							}

							// 2. Extracción de Metadatos (Opcionales en este tipo de carga, pero útiles)
							string fechaVigencia = ExtraerSeguro(fila, mapaBloqueado, "FechaVigencia", "");
							if (string.IsNullOrWhiteSpace(fechaVigencia)) fechaVigencia = new DateTime(DateTime.Now.Year, 12, 31).ToString("yyyy-MM-dd");

							DataRow nuevaFila = tablaCrudos.NewRow();
							nuevaFila["ClaveMunicipio"] = claveMun.ToString();
							nuevaFila["TipoPredio"] = tipoPredio;
							nuevaFila["CuentaPredial"] = cuentaPredial;
							nuevaFila["Bimestre"] = ExtraerSeguro(fila, mapaBloqueado, "Bimestre", "0");
							nuevaFila["FolioCarga"] = param.FolioCarga;

							string imp = ExtraerSeguro(fila, mapaBloqueado, "ImpuestoDeterminado", "0");
							decimal.TryParse(imp.Replace("$", "").Replace(",", ""), out decimal impDec);
							nuevaFila["ImpuestoDeterminado"] = impDec;

							nuevaFila["ClasePago"] = ExtraerSeguro(fila, mapaBloqueado, "ClasePago", "1");
							nuevaFila["FechaVigencia"] = fechaVigencia;

							if (int.TryParse(ExtraerSeguro(fila, mapaBloqueado, "IdControl", ""), out int idCtrl)) nuevaFila["IdControl"] = idCtrl;
							else nuevaFila["IdControl"] = DBNull.Value;

							if (int.TryParse(ExtraerSeguro(fila, mapaBloqueado, "FolioEmision", ""), out int folEmi)) nuevaFila["FolioEmision"] = folEmi;
							else nuevaFila["FolioEmision"] = DBNull.Value;

							tablaCrudos.Rows.Add(nuevaFila);
						}

						// 3. Volcado a Base de Datos
						if (tablaCrudos.Rows.Count > 0)
						{
							List<string> alertasSP = await InsertarBulkAsync(tablaCrudos, param);

							if (alertasSP.Any())
							{
								int erroresReales = 0;
								foreach (var msg in alertasSP)
								{
									resultadoFinal.ErroresDetalle.Add(msg);

									// Matemáticas honestas: Discriminamos las notificaciones positivas de los errores/avisos reales
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
			}
			catch (Exception ex)
			{
				LogService.WriteLogAsync("ERROR", param.UsuarioLogin, "ProcesadorPorPagarExcel", $"Fallo crítico: {ex.Message}").Wait();
				throw;
			}

			return resultadoFinal;
		}

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
					// 🚀 NOMBRE EXACTO DE LA TABLA EN SQL
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