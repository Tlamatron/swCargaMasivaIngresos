using swCargaMasivaIngresos.Models;
using swCargaMasivaIngresos.Services.Comunes;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace swCargaMasivaIngresos.Services
{
	/// <summary>
	/// Clase encargada de procesar archivos de pagos en formato TXT o CSV de manera universal. 
	/// Implementa la interfaz IProcesadorFormato de forma asíncrona.
	/// </summary>
	public class ProcesadorPagosUniversal : IProcesadorFormato
	{
		private readonly string CadenaConexion = ConfiguracionApp.ObtenerCadenaConexion();

		/// <summary>
		/// Método principal asíncrono para procesar un archivo de pagos en formato TXT o CSV. 
		/// Lee el archivo, mapea los encabezados, limpia, valida e inserta/consolida los registros válidos.
		/// </summary>
		public async Task<ResultadoProceso> ProcesarAsync(string rutaArchivo, ParametrosCarga param)
		{
			var resultadoFinal = new ResultadoProceso { ErroresDetalle = new List<string>() };
			string extension = System.IO.Path.GetExtension(rutaArchivo);

			LogService.WriteLogAsync("WARN", param.UsuarioLogin, "ProcesadorPagosUniversal", $"[TRACE] Iniciando lectura inteligente. Folio: {param.FolioCarga}").Wait();

			try
			{
				var hojasLeidas = LectorUniversal.LeerArchivo(rutaArchivo, extension);

				foreach (var hoja in hojasLeidas)
				{
					if (hoja.ErroresEstructurales.Any())
					{
						resultadoFinal.ErroresDetalle.AddRange(hoja.ErroresEstructurales);
						continue;
					}

					var mapaCrudo = MapeadorInteligente.ObtenerMapaPorRegiones(hoja.TablaCruda, out int filaInicioDatos);

					// ==============================================================================
					// 🚀 INFERENCIA DE CONTEXTO DESDE EL NOMBRE DEL ARCHIVO (Caso Oriental)
					// ==============================================================================
					string contextoUpper = hoja.ContextoPestaña.ToUpper();
					string clasePagoInferida = "99";
					string bimestreInferido = "99";
					string tipoPredioInferido = "";

					if (contextoUpper.Contains("ANUAL"))
					{
						clasePagoInferida = "1";
						bimestreInferido = "0";
					}
					else if (contextoUpper.Contains("BIMESTRE") || contextoUpper.Contains("BIM"))
					{
						clasePagoInferida = "2";
						for (int b = 1; b <= 6; b++)
						{
							if (contextoUpper.Contains($"{b}BIMESTRE") || contextoUpper.Contains($"BIMESTRE {b}") || contextoUpper.Contains($"BIMESTRE{b}") || contextoUpper.StartsWith($"{b}"))
							{
								bimestreInferido = b.ToString();
								break;
							}
						}
					}

					if (contextoUpper.Contains("URBANO") && !contextoUpper.Contains("SUB")) tipoPredioInferido = "1";
					else if (contextoUpper.Contains("RUSTICO") || contextoUpper.Contains("RÚSTICO")) tipoPredioInferido = "2";
					else if (contextoUpper.Contains("SUB-URBANO") || contextoUpper.Contains("SUBURBANO") || contextoUpper.Contains("SUB")) tipoPredioInferido = "3";

					// ==============================================================================
					// 🚀 PRE-ESCANEO DE ESTATUS (Filtro Inteligente de Oriental)
					// ==============================================================================
					bool archivoUsaBanderaPagado = false;
					foreach (DataRow r in hoja.TablaCruda.Rows)
					{
						if (r.ItemArray.Any(x => x?.ToString().Trim().ToUpper() == "PAGADO"))
						{
							archivoUsaBanderaPagado = true;
							break;
						}
					}

					if (mapaCrudo.Count == 0)
					{
						// PLAN B: Detección de Archivo Crudo sin Encabezados (Layout estricto)
						if (hoja.TablaCruda.Columns.Count >= 5 && hoja.TablaCruda.Rows.Count > 0)
						{
							var primeraFila = hoja.TablaCruda.Rows[0];
							int celdasNumericas = primeraFila.ItemArray.Take(5).Count(x => decimal.TryParse(x?.ToString(), out _));

							if (celdasNumericas >= 3)
							{
								LogService.WriteLogAsync("WARN", param.UsuarioLogin, "ProcesadorPagosUniversal", $"[TRACE] Aplicando Plan B (Mapeo Fijo Sin Encabezados) para pestaña {hoja.ContextoPestaña}").Wait();

								mapaCrudo = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
								{
									{ "CLAVE DEL MUNICIPIO", 0 },
									{ "TIPO DE PREDIO", 1 },
									{ "CUENTA PREDIAL", 2 },
									{ "CLASE DE PAGO", 3 },
									{ "BIMESTRE", 4 }
								};

								if (hoja.TablaCruda.Columns.Count >= 6 && decimal.TryParse(primeraFila.ItemArray[5]?.ToString(), out _))
								{
									mapaCrudo.Add("IMPUESTO DETERMINADO", 5);
								}

								filaInicioDatos = 0;
							}
						}

						if (mapaCrudo.Count == 0)
						{
							resultadoFinal.ErroresDetalle.Add($"No se encontraron encabezados válidos en la pestaña: {hoja.ContextoPestaña}. El archivo no cumple con el formato.");
							continue;
						}
					}

					var mapaBloqueado = MapeadorInteligente.ProcesarEncabezadosConMemoria(mapaCrudo);
					DataTable tablaCrudos = CrearEstructuraRaw();

					for (int i = filaInicioDatos; i < hoja.TablaCruda.Rows.Count; i++)
					{
						var fila = hoja.TablaCruda.Rows[i];
						if (string.IsNullOrWhiteSpace(string.Join("", fila.ItemArray))) continue;

						// 🚀 REGLA ORIENTAL: Filtro Inteligente de Pagados
						if (archivoUsaBanderaPagado)
						{
							bool filaEstaPagada = fila.ItemArray.Any(x => x?.ToString().Trim().ToUpper() == "PAGADO");
							if (!filaEstaPagada) continue;
						}

						string cuentaPredial = ExtraerSeguro(fila, mapaBloqueado, "CuentaPredial", "");
						if (string.IsNullOrWhiteSpace(cuentaPredial) || cuentaPredial.Equals("Cuenta", StringComparison.OrdinalIgnoreCase)) continue;
						if (cuentaPredial.EndsWith(".0")) cuentaPredial = cuentaPredial.Replace(".0", "");

						string anioPredialStr = ExtraerSeguro(fila, mapaBloqueado, "Anio", "");
						int anioFiscal = DateTime.Now.Year;

						var matchAnio = System.Text.RegularExpressions.Regex.Match(anioPredialStr, @"\b(19\d\d|20\d\d)\b");
						if (matchAnio.Success)
						{
							anioFiscal = int.Parse(matchAnio.Value);
						}

						// 🚀 CASCADA DE INFERENCIA ENRIQUECIDA
						string tipoPredio = ExtraerSeguro(fila, mapaBloqueado, "TipoPredio", "").ToUpper().Trim();
						if (string.IsNullOrWhiteSpace(tipoPredio)) tipoPredio = tipoPredioInferido;
						if (tipoPredio == "U" || tipoPredio.StartsWith("URBANO")) tipoPredio = "1";
						else if (tipoPredio == "R" || tipoPredio.StartsWith("RUSTICO") || tipoPredio.StartsWith("RÚSTICO")) tipoPredio = "2";
						else if (tipoPredio == "S" || tipoPredio.StartsWith("SUBURBANO") || tipoPredio.StartsWith("SUB") || tipoPredio == "S-URB" || tipoPredio.Contains("-URB") || tipoPredio == "S_URB" || tipoPredio.Contains("_URB")) tipoPredio = "3";
						if (string.IsNullOrWhiteSpace(tipoPredio) || (tipoPredio != "1" && tipoPredio != "2" && tipoPredio != "3")) tipoPredio = "1";

						string clasePago = ExtraerSeguro(fila, mapaBloqueado, "ClasePago", "99");
						if (!string.IsNullOrWhiteSpace(clasePago) && clasePago != "99")
						{
							string cpUpper = clasePago.ToUpper();
							if (cpUpper == "ANUAL" || cpUpper == "A" || cpUpper.Contains("ANUAL")) clasePago = "1";
							else if (cpUpper == "BIMESTRAL" || cpUpper == "B" || cpUpper.StartsWith("BIM")) clasePago = "2";
						}
						if (string.IsNullOrWhiteSpace(clasePago) || clasePago == "99") clasePago = clasePagoInferida;

						string bimestre = MapeadorInteligente.RastrearBimestres(fila, mapaBloqueado);
						if (string.IsNullOrWhiteSpace(bimestre) || bimestre == "99") bimestre = bimestreInferido;

						// 🚀 EL PARACAÍDAS DEFINITIVO
						if (clasePago == "99" || string.IsNullOrWhiteSpace(clasePago))
						{
							clasePago = "1";
							if (bimestre == "99" || string.IsNullOrWhiteSpace(bimestre)) bimestre = "0";
						}

						string claveMunicipio = ExtraerSeguro(fila, mapaBloqueado, "ClaveMunicipio", "");
						if (string.IsNullOrWhiteSpace(claveMunicipio) && param != null)
						{
							claveMunicipio = param.ClaveMunicipioDestino > 0 ? param.ClaveMunicipioDestino.ToString() : param.OficinaId.ToString();
						}

						string fechaVigencia = ExtraerSeguro(fila, mapaBloqueado, "FechaVigencia", "").Trim();
						if (string.IsNullOrWhiteSpace(fechaVigencia)) fechaVigencia = new DateTime(anioFiscal, 12, 31).ToString("yyyy-MM-dd");
						else if (double.TryParse(fechaVigencia, out double diasExcel) && diasExcel > 10000 && !fechaVigencia.Contains("-") && !fechaVigencia.Contains("/")) fechaVigencia = DateTime.FromOADate(diasExcel).ToString("yyyy-MM-dd");
						else if (DateTime.TryParse(fechaVigencia, new System.Globalization.CultureInfo("es-MX"), System.Globalization.DateTimeStyles.None, out DateTime fechaParseada)) fechaVigencia = fechaParseada.ToString("yyyy-MM-dd");
						else fechaVigencia = new DateTime(anioFiscal, 12, 31).ToString("yyyy-MM-dd");

						DataRow nuevaFila = tablaCrudos.NewRow();
						nuevaFila["ClaveMunicipio"] = claveMunicipio;
						nuevaFila["TipoPredio"] = tipoPredio;
						nuevaFila["CuentaPredial"] = cuentaPredial;
						nuevaFila["ClasePago"] = clasePago;
						nuevaFila["Bimestre"] = bimestre;
						nuevaFila["ImpuestoDeterminado"] = ExtraerSeguro(fila, mapaBloqueado, "ImpuestoDeterminado", "0");
						nuevaFila["FechaVigencia"] = fechaVigencia;
						nuevaFila["FolioCarga"] = param.FolioCarga.ToString();

						if (int.TryParse(ExtraerSeguro(fila, mapaBloqueado, "IdControl", ""), out int idCtrl)) nuevaFila["IdControl"] = idCtrl;
						else nuevaFila["IdControl"] = DBNull.Value;

						if (int.TryParse(ExtraerSeguro(fila, mapaBloqueado, "FolioEmision", ""), out int folEmi)) nuevaFila["FolioEmision"] = folEmi;
						else nuevaFila["FolioEmision"] = DBNull.Value;

						tablaCrudos.Rows.Add(nuevaFila);
					}

					var resultadoLimpieza = LimpiadorDatos.LimpiarYValidar(tablaCrudos, hoja.ContextoPestaña, param);

					if (resultadoLimpieza.TablaValidos.Rows.Count > 0)
					{
						List<string> erroresLogicos = await InsertarBulkAsync(resultadoLimpieza.TablaValidos, param);

						if (erroresLogicos.Any())
						{
							resultadoFinal.ErroresDetalle.AddRange(erroresLogicos);
							resultadoFinal.RegistrosFallidos += erroresLogicos.Count;
							resultadoFinal.RegistrosExitosos -= erroresLogicos.Count;
						}
					}

					resultadoFinal.RegistrosExitosos += resultadoLimpieza.TablaValidos.Rows.Count;
					resultadoFinal.RegistrosFallidos += resultadoLimpieza.TablaRechazados.Rows.Count;

					if (resultadoLimpieza.DetallesErrores.Any())
					{
						resultadoFinal.ErroresDetalle.AddRange(resultadoLimpieza.DetallesErrores);
					}
				}
			}
			catch (Exception ex)
			{
				LogService.WriteLogAsync("ERROR", param.UsuarioLogin, "ProcesadorPagosUniversal", $"Fallo crítico: {ex.Message}").Wait();
				throw;
			}

			return resultadoFinal;
		}

		/// <summary>
		/// Extrae de manera segura un valor de una fila de datos según el mapa oficial, devolviendo un valor por defecto si ocurre algún error o si el valor es nulo o vacío.
		/// </summary>
		/// <param name="fila"></param>
		/// <param name="mapa"></param>
		/// <param name="columna"></param>
		/// <param name="valorPorDefecto"></param>
		/// <returns></returns>
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
		/// Crea la estructura de un DataTable para almacenar temporalmente los datos de pagos antes de ser insertados en la base de datos.
		/// </summary>
		/// <returns></returns>
		private DataTable CrearEstructuraRaw()
		{
			DataTable dt = new DataTable();
			dt.Columns.Add("ClaveMunicipio", typeof(string));
			dt.Columns.Add("TipoPredio", typeof(string));
			dt.Columns.Add("CuentaPredial", typeof(string));
			dt.Columns.Add("ClasePago", typeof(string));
			dt.Columns.Add("Bimestre", typeof(string));
			dt.Columns.Add("ImpuestoDeterminado", typeof(string));
			dt.Columns.Add("FechaVigencia", typeof(string));
			dt.Columns.Add("FolioCarga", typeof(string));
			dt.Columns.Add("IdControl", typeof(int));
			dt.Columns.Add("FolioEmision", typeof(int));
			return dt;
		}

		/// <summary>
		/// Inserta asíncronamente los registros en la base de datos (Staging), llama al SP de ingesta, 
		/// ejecuta el SP de Consolidación de adeudos y captura cualquier error lógico devuelto.
		/// </summary>
		private async Task<List<string>> InsertarBulkAsync(DataTable lote, ParametrosCarga param)
		{
			var erroresConsolidacion = new List<string>();

			string usuarioLogin = ContextoGlobal.UsuarioActual;

			SeguridadService segService = new SeguridadService();
			int appId = Convert.ToInt32(System.Configuration.ConfigurationManager.AppSettings["AppId"] ?? "1");
			bool estaAutorizado = await segService.TienePermisoEjecucionAsync(usuarioLogin, "pred_Operacion.sp_ProcesarMergeEtiquetado", CadenaConexion, appId);

			if (!estaAutorizado)
			{
				await LogService.WriteLogAsync("ERROR", usuarioLogin, "ProcesadorPagosUniversal", $"El usuario {usuarioLogin} intentó ejecutar pred_Operacion.sp_ProcesarMergeEtiquetado sin permisos.");

				throw new UnauthorizedAccessException("Acceso denegado. No tienes los roles necesarios para ejecutar esta operación.");
			}
			estaAutorizado = await segService.TienePermisoEjecucionAsync(usuarioLogin, "pred_Operacion.sp_ConsolidarAdeudos", CadenaConexion, appId);

			if (!estaAutorizado)
			{
				await LogService.WriteLogAsync("ERROR", usuarioLogin, "ProcesadorPagosUniversal", $"El usuario {usuarioLogin} intentó ejecutar pred_Operacion.sp_ConsolidarAdeudos sin permisos.");

				throw new UnauthorizedAccessException("Acceso denegado. No tienes los roles necesarios para ejecutar esta operación.");
			}

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
					bulkCopy.ColumnMappings.Add("IdControl", "IdControl");
					bulkCopy.ColumnMappings.Add("FolioEmision", "FolioEmision");

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