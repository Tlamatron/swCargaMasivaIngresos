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
	/// Clase ProcesadorPagosExcel que implementa la interfaz IProcesadorFormato. Este procesador se encarga de leer archivos Excel que contienen datos de pagos, mapearlos, limpiarlos y validarlos, y finalmente insertar los registros válidos en la base de datos mediante una operación de inserción masiva.
	/// </summary>
	public class ProcesadorPagosExcel : IProcesadorFormato
	{
		private readonly string CadenaConexion = ConfiguracionApp.ObtenerCadenaConexion();

		/// <summary>
		/// Método principal para procesar un archivo Excel de pagos. Este método realiza la lectura del archivo, mapea los datos, limpia y valida los registros, y finalmente inserta los registros válidos en la base de datos.
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

						// 🚀 1. LLAMAMOS AL NUEVO SÚPER MOTOR VERTICAL
						int filaInicioDatos;
						var mapaCrudo = MapeadorInteligente.ObtenerMapaPorRegiones(tablaExcel, out filaInicioDatos);

						// Si no encontró el mapa, ignora la pestaña (está vacía o es de logos)
						if (mapaCrudo.Count == 0) continue;

						var mapaBloqueado = MapeadorInteligente.ProcesarEncabezadosConMemoria(mapaCrudo);
						// 🚀 CANDADO FINANCIERO 1: Exigir la columna de pago
						//if (!mapaBloqueado.Columnas.ContainsKey("ImpuestoDeterminado"))
						//{
						//	throw new Exception("El archivo carece de una columna identificable para el Monto/Pago (Ej. PAGO, IMPORTE, TOTAL, IMPUESTO). Al ser una carga de Pagos, este dato es estrictamente obligatorio.");
						//}

						DataTable tablaCrudos = CrearEstructuraRaw();

						LogService.WriteLogAsync("WARN", "SISTEMA_DEBUG", "Procesador", $"[TRACE] Iniciando bucle en fila {filaInicioDatos}").Wait();

						// 🚀 2. INICIAMOS EL CICLO EXACTAMENTE EN LA FILA DE DATOS
						for (int i = filaInicioDatos; i < tablaExcel.Rows.Count; i++)
						{
							var fila = tablaExcel.Rows[i];

							string textoInicioFila = string.Join(" ", fila.ItemArray.Take(3).Select(x => x?.ToString().ToUpper() ?? ""));
							if (textoInicioFila.Contains("TOTAL")) break;

							if (string.IsNullOrWhiteSpace(string.Join("", fila.ItemArray))) continue;

							string cuentaPredial = MapeadorInteligente.Extraer(fila, mapaBloqueado, "CuentaPredial");
							if (string.IsNullOrWhiteSpace(cuentaPredial) || cuentaPredial.Equals("Cuenta", StringComparison.OrdinalIgnoreCase)) continue;

							// 🚀 NUEVO: Limpiamos el .0 que inyecta Excel en las celdas numéricas
							if (cuentaPredial.EndsWith(".0")) cuentaPredial = cuentaPredial.Replace(".0", "");

							// 1. AÑO POR DEFECTO
							string anioPredial = MapeadorInteligente.Extraer(fila, mapaBloqueado, "Anio");
							if (string.IsNullOrWhiteSpace(anioPredial)) anioPredial = DateTime.Now.Year.ToString();

							if (anioPredial.Contains("2025") || anioPredial.Contains("2024") || anioPredial.Contains("2023") || anioPredial.Contains("2022") || anioPredial.Contains("2021")) continue;

							// 2. HOMOLOGACIÓN DE TIPO DE PREDIO (Zoquitlán)
							string tipoPredio = MapeadorInteligente.Extraer(fila, mapaBloqueado, "TipoPredio").ToUpper().Trim();
							if (tipoPredio == "U" || tipoPredio.StartsWith("URBANO")) tipoPredio = "1";
							else if (tipoPredio == "R" || tipoPredio.StartsWith("RUSTICO")) tipoPredio = "2";
							else if (tipoPredio == "S" || tipoPredio.StartsWith("SUBURBANO")) tipoPredio = "3";
							if (string.IsNullOrWhiteSpace(tipoPredio)) tipoPredio = "1"; // Si por algo no se mapeó, forzamos Urbano.

							// 3. INYECCIÓN TEMPRANA DE CLASE Y BIMESTRE
							string clasePago = MapeadorInteligente.Extraer(fila, mapaBloqueado, "ClasePago");
							if (string.IsNullOrWhiteSpace(clasePago)) clasePago = "1"; // Anual

							string bimestre = MapeadorInteligente.RastrearBimestres(fila, mapaBloqueado);
							if (string.IsNullOrWhiteSpace(bimestre)) bimestre = "0"; // Todos

							// 4. DOBLE ESCUDO DEL MUNICIPIO (Previene el '0')
							string claveMunicipio = MapeadorInteligente.Extraer(fila, mapaBloqueado, "ClaveMunicipio");
							if (string.IsNullOrWhiteSpace(claveMunicipio) && param != null)
							{
								claveMunicipio = param.ClaveMunicipioDestino > 0 ? param.ClaveMunicipioDestino.ToString() : param.OficinaId.ToString();
							}

							// 🚀 5. NUEVO: RESCATE DE FECHAS SERIALES DE EXCEL
							// 🚀 5. ESTANDARIZACIÓN UNIVERSAL DE FECHAS (Protección SQL)
							string fechaVigencia = MapeadorInteligente.Extraer(fila, mapaBloqueado, "FechaVigencia").Trim();

							// Escenario A: Viene como número serial crudo de Excel (Ej. "46104" o "46104.0")
							if (double.TryParse(fechaVigencia, out double diasExcel) && diasExcel > 10000 && !fechaVigencia.Contains("-") && !fechaVigencia.Contains("/"))
							{
								fechaVigencia = DateTime.FromOADate(diasExcel).ToString("yyyy-MM-dd");
							}
							// Escenario B: Viene como texto desde Excel (Ej. "23/03/2026" o "23-03-2026 12:00:00 a.m.")
							else if (DateTime.TryParse(fechaVigencia, new System.Globalization.CultureInfo("es-MX"), System.Globalization.DateTimeStyles.None, out DateTime fechaParseada))
							{
								// Lo forzamos al estándar universal de SQL Server para evitar colisiones de idioma
								fechaVigencia = fechaParseada.ToString("yyyy-MM-dd");
							}

							DataRow nuevaFila = tablaCrudos.NewRow();
							nuevaFila["ClaveMunicipio"] = claveMunicipio;
							nuevaFila["TipoPredio"] = tipoPredio;
							nuevaFila["CuentaPredial"] = cuentaPredial;
							nuevaFila["ClasePago"] = clasePago;
							nuevaFila["Bimestre"] = bimestre;
							nuevaFila["ImpuestoDeterminado"] = MapeadorInteligente.Extraer(fila, mapaBloqueado, "ImpuestoDeterminado");
							nuevaFila["FechaVigencia"] = fechaVigencia;

							tablaCrudos.Rows.Add(nuevaFila);
						}

						LogService.WriteLogAsync("WARN", "SISTEMA_DEBUG", "Procesador", $"[TRACE] Bucle finalizado. Filas crudas recolectadas: {tablaCrudos.Rows.Count}").Wait();

						var resultadoLimpieza = LimpiadorDatos.LimpiarYValidar(tablaCrudos, nombrePestaña, param);

						if (resultadoLimpieza.TablaValidos.Rows.Count > 0)
						{
							InsertarBulk(resultadoLimpieza.TablaValidos);
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
		/// Método privado que crea y devuelve la estructura de un DataTable para almacenar temporalmente los registros crudos de pagos antes de su limpieza y validación. Esta estructura incluye columnas para ClaveMunicipio, TipoPredio, CuentaPredial, ClasePago, Bimestre, ImpuestoDeterminado, FechaVigencia y FolioCarga.
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
			return dt;
		}

		/// <summary>
		/// Método privado que realiza la inserción masiva de los registros válidos de pagos en la base de datos utilizando SqlBulkCopy. Se asegura de mapear correctamente las columnas del DataTable a las columnas de la tabla de destino en la base de datos y luego ejecuta un procedimiento almacenado para procesar los registros insertados.
		/// </summary>
		/// <param name="lote"></param>
		private void InsertarBulk(DataTable lote)
		{
			foreach (DataRow row in lote.Rows)
			{
				// Regla de nulos que acordamos para consolidación
				if (row["ImpuestoDeterminado"] == DBNull.Value || string.IsNullOrWhiteSpace(row["ImpuestoDeterminado"]?.ToString()))
				{
					row["ImpuestoDeterminado"] = DBNull.Value;
				}

				if (row["Bimestre"] != DBNull.Value && string.IsNullOrWhiteSpace(row["Bimestre"].ToString())) row["Bimestre"] = "0";
				if (row["ClasePago"] != DBNull.Value && string.IsNullOrWhiteSpace(row["ClasePago"].ToString())) row["ClasePago"] = "1";
			}

			using (SqlConnection conn = new SqlConnection(CadenaConexion))
			{
				conn.Open();
				using (SqlBulkCopy bulkCopy = new SqlBulkCopy(conn))
				{
					// 🚀 CORRECCIÓN: Apuntamos a la tabla de Pagos, NO a la de Padrón
					bulkCopy.DestinationTableName = "pred_Operacion.Staging_Etiquetado";
					bulkCopy.BatchSize = 10000;
					bulkCopy.BulkCopyTimeout = 120;

					// Mapeo exclusivo de Pagos
					bulkCopy.ColumnMappings.Add("ClaveMunicipio", "ClaveMunicipio");
					bulkCopy.ColumnMappings.Add("TipoPredio", "TipoPredio");
					bulkCopy.ColumnMappings.Add("CuentaPredial", "CuentaPredial");
					bulkCopy.ColumnMappings.Add("FolioCarga", "FolioCarga");
					bulkCopy.ColumnMappings.Add("Bimestre", "Bimestre");
					bulkCopy.ColumnMappings.Add("ClasePago", "ClasePago");
					bulkCopy.ColumnMappings.Add("ImpuestoDeterminado", "ImpuestoDeterminado");

					bulkCopy.WriteToServer(lote);
				}

				// 🚀 CORRECCIÓN: Debemos disparar el SP de etiquetados, no el de padrón
				using (SqlCommand cmd = new SqlCommand("pred_Operacion.sp_ProcesarMergeEtiquetado", conn))
				{
					cmd.CommandType = CommandType.StoredProcedure;
					cmd.CommandTimeout = 180;
					// Suponiendo que le pasas param.FolioCarga, asegúrate de recibir 'param' en el método InsertarBulk
					cmd.Parameters.AddWithValue("@FolioCarga", Convert.ToInt32(lote.Rows[0]["FolioCarga"]));
					cmd.ExecuteNonQuery();
				}
			}
		}


		private void InsertarBulk_v01(DataTable lote)
		{
			foreach (DataRow row in lote.Rows)
			{
				if (row["FechaVigencia"] != DBNull.Value && string.IsNullOrWhiteSpace(row["FechaVigencia"].ToString())) row["FechaVigencia"] = DBNull.Value;
				if (row["ImpuestoDeterminado"] == DBNull.Value || string.IsNullOrWhiteSpace(row["ImpuestoDeterminado"]?.ToString()))
				{
					row["ImpuestoDeterminado"] = DBNull.Value;
				}
				if (row["Bimestre"] != DBNull.Value && string.IsNullOrWhiteSpace(row["Bimestre"].ToString())) row["Bimestre"] = "0";
				if (row["ClasePago"] != DBNull.Value && string.IsNullOrWhiteSpace(row["ClasePago"].ToString())) row["ClasePago"] = "1";
			}

			using (SqlConnection conn = new SqlConnection(CadenaConexion))
			{
				conn.Open();
				using (SqlBulkCopy bulkCopy = new SqlBulkCopy(conn))
				{
					bulkCopy.DestinationTableName = "pred_Operacion.Staging_Predial";
					bulkCopy.BatchSize = 10000;
					bulkCopy.BulkCopyTimeout = 120;

					bulkCopy.ColumnMappings.Add("ClaveMunicipio", "ClaveMunicipio");
					bulkCopy.ColumnMappings.Add("TipoPredio", "TipoPredio");
					bulkCopy.ColumnMappings.Add("CuentaPredial", "CuentaPredial");
					bulkCopy.ColumnMappings.Add("ClasePago", "ClasePago");
					bulkCopy.ColumnMappings.Add("Bimestre", "Bimestre");
					bulkCopy.ColumnMappings.Add("ImpuestoDeterminado", "ImpuestoDeterminado");
					bulkCopy.ColumnMappings.Add("FechaVigencia", "FechaVigencia");
					bulkCopy.ColumnMappings.Add("FolioCarga", "FolioCarga");

					bulkCopy.WriteToServer(lote);
				}
			}
		}
	}
}