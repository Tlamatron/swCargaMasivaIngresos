using ExcelDataReader;
using swCargaMasivaIngresos.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.Linq;

namespace swCargaMasivaIngresos.Services
{
	/// <summary>
	/// Clase encargada de procesar archivos Excel que contienen información de pagos. Implementa la interfaz IProcesadorFormato y proporciona métodos para leer, limpiar y validar los datos del archivo, así como para insertar los registros válidos en la base de datos mediante operaciones bulk.
	/// </summary>
	public class ProcesadorPagosExcel : IProcesadorFormato
	{
		private readonly string CadenaConexion = ConfiguracionApp.ObtenerCadenaConexion();

		/// <summary>
		/// Método principal para procesar un archivo Excel de pagos. Lee el archivo, extrae y valida los datos, y finalmente inserta los registros válidos en la base de datos. Devuelve un objeto ResultadoProceso que contiene información sobre el número de registros exitosos y fallidos, así como detalles de errores.
		/// </summary>
		/// <param name="rutaArchivo"></param>
		/// <param name="param"></param>
		/// <returns></returns>
		public ResultadoProceso Procesar(string rutaArchivo, ParametrosCarga param)
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

						int filaInicioDatos;
						var mapaCrudo = MapeadorInteligente.ObtenerMapaPorRegiones(tablaExcel, out filaInicioDatos);

						if (mapaCrudo.Count == 0) continue;

						var mapaBloqueado = MapeadorInteligente.ProcesarEncabezadosConMemoria(mapaCrudo);
						DataTable tablaCrudos = CrearEstructuraRaw();

						LogService.WriteLogAsync("WARN", "SISTEMA_DEBUG", "Procesador", $"[TRACE] Iniciando bucle en fila {filaInicioDatos}").Wait();
						HashSet<string> pagosProcesados = new HashSet<string>();
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
							if (tipoPredio == "U" || tipoPredio.StartsWith("URBANO")) tipoPredio = "1";
							else if (tipoPredio == "R" || tipoPredio.StartsWith("RUSTICO") || tipoPredio.StartsWith("RÚSTICO")) tipoPredio = "2";
							else if (tipoPredio == "S" || tipoPredio.StartsWith("SUBURBANO") || tipoPredio.StartsWith("SUB")) tipoPredio = "3";
							if (string.IsNullOrWhiteSpace(tipoPredio)) tipoPredio = "1";

							// 🚀 VALORES POR DEFECTO LÓGICOS
							string clasePago = ExtraerSeguro(fila, mapaBloqueado, "ClasePago", "1"); // Anual por default
							string bimestre = MapeadorInteligente.RastrearBimestres(fila, mapaBloqueado);
							if (string.IsNullOrWhiteSpace(bimestre)) bimestre = "0";

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

							// 🚀 2. FILTRO ANTI-DUPLICADOS EN MEMORIA (Con Registro de Error)
							string llaveUnica = $"{claveMunicipio}-{tipoPredio}-{cuentaPredial}-{bimestre}";
							if (pagosProcesados.Contains(llaveUnica))
							{
								// Registramos el fallo para que salga en el correo
								resultadoFinal.RegistrosFallidos++;
								resultadoFinal.ErroresDetalle.Add($"Fila {i + 1}: El pago de la cuenta {cuentaPredial} para el bimestre {bimestre} está duplicado en el archivo.");

								continue; // Ahora sí, saltamos el registro para proteger la base de datos
							}
							pagosProcesados.Add(llaveUnica);

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
							InsertarBulk(resultadoLimpieza.TablaValidos, param);
						}

						resultadoFinal.RegistrosExitosos += resultadoLimpieza.TablaValidos.Rows.Count;
						resultadoFinal.RegistrosFallidos += resultadoLimpieza.TablaRechazados.Rows.Count;
						if (resultadoLimpieza.DetallesErrores != null) resultadoFinal.ErroresDetalle.AddRange(resultadoLimpieza.DetallesErrores);
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
		/// Método que crea y devuelve la estructura de un DataTable para almacenar temporalmente los datos crudos extraídos del archivo Excel antes de ser limpiados y validados. Define las columnas necesarias para el procesamiento posterior.
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
			dt.Columns.Add("FolioCarga", typeof(string)); // Era string, lo mantenemos como string para homogeneidad
			return dt;
		}

		/// <summary>
		/// Método que realiza la inserción masiva de registros válidos en la base de datos utilizando SqlBulkCopy. Configura las columnas a mapear y ejecuta un procedimiento almacenado para procesar los datos insertados.
		/// </summary>
		/// <param name="lote"></param>
		/// <param name="param"></param>
		private void InsertarBulk(DataTable lote, ParametrosCarga param)
		{
			using (SqlConnection conn = new SqlConnection(CadenaConexion))
			{
				conn.Open();
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
					// La fecha no se mapea porque Etiquetados genera su propia fecha de operación en el SP, 
					// pero la rescatamos en memoria por si el Limpiador la necesita evaluar.

					bulkCopy.WriteToServer(lote);
				}

				using (SqlCommand cmd = new SqlCommand("pred_Operacion.sp_ProcesarMergeEtiquetado", conn))
				{
					cmd.CommandType = CommandType.StoredProcedure;
					cmd.CommandTimeout = 180;
					cmd.Parameters.AddWithValue("@FolioCarga", param.FolioCarga);
					cmd.ExecuteNonQuery();
				}
			}
		}
	}
}