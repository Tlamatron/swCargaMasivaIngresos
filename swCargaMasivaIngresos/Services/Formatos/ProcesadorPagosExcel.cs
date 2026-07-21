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

						// Inferir Clase de Pago y Bimestre
						if (pestanaUpper.Contains("ANUAL"))
						{
							clasePagoInferida = "1";
							bimestreInferido = "0";
						}
						else if (pestanaUpper.Contains("BIMESTRE"))
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

						// Inferir Tipo de Predio
						if (pestanaUpper.Contains("URBANO") && !pestanaUpper.Contains("SUB")) tipoPredioInferido = "1";
						else if (pestanaUpper.Contains("RUSTICO") || pestanaUpper.Contains("RÚSTICO")) tipoPredioInferido = "2";
						else if (pestanaUpper.Contains("SUB-URBANO") || pestanaUpper.Contains("SUBURBANO") || pestanaUpper.Contains("SUB")) tipoPredioInferido = "3";

						int filaInicioDatos;
						var mapaCrudo = MapeadorInteligente.ObtenerMapaPorRegiones(tablaExcel, out filaInicioDatos);

						if (mapaCrudo.Count == 0) continue;

						var mapaBloqueado = MapeadorInteligente.ProcesarEncabezadosConMemoria(mapaCrudo);
						DataTable tablaCrudos = CrearEstructuraRaw();

						LogService.WriteLogAsync("WARN", "SISTEMA_DEBUG", "Procesador", $"[TRACE] Iniciando bucle en fila {filaInicioDatos}").Wait();

						for (int i = filaInicioDatos; i < tablaExcel.Rows.Count; i++)
						{
							var fila = tablaExcel.Rows[i];

							string textoInicioFila = string.Join(" ", fila.ItemArray.Take(3).Select(x => x?.ToString().ToUpper() ?? ""));
							if (textoInicioFila.Contains("TOTAL")) break;

							if (string.IsNullOrWhiteSpace(string.Join("", fila.ItemArray))) continue;

							// 🚀 1. EXTRACCIÓN SEGURA DE LA LLAVE (Cuenta Predial)
							string cuentaPredial = ExtraerSeguro(fila, mapaBloqueado, "CuentaPredial", "");
							if (string.IsNullOrWhiteSpace(cuentaPredial) || cuentaPredial.Equals("Cuenta", StringComparison.OrdinalIgnoreCase)) continue;
							if (cuentaPredial.EndsWith(".0")) cuentaPredial = cuentaPredial.Replace(".0", "");

							// 🚀 DETECCIÓN DE BASURA Y SUMATORIAS FINALES (Freno de Emergencia)
							if (cuentaPredial.Contains("AÑO ") || cuentaPredial.Contains("REZAGO") || cuentaPredial.Contains("TOTAL") || cuentaPredial.Contains("SUMA") || cuentaPredial.Contains("CUADRO"))
							{
								break;
							}

							string tipoPredioHibrido = "";

							// 🚀 AUTOCORRECCIÓN CUENTAS HÍBRIDAS (Ej. "U-27065")
							if (!string.IsNullOrWhiteSpace(cuentaPredial) && cuentaPredial.Contains("-"))
							{
								var partes = cuentaPredial.Split('-');
								if (partes.Length == 2)
								{
									string prefijo = partes[0].Trim().ToUpper();

									if (prefijo == "U") tipoPredioHibrido = "1";
									else if (prefijo == "R") tipoPredioHibrido = "2";
									else if (prefijo == "S") tipoPredioHibrido = "3";

									if (!string.IsNullOrWhiteSpace(tipoPredioHibrido))
									{
										cuentaPredial = partes[1].Trim();
									}
								}
							}

							// 🚀 2. EXTRACCIÓN Y EVALUACIÓN DE AÑOS PAGADOS
							string anioPredialStr = ExtraerSeguro(fila, mapaBloqueado, "Anio", "").ToUpper().Trim();
							bool incluyeAnioActual = false;
							int anioActual = DateTime.Now.Year;

							// Escenario A: Matriz Horizontal (Ocotepec - Años como Columnas)
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

									if (string.IsNullOrWhiteSpace(valorAnioActual) || valorAnioActual == "0" || valorAnioActual == "0.00")
									{
										// Son rezagos puros. Ignoramos la fila silenciosamente sin generar error.
										continue;
									}
									else
									{
										// SÍ pagó el año actual. Todo en orden.
										incluyeAnioActual = true;
									}
								}
								else
								{
									// Si ni siquiera existe la columna del año actual en el Excel, asumimos que todo es válido 
									// (Mecanismo de seguridad para archivos muy simples)
									incluyeAnioActual = true;
								}
							}
							// Escenario B: Rangos de Texto (Tlacuilotepec - Ej. "DEL 2022 AL 2026")
							else
							{
								var matches = System.Text.RegularExpressions.Regex.Matches(anioPredialStr, @"\d{4}");
								if (matches.Count > 0)
								{
									var añosEncontrados = matches.Cast<System.Text.RegularExpressions.Match>().Select(m => int.Parse(m.Value)).ToList();
									int anioMinimo = añosEncontrados.Min();
									int anioMaximo = añosEncontrados.Max();

									if (anioActual >= anioMinimo && anioActual <= anioMaximo)
									{
										incluyeAnioActual = true;
									}
								}

								// Si trae texto y no incluye el 2026, ESTO SÍ es un error reportable
								if (!incluyeAnioActual && anioPredialStr != "Rezagos Anteriores")
								{
									resultadoFinal.RegistrosFallidos++;
									resultadoFinal.ErroresDetalle.Add($"Fila {i + 1}: El periodo de pago '{anioPredialStr}' de la cuenta {cuentaPredial} no incluye el ejercicio fiscal en curso ({anioActual}).");
									continue;
								}
							}

							// 🚀 3. HOMOLOGACIÓN DE TIPO DE PREDIO
							string tipoPredio = ExtraerSeguro(fila, mapaBloqueado, "TipoPredio", "").ToUpper().Trim();

							if (string.IsNullOrWhiteSpace(tipoPredio))
							{
								tipoPredio = !string.IsNullOrWhiteSpace(tipoPredioHibrido) ? tipoPredioHibrido : tipoPredioInferido;
							}

							if (tipoPredio == "U" || tipoPredio.StartsWith("URBANO")) tipoPredio = "1";
							else if (tipoPredio == "R" || tipoPredio.StartsWith("RUSTICO") || tipoPredio.StartsWith("RÚSTICO")) tipoPredio = "2";
							else if (tipoPredio == "S" || tipoPredio.StartsWith("SUBURBANO") || tipoPredio.StartsWith("SUB")) tipoPredio = "3";
							if (string.IsNullOrWhiteSpace(tipoPredio)) tipoPredio = "1";

							// 🚀 4. ASIGNACIÓN LÓGICA DE CLASE DE PAGO Y BIMESTRE
							string clasePago = ExtraerSeguro(fila, mapaBloqueado, "ClasePago", "");
							if (string.IsNullOrWhiteSpace(clasePago)) clasePago = clasePagoInferida;

							// Si trae texto de años, forzamos Anual (Tlacuilotepec)
							if (string.IsNullOrWhiteSpace(clasePago) && !string.IsNullOrWhiteSpace(anioPredialStr))
							{
								clasePago = "1";
							}

							string bimestre = MapeadorInteligente.RastrearBimestres(fila, mapaBloqueado);

							// Escaneo dinámico de columnas "B" (Ocotepec)
							if ((string.IsNullOrWhiteSpace(clasePago) || clasePago == "99") && filaInicioDatos > 0)
							{
								var filaEncabezados = tablaExcel.Rows[filaInicioDatos - 1];
								var indicesB = new System.Collections.Generic.List<int>();

								for (int col = 0; col < tablaExcel.Columns.Count; col++)
								{
									if (filaEncabezados[col]?.ToString().Trim().ToUpper() == "B")
									{
										indicesB.Add(col);
									}
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

							if (string.IsNullOrWhiteSpace(bimestre)) bimestre = bimestreInferido;
							if (string.IsNullOrWhiteSpace(bimestre)) bimestre = "99";
							if (string.IsNullOrWhiteSpace(clasePago)) clasePago = "99";

							// 🚀 5. ASIGNACIONES FINALES Y EMPAQUETADO
							string claveMunicipio = ExtraerSeguro(fila, mapaBloqueado, "ClaveMunicipio", "");
							if (string.IsNullOrWhiteSpace(claveMunicipio) && param != null)
							{
								claveMunicipio = param.ClaveMunicipioDestino > 0 ? param.ClaveMunicipioDestino.ToString() : param.OficinaId.ToString();
							}

							string fechaVigencia = ExtraerSeguro(fila, mapaBloqueado, "FechaVigencia", "").Trim();
							if (string.IsNullOrWhiteSpace(fechaVigencia))
							{
								fechaVigencia = DateTime.Now.ToString("yyyy-MM-dd");
							}
							else if (double.TryParse(fechaVigencia, out double diasExcel) && diasExcel > 10000 && !fechaVigencia.Contains("-") && !fechaVigencia.Contains("/"))
							{
								fechaVigencia = DateTime.FromOADate(diasExcel).ToString("yyyy-MM-dd");
							}
							else if (DateTime.TryParse(fechaVigencia, new System.Globalization.CultureInfo("es-MX"), System.Globalization.DateTimeStyles.None, out DateTime fechaParseada))
							{
								fechaVigencia = fechaParseada.ToString("yyyy-MM-dd");
							}
							else
							{
								fechaVigencia = DateTime.Now.ToString("yyyy-MM-dd");
							}

							DataRow nuevaFila = tablaCrudos.NewRow();
							nuevaFila["ClaveMunicipio"] = claveMunicipio;
							nuevaFila["TipoPredio"] = tipoPredio;
							nuevaFila["CuentaPredial"] = cuentaPredial;
							nuevaFila["ClasePago"] = clasePago;
							nuevaFila["Bimestre"] = bimestre;

							// Parseo Decimal Seguro (Lafragua)
							string impuestoStr = ExtraerSeguro(fila, mapaBloqueado, "ImpuestoDeterminado", "0").Trim();
							if (decimal.TryParse(impuestoStr, out decimal impuestoDecimal))
							{
								nuevaFila["ImpuestoDeterminado"] = impuestoDecimal;
							}
							else
							{
								nuevaFila["ImpuestoDeterminado"] = 0m;
							}

							nuevaFila["FechaVigencia"] = fechaVigencia;
							nuevaFila["FolioCarga"] = param.FolioCarga.ToString();
							tablaCrudos.Rows.Add(nuevaFila);
						}

						LogService.WriteLogAsync("WARN", "SISTEMA_DEBUG", "Procesador", $"[TRACE] Bucle finalizado. Filas crudas recolectadas: {tablaCrudos.Rows.Count}").Wait();

						var resultadoLimpieza = LimpiadorDatos.LimpiarYValidar(tablaCrudos, nombrePestaña, param);

						if (resultadoLimpieza.TablaValidos.Rows.Count > 0)
						{
							List<string> erroresLogicos = await InsertarBulkAsync(resultadoLimpieza.TablaValidos, param);

							if (erroresLogicos.Any())
							{
								resultadoFinal.ErroresDetalle.AddRange(erroresLogicos);
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