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
	/// Clase encargada de procesar archivos Excel que contienen información de pagos. Implementa la interfaz IProcesadorFormato y proporciona métodos para leer, limpiar y validar los datos del archivo, así como para insertar los registros válidos en la base de datos mediante operaciones bulk.
	/// </summary>
	public class ProcesadorPagosExcel : IProcesadorFormato
	{
		private readonly string CadenaConexion = ConfiguracionApp.ObtenerCadenaConexion();

		/// <summary>
		/// Procesa un archivo Excel que contiene información de pagos, limpiando y validando los datos antes de insertarlos en la base de datos. Se infiere el contexto de cada pestaña del archivo (como clase de pago, bimestre y tipo de predio) a partir del nombre de la pestaña y los datos contenidos en ella.
		/// </summary>
		/// <param name="rutaArchivo"></param>
		/// <param name="param"></param>
		/// <returns></returns>
		public async Task<ResultadoProceso> ProcesarAsync(string rutaArchivo, ParametrosCarga param)
		{
			var resultadoFinal = new ResultadoProceso { ErroresDetalle = new List<string>() };

			LogService.WriteLogAsync("INFO", param.UsuarioLogin, "ProcesadorPagosExcel", $"Iniciando Lectura por Regiones. Folio: {param.FolioCarga}").Wait();

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
						string nombrePestaña = tablaExcel.TableName;
						string pestanaUpper = nombrePestaña.ToUpper();

						// 🚀 INFERENCIA DE CONTEXTO DESDE EL NOMBRE DE LA PESTAÑA
						string clasePagoInferida = "99";
						string bimestreInferido = "99";
						string tipoPredioInferido = "";

						if (pestanaUpper.Contains("ANUAL"))
						{
							clasePagoInferida = "1";
							bimestreInferido = "0";
						}
						else if (pestanaUpper.Contains("BIMESTRE") || pestanaUpper.Contains("BIM"))
						{
							clasePagoInferida = "2";
							for (int b = 1; b <= 6; b++)
							{
								if (pestanaUpper.Contains($"{b}BIMESTRE") || pestanaUpper.Contains($"BIMESTRE {b}") || pestanaUpper.Contains($"BIMESTRE{b}"))
								{
									bimestreInferido = b.ToString();
									break;
								}
							}
						}

						if (pestanaUpper.Contains("URBANO") && !pestanaUpper.Contains("SUB")) tipoPredioInferido = "1";
						else if (pestanaUpper.Contains("RUSTICO") || pestanaUpper.Contains("RÚSTICO")) tipoPredioInferido = "2";
						else if (pestanaUpper.Contains("SUB-URBANO") || pestanaUpper.Contains("SUBURBANO") || pestanaUpper.Contains("SUB")) tipoPredioInferido = "3";

						int filaInicioDatos;
						var mapaCrudo = MapeadorInteligente.ObtenerMapaPorRegiones(tablaExcel, out filaInicioDatos);

						if (mapaCrudo.Count == 0) continue;

						// 🚀 NUEVA INFERENCIA: LEER EL MEMBRETE/ENCABEZADOS DEL ARCHIVO
						if (filaInicioDatos > 0)
						{
							string textoEncabezadoGlobal = "";
							for (int r = 0; r < filaInicioDatos; r++)
							{
								var rowInfo = tablaExcel.Rows[r].ItemArray.Select(x => x?.ToString().Trim().ToUpper() ?? "");
								textoEncabezadoGlobal += " " + string.Join(" ", rowInfo);
							}

							if (clasePagoInferida == "99")
							{
								if (textoEncabezadoGlobal.Contains("BIMESTRAL") || textoEncabezadoGlobal.Contains("BIMESTRE") || textoEncabezadoGlobal.Contains("BIM"))
								{
									clasePagoInferida = "2";
								}
								else if (textoEncabezadoGlobal.Contains("ANUAL"))
								{
									clasePagoInferida = "1";
								}
							}

							if (textoEncabezadoGlobal.Contains("SUBURBANO") || textoEncabezadoGlobal.Contains("SUB-URBANO") || textoEncabezadoGlobal.Contains("SUB URBANO"))
							{
								tipoPredioInferido = "3";
							}
							else if (textoEncabezadoGlobal.Contains("RUSTICO") || textoEncabezadoGlobal.Contains("RÚSTICO"))
							{
								tipoPredioInferido = "2";
							}
							else if (textoEncabezadoGlobal.Contains("URBANO"))
							{
								tipoPredioInferido = "1";
							}
						}

						var mapaBloqueado = MapeadorInteligente.ProcesarEncabezadosConMemoria(mapaCrudo);
						DataTable tablaCrudos = CrearEstructuraRaw();

						// ==============================================================================
						// 🚀 PRE-ESCANEO DE SEGURIDAD (Detección de Exclusión Mutua)
						// ==============================================================================
						bool archivoTienePagosBimestrales = mapaBloqueado.BimestresSueltos.Count > 0;
						if (!archivoTienePagosBimestrales)
						{
							for (int r = filaInicioDatos; r < tablaExcel.Rows.Count; r++)
							{
								string textoPreFila = string.Join(" ", tablaExcel.Rows[r].ItemArray).ToUpper();
								if (textoPreFila.Contains("BIMESTRAL") || textoPreFila.Contains("BIMESTRE") || textoPreFila.Contains("BIM "))
								{
									archivoTienePagosBimestrales = true;
									break;
								}
							}
						}

						LogService.WriteLogAsync("WARN", "SISTEMA_DEBUG", "Procesador", $"[TRACE] Bucle en fila {filaInicioDatos}. Referencia Bimestral Encontrada: {archivoTienePagosBimestrales}").Wait();

						for (int i = filaInicioDatos; i < tablaExcel.Rows.Count; i++)
						{
							var fila = tablaExcel.Rows[i];

							string textoInicioFila = string.Join(" ", fila.ItemArray.Take(6).Select(x => x?.ToString().ToUpper() ?? ""));
							if (textoInicioFila.Contains("TOTAL") || textoInicioFila.Contains("SUMA") || textoInicioFila.Contains("CUADRO")) break;

							if (string.IsNullOrWhiteSpace(string.Join("", fila.ItemArray))) continue;

							// 🚀 1. EXTRACCIÓN SEGURA Y LIMPIEZA INTELIGENTE DE LA LLAVE
							string cuentaPredial = ExtraerSeguro(fila, mapaBloqueado, "CuentaPredial", "");
							if (string.IsNullOrWhiteSpace(cuentaPredial) || cuentaPredial.Equals("Cuenta", StringComparison.OrdinalIgnoreCase)) continue;

							bool esPagoAnualPorTexto = false;
							bool esBimestralPorTexto = false;
							string bimestrePorTexto = "";
							string cuentaUpper = cuentaPredial.ToUpper();

							// Limpiar "BIMESTRAL"
							if (cuentaUpper.Contains("(BIMESTRAL)"))
							{
								cuentaPredial = cuentaUpper.Replace("(BIMESTRAL)", "").Trim();
								cuentaUpper = cuentaPredial.ToUpper();
							}

							// 🛠️ FIX ZIHUATEUTLA (Febrero): Detectar y extraer "BIMESTRE 1", "BIM 2", etc.
							var regexBimestreExacto = new System.Text.RegularExpressions.Regex(@"(?i)(?:BIMESTRE|BIM)\s*([1-6])");
							var matchBimestre = regexBimestreExacto.Match(cuentaUpper);
							if (matchBimestre.Success)
							{
								esBimestralPorTexto = true;
								bimestrePorTexto = matchBimestre.Groups[1].Value; // Extrae mágicamente el número (ej. "1")

								// Borramos ese texto de la cuenta para dejarla limpia (ej. "4353")
								cuentaPredial = regexBimestreExacto.Replace(cuentaUpper, "").Trim();
								cuentaPredial = cuentaPredial.Replace("()", "").Replace("[]", "").Replace("-", "").Trim();
								cuentaUpper = cuentaPredial.ToUpper();
							}

							// 🛠️ Limpiar textos como "PAGO 2026" o "DEL 2022 AL 2026" usando Regex.
							var regexPagoAnual = new System.Text.RegularExpressions.Regex(@"(?i)(PAGO\s*DEL\s*\d{4}\s*AL\s*\d{4}|PAGO\s*\d{4})");
							if (regexPagoAnual.IsMatch(cuentaUpper))
							{
								esPagoAnualPorTexto = true;
								cuentaPredial = regexPagoAnual.Replace(cuentaUpper, "").Trim();
								cuentaPredial = cuentaPredial.Replace("()", "").Replace("[]", "").Trim();
							}

							if (cuentaPredial.EndsWith(".0")) cuentaPredial = cuentaPredial.Replace(".0", "");

							if (cuentaPredial.Contains("AÑO ") || cuentaPredial.Contains("REZAGO") || cuentaPredial.Contains("TOTAL") || cuentaPredial.Contains("SUMA") || cuentaPredial.Contains("CUADRO"))
							{
								break;
							}

							string tipoPredioHibrido = "";
							if (!string.IsNullOrWhiteSpace(cuentaPredial) && cuentaPredial.Contains("-"))
							{
								var partes = cuentaPredial.Split('-');
								if (partes.Length == 2)
								{
									string prefijo = partes[0].Trim().ToUpper();
									if (prefijo == "U") tipoPredioHibrido = "1";
									else if (prefijo == "R") tipoPredioHibrido = "2";
									else if (prefijo == "S") tipoPredioHibrido = "3";

									if (!string.IsNullOrWhiteSpace(tipoPredioHibrido)) cuentaPredial = partes[1].Trim();
								}
							}

							// 🚀 2. EXTRACCIÓN Y EVALUACIÓN DE AÑOS PAGADOS
							string anioPredialStr = ExtraerSeguro(fila, mapaBloqueado, "Anio", "").ToUpper().Trim();
							bool incluyeAnioActual = false;
							int anioActual = DateTime.Now.Year;

							if (string.IsNullOrWhiteSpace(anioPredialStr))
							{
								int colAnioActual = -1;
								string anioActualStr = anioActual.ToString();

								foreach (var kvp in mapaCrudo)
								{
									if (kvp.Key.StartsWith(anioActualStr))
									{
										colAnioActual = kvp.Value;
										break;
									}
								}

								if (colAnioActual != -1)
								{
									string valorAnioActual = fila[colAnioActual]?.ToString().Trim();
									if (string.IsNullOrWhiteSpace(valorAnioActual) || valorAnioActual == "0" || valorAnioActual == "0.00" || valorAnioActual == "-") continue;
									else incluyeAnioActual = true;
								}
								else
								{
									incluyeAnioActual = true;
								}
							}
							else
							{
								var matches = System.Text.RegularExpressions.Regex.Matches(anioPredialStr, @"\d{4}");
								if (matches.Count > 0)
								{
									var añosEncontrados = matches.Cast<System.Text.RegularExpressions.Match>().Select(m => int.Parse(m.Value)).ToList();
									int anioMinimo = añosEncontrados.Min();
									int anioMaximo = añosEncontrados.Max();
									if (anioActual >= anioMinimo && anioActual <= anioMaximo) incluyeAnioActual = true;
								}

								if (!incluyeAnioActual && anioPredialStr != "Rezagos Anteriores")
								{
									resultadoFinal.RegistrosFallidos++;
									resultadoFinal.ErroresDetalle.Add($"Fila {i + 1}: El periodo de pago '{anioPredialStr}' de la cuenta {cuentaPredial} no incluye el ejercicio fiscal en curso ({anioActual}).");
									continue;
								}
							}

							// 🚀 3. HOMOLOGACIÓN DE TIPO DE PREDIO
							string tipoPredio = ExtraerSeguro(fila, mapaBloqueado, "TipoPredio", "").ToUpper().Trim();
							if (string.IsNullOrWhiteSpace(tipoPredio)) tipoPredio = !string.IsNullOrWhiteSpace(tipoPredioHibrido) ? tipoPredioHibrido : tipoPredioInferido;

							if (tipoPredio == "U" || tipoPredio.StartsWith("URBANO")) tipoPredio = "1";
							else if (tipoPredio == "R" || tipoPredio.StartsWith("RUSTICO") || tipoPredio.StartsWith("RÚSTICO")) tipoPredio = "2";
							else if (tipoPredio == "S" || tipoPredio.StartsWith("SUBURBANO") || tipoPredio.StartsWith("SUB") || tipoPredio == "S-URB" || tipoPredio.Contains("-URB")) tipoPredio = "3";
							if (string.IsNullOrWhiteSpace(tipoPredio)) tipoPredio = "1";

							// =========================================================================
							// 🚀 4. CASCADA LÓGICA DE INFERENCIA DE PAGO 
							// =========================================================================
							string clasePago = ExtraerSeguro(fila, mapaBloqueado, "ClasePago", "");
							if (string.IsNullOrWhiteSpace(clasePago)) clasePago = clasePagoInferida;

							string bimestre = MapeadorInteligente.RastrearBimestres(fila, mapaBloqueado);

							// 💡 INFERENCIA A: Buscar columna con la letra "B" en el encabezado
							if ((string.IsNullOrWhiteSpace(clasePago) || clasePago == "99") && filaInicioDatos > 0)
							{
								var filaEncabezados = tablaExcel.Rows[filaInicioDatos - 1];
								var indicesB = new System.Collections.Generic.List<int>();

								for (int col = 0; col < tablaExcel.Columns.Count; col++)
								{
									if (filaEncabezados[col]?.ToString().Trim().ToUpper() == "B") indicesB.Add(col);
								}

								if (indicesB.Count > 0)
								{
									foreach (int idx in indicesB)
									{
										string valB = fila[idx]?.ToString().Trim();
										if (!string.IsNullOrWhiteSpace(valB) && valB != "0")
										{
											bimestre = valB;
											clasePago = "2";
											break;
										}
									}
								}
							}

							// 💡 INFERENCIA B: Inferencia de contexto a nivel de renglón
							string textoFilaCompleta = string.Join(" ", fila.ItemArray).ToUpper();

							if (esBimestralPorTexto)
							{
								clasePago = "2";
								bimestre = bimestrePorTexto;
							}
							else if (textoFilaCompleta.Contains("BIMESTRAL") || textoFilaCompleta.Contains("BIMESTRE"))
							{
								clasePago = "2";
								if (textoFilaCompleta.Contains("6 BIMESTRES") || textoFilaCompleta.Contains("SEIS BIMESTRES"))
								{
									bimestre = "6";
								}
							}
							else if (esPagoAnualPorTexto || textoFilaCompleta.Contains("PAGO 20") || textoFilaCompleta.Contains("AL 20"))
							{
								clasePago = "1";
								bimestre = "0";
							}

							// 💡 INFERENCIA C: Auto-Corrección lógica por Bimestre explícito.
							if (clasePago == "99" || string.IsNullOrWhiteSpace(clasePago))
							{
								// Si halló un bimestre del 1 al 6, obliga a que la clase sea 2.
								if (!string.IsNullOrWhiteSpace(bimestre) && bimestre != "0" && bimestre != "99")
								{
									clasePago = "2";
								}
								// 🛠️ EXPLICACIÓN NUEVA: Si halló un "0" en la columna bimestre, obliga a que sea Anual.
								else if (bimestre == "0")
								{
									clasePago = "1";
								}
							}

							// 💡 INFERENCIA D: El Default Inteligente
							if (clasePago == "99" || string.IsNullOrWhiteSpace(clasePago))
							{
								// 1. Si el renglón tiene un año válido asignado explícitamente
								if ((!string.IsNullOrWhiteSpace(anioPredialStr) && anioPredialStr != "-") || incluyeAnioActual)
								{
									clasePago = "1";
								}
								// 2. Si vimos referencias bimestrales en el archivo
								else if (archivoTienePagosBimestrales)
								{
									clasePago = "1";
								}
								// 3. Si la inferencia global de la pestaña era Anual
								else if (clasePagoInferida == "1")
								{
									clasePago = "1";
								}
							}

							// 🚀 5. ASIGNACIONES FINALES (Flexibilidad de Fecha)
							string claveMunicipio = ExtraerSeguro(fila, mapaBloqueado, "ClaveMunicipio", "");
							if (string.IsNullOrWhiteSpace(claveMunicipio) && param != null)
								claveMunicipio = param.ClaveMunicipioDestino > 0 ? param.ClaveMunicipioDestino.ToString() : param.OficinaId.ToString();

							string fechaVigencia = ExtraerSeguro(fila, mapaBloqueado, "FechaVigencia", "").Trim();
							if (string.IsNullOrWhiteSpace(fechaVigencia))
								fechaVigencia = DateTime.Now.ToString("yyyy-MM-dd");
							else if (double.TryParse(fechaVigencia, out double diasExcel) && diasExcel > 10000 && !fechaVigencia.Contains("-") && !fechaVigencia.Contains("/"))
								fechaVigencia = DateTime.FromOADate(diasExcel).ToString("yyyy-MM-dd");
							else if (DateTime.TryParse(fechaVigencia, new System.Globalization.CultureInfo("es-MX"), System.Globalization.DateTimeStyles.None, out DateTime fechaParseada))
								fechaVigencia = fechaParseada.ToString("yyyy-MM-dd");
							else
								fechaVigencia = DateTime.Now.ToString("yyyy-MM-dd");

							// 🚀 6. GENERACIÓN DE FILAS Y LA BARRERA DE RECHAZO
							var bimestresMultiples = MapeadorInteligente.ExtraerBimestresMultiplesConMonto(fila, mapaBloqueado);

							if (bimestresMultiples.Count > 0)
							{
								clasePago = "2"; // Fuerza el tipo de pago a Bimestral
								foreach (var bim in bimestresMultiples)
								{
									DataRow nuevaFila = tablaCrudos.NewRow();
									nuevaFila["ClaveMunicipio"] = claveMunicipio;
									nuevaFila["TipoPredio"] = tipoPredio;
									nuevaFila["CuentaPredial"] = cuentaPredial;
									nuevaFila["ClasePago"] = clasePago;
									nuevaFila["Bimestre"] = bim.Key;
									nuevaFila["ImpuestoDeterminado"] = bim.Value;
									nuevaFila["FechaVigencia"] = fechaVigencia;
									nuevaFila["FolioCarga"] = param.FolioCarga.ToString();
									tablaCrudos.Rows.Add(nuevaFila);
								}
							}
							else
							{
								// Layout Tradicional Vertical
								if (string.IsNullOrWhiteSpace(bimestre)) bimestre = bimestreInferido;

								// Limpieza lógica del bimestre 
								if (string.IsNullOrWhiteSpace(bimestre) || bimestre == "99")
								{
									bimestre = (clasePago == "1") ? "0" : "99";
								}

								if (clasePago == "1" && anioPredialStr == "-") continue;

								// 🛑 BARRERA 1: Sin contexto en absoluto (Solo se dispara si fallan las deducciones inteligentes)
								if (clasePago == "99")
								{
									resultadoFinal.RegistrosFallidos++;
									resultadoFinal.ErroresDetalle.Add($"Fila {i + 1} (Cuenta {cuentaPredial}): Rechazado. No se encontró ninguna referencia en el archivo ni en la fila para determinar si el pago es Anual o Bimestral.");
									continue;
								}

								// 🛑 BARRERA 2: Contradicción Anual
								if (clasePago == "1" && (bimestre != "0" && bimestre != "99"))
								{
									resultadoFinal.RegistrosFallidos++;
									resultadoFinal.ErroresDetalle.Add($"Fila {i + 1} (Cuenta {cuentaPredial}): Contradicción detectada. El pago es Anual, pero tiene asignado el bimestre {bimestre}.");
									continue;
								}

								// 🛑 BARRERA 3: Contradicción Bimestral
								if (clasePago == "2" && (bimestre == "0" || bimestre == "99"))
								{
									resultadoFinal.RegistrosFallidos++;
									resultadoFinal.ErroresDetalle.Add($"Fila {i + 1} (Cuenta {cuentaPredial}): Pago Bimestral sin periodo especificado. El sistema no sabría qué bimestre consolidar.");
									continue;
								}

								DataRow nuevaFila = tablaCrudos.NewRow();
								nuevaFila["ClaveMunicipio"] = claveMunicipio;
								nuevaFila["TipoPredio"] = tipoPredio;
								nuevaFila["CuentaPredial"] = cuentaPredial;
								nuevaFila["ClasePago"] = clasePago;
								nuevaFila["Bimestre"] = bimestre;

								string impuestoStr = ExtraerSeguro(fila, mapaBloqueado, "ImpuestoDeterminado", "0").Trim();
								impuestoStr = impuestoStr.Replace("$", "").Replace(",", "").Trim();

								if (decimal.TryParse(impuestoStr, out decimal impuestoDecimal)) nuevaFila["ImpuestoDeterminado"] = impuestoDecimal;
								else nuevaFila["ImpuestoDeterminado"] = 0m;

								nuevaFila["FechaVigencia"] = fechaVigencia;
								nuevaFila["FolioCarga"] = param.FolioCarga.ToString();
								tablaCrudos.Rows.Add(nuevaFila);
							}
						}

						LogService.WriteLogAsync("WARN", "SISTEMA_DEBUG", "Procesador", $"[TRACE] Bucle finalizado. Filas crudas recolectadas: {tablaCrudos.Rows.Count}").Wait();

						var resultadoLimpieza = LimpiadorDatos.LimpiarYValidar(tablaCrudos, nombrePestaña, param);

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
			}
			catch (Exception ex)
			{
				LogService.WriteLogAsync("ERROR", param.UsuarioLogin, "ProcesadorPagosExcel", $"Fallo crítico: {ex.Message}").Wait();
				throw;
			}

			return resultadoFinal;
		}

		/// <summary>
		/// Extrae de manera segura un valor de una fila de datos utilizando el mapeo oficial, devolviendo un valor por defecto si ocurre algún error o si el valor extraído es nulo o vacío.
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
		/// Crea una estructura de DataTable para almacenar temporalmente los datos crudos extraídos del archivo Excel antes de su limpieza y validación. Esta estructura incluye columnas para la clave del municipio, tipo de predio, cuenta predial, clase de pago, bimestre, impuesto determinado, fecha de vigencia y folio de carga.
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
			dt.Columns.Add("ImpuestoDeterminado", typeof(decimal)); // Blindaje Lafragua
			dt.Columns.Add("FechaVigencia", typeof(string));
			dt.Columns.Add("FolioCarga", typeof(string));
			return dt;
		}

		/// <summary>
		/// Inserta de manera masiva los registros válidos en la base de datos utilizando SqlBulkCopy y luego ejecuta procedimientos almacenados para procesar y consolidar los datos. Devuelve una lista de errores encontrados durante la consolidación.
		/// </summary>
		/// <param name="lote"></param>
		/// <param name="param"></param>
		/// <returns></returns>
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