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
		/// Método principal asíncrono para procesar un archivo Excel de pagos. Lee el archivo, extrae y valida los datos, y finalmente inserta/consolida los registros válidos en la base de datos. Devuelve un objeto ResultadoProceso que contiene información sobre el número de registros exitosos y fallidos, así como detalles de errores.
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

						// 🚀 NUEVO: INFERENCIA DE CONTEXTO DESDE EL NOMBRE DE LA PESTAÑA (Caso Huehuetlán)
						string clasePagoInferida = "99";
						string bimestreInferido = "99";
						string tipoPredioInferido = "";

						// Inferir Clase de Pago y Bimestre
						if (pestanaUpper.Contains("ANUAL"))
						{
							clasePagoInferida = "1";
							bimestreInferido = "0"; // Los anuales suelen tener bimestre 0
						}
						else if (pestanaUpper.Contains("BIMESTRE"))
						{
							clasePagoInferida = "2";
							// Buscar dinámicamente qué número de bimestre es (1 al 6)
							for (int b = 1; b <= 6; b++)
							{
								if (pestanaUpper.Contains($"{b}BIMESTRE") || pestanaUpper.Contains($"BIMESTRE {b}") || pestanaUpper.Contains($"BIMESTRE{b}"))
								{
									bimestreInferido = b.ToString();
									break;
								}
							}
						}

						// Inferir Tipo de Predio (Por si también omiten la columna)
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

							// 🚀 EXTRACCIÓN SEGURA DE LA LLAVE
							string cuentaPredial = ExtraerSeguro(fila, mapaBloqueado, "CuentaPredial", "");
							if (string.IsNullOrWhiteSpace(cuentaPredial) || cuentaPredial.Equals("Cuenta", StringComparison.OrdinalIgnoreCase)) continue;
							if (cuentaPredial.EndsWith(".0")) cuentaPredial = cuentaPredial.Replace(".0", "");

							string anioPredial = ExtraerSeguro(fila, mapaBloqueado, "Anio", "");
							if (string.IsNullOrWhiteSpace(anioPredial)) anioPredial = DateTime.Now.Year.ToString();
							if (anioPredial.Contains("2025") || anioPredial.Contains("2024") || anioPredial.Contains("2023") || anioPredial.Contains("2022") || anioPredial.Contains("2021")) continue;

							// 🚀 HOMOLOGACIÓN SEGURA DE TIPO DE PREDIO
							string tipoPredio = ExtraerSeguro(fila, mapaBloqueado, "TipoPredio", "").ToUpper().Trim();
							if (string.IsNullOrWhiteSpace(tipoPredio)) tipoPredio = tipoPredioInferido;

							if (tipoPredio == "U" || tipoPredio.StartsWith("URBANO")) tipoPredio = "1";
							else if (tipoPredio == "R" || tipoPredio.StartsWith("RUSTICO") || tipoPredio.StartsWith("RÚSTICO")) tipoPredio = "2";
							else if (tipoPredio == "S" || tipoPredio.StartsWith("SUBURBANO") || tipoPredio.StartsWith("SUB")) tipoPredio = "3";
							if (string.IsNullOrWhiteSpace(tipoPredio)) tipoPredio = "1";

							// 🚀 VALORES POR DEFECTO LÓGICOS (Banderas 99)
							string clasePago = ExtraerSeguro(fila, mapaBloqueado, "ClasePago", "");
							if (string.IsNullOrWhiteSpace(clasePago)) clasePago = clasePagoInferida;
							if (string.IsNullOrWhiteSpace(clasePago)) clasePago = "99";

							string bimestre = MapeadorInteligente.RastrearBimestres(fila, mapaBloqueado);
							if (string.IsNullOrWhiteSpace(bimestre)) bimestre = bimestreInferido;
							if (string.IsNullOrWhiteSpace(bimestre)) bimestre = "99";

							string claveMunicipio = ExtraerSeguro(fila, mapaBloqueado, "ClaveMunicipio", "");
							if (string.IsNullOrWhiteSpace(claveMunicipio) && param != null)
							{
								claveMunicipio = param.ClaveMunicipioDestino > 0 ? param.ClaveMunicipioDestino.ToString() : param.OficinaId.ToString();
							}

							// 🚀 FALLBACK LÓGICO DE FECHAS
							string fechaVigencia = ExtraerSeguro(fila, mapaBloqueado, "FechaVigencia", "").Trim();

							if (string.IsNullOrWhiteSpace(fechaVigencia))
							{
								// Si es un archivo de pagos sin fecha, asumimos que se pagó hoy
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
								// Rescate final si el Excel trajo basura textual en la columna de fecha
								fechaVigencia = DateTime.Now.ToString("yyyy-MM-dd");
							}

							DataRow nuevaFila = tablaCrudos.NewRow();
							nuevaFila["ClaveMunicipio"] = claveMunicipio;
							nuevaFila["TipoPredio"] = tipoPredio;
							nuevaFila["CuentaPredial"] = cuentaPredial;
							nuevaFila["ClasePago"] = clasePago;
							nuevaFila["Bimestre"] = bimestre;
							nuevaFila["ImpuestoDeterminado"] = ExtraerSeguro(fila, mapaBloqueado, "ImpuestoDeterminado", "0");
							nuevaFila["FechaVigencia"] = fechaVigencia;
							nuevaFila["FolioCarga"] = param.FolioCarga.ToString();
							tablaCrudos.Rows.Add(nuevaFila);
						}

						LogService.WriteLogAsync("WARN", "SISTEMA_DEBUG", "Procesador", $"[TRACE] Bucle finalizado. Filas crudas recolectadas: {tablaCrudos.Rows.Count}").Wait();

						var resultadoLimpieza = LimpiadorDatos.LimpiarYValidar(tablaCrudos, nombrePestaña, param);

						if (resultadoLimpieza.TablaValidos.Rows.Count > 0)
						{
							// 🚀 3. EJECUCIÓN ASÍNCRONA DE LA INGESTA Y CONSOLIDACIÓN
							List<string> erroresLogicos = await InsertarBulkAsync(resultadoLimpieza.TablaValidos, param);

							// Sumamos los errores lógicos a la lista global para el correo
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
		/// Método auxiliar para extraer valores de una fila de datos de manera segura, manejando posibles excepciones y proporcionando un valor por defecto si la extracción falla o el valor es nulo o vacío.
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
		/// Método que crea y devuelve la estructura de un DataTable para almacenar temporalmente los datos crudos extraídos del archivo Excel antes de ser limpiados y validados. Define las columnas necesarias para el procesamiento posterior.
		/// </summary>
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
			return dt;
		}

		/// <summary>
		/// Método asíncrono que realiza la inserción masiva de registros válidos en la base de datos utilizando SqlBulkCopy. Configura las columnas a mapear y ejecuta un procedimiento almacenado para procesar e ingestar los datos, y luego consolida los adeudos, capturando los errores lógicos.
		/// </summary>
		private async Task<List<string>> InsertarBulkAsync(DataTable lote, ParametrosCarga param)
		{
			var erroresConsolidacion = new List<string>();

			using (SqlConnection conn = new SqlConnection(CadenaConexion))
			{
				await conn.OpenAsync();

				// PASO 1: Inserción Masiva a Staging
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

				// PASO 2: Ingesta a PadronDestino (SP Original)
				using (SqlCommand cmd = new SqlCommand("pred_Operacion.sp_ProcesarMergeEtiquetado", conn))
				{
					cmd.CommandType = CommandType.StoredProcedure;
					cmd.CommandTimeout = 180;
					cmd.Parameters.AddWithValue("@FolioCarga", param.FolioCarga);
					await cmd.ExecuteNonQueryAsync();
				}

				// 🚀 PASO 3: NUEVA CONSOLIDACIÓN Y CAPTURA DE ERRORES LÓGICOS
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