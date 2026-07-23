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
	/// Clase ProcesadorPadronExcel que implementa la interfaz IProcesadorFormato. Este procesador se encarga de leer archivos Excel que contienen datos de padrón, mapearlos, limpiarlos y validarlos, y finalmente insertar los registros válidos en la base de datos mediante una operación de inserción masiva.
	/// </summary>
	public class ProcesadorPadronExcel : IProcesadorFormato
	{
		private readonly string CadenaConexion = ConfiguracionApp.ObtenerCadenaConexion();

		/// <summary>
		/// Método asíncrono que recibe la ruta del archivo Excel y los parámetros de carga, y realiza la lectura, mapeo, limpieza, validación e inserción/consolidación de los datos en la base de datos.
		/// </summary>
		/// <param name="rutaArchivo"></param>
		/// <param name="param"></param>
		/// <returns></returns>
		public async Task<ResultadoProceso> ProcesarAsync(string rutaArchivo, ParametrosCarga param)
		{
			var resultadoFinal = new ResultadoProceso { ErroresDetalle = new List<string>() };

			LogService.WriteLogAsync("INFO", param.UsuarioLogin, "ProcesadorPadronExcel", $"Iniciando Lectura por Regiones. Folio: {param.FolioCarga}").Wait();

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

						// 🚀 1. LECTURA POR REGIONES DIRECTA
						int filaInicioDatos;
						var mapaCrudo = MapeadorInteligente.ObtenerMapaPorRegiones(tablaExcel, out filaInicioDatos);

						if (mapaCrudo.Count == 0) continue;

						var mapaBloqueado = MapeadorInteligente.ProcesarEncabezadosConMemoria(mapaCrudo);
						DataTable tablaCrudos = CrearEstructuraPadronCompleta();

						// 🚀 2. EXTRACCIÓN
						for (int i = filaInicioDatos; i < tablaExcel.Rows.Count; i++)
						{
							var fila = tablaExcel.Rows[i];

							string textoInicioFila = string.Join(" ", fila.ItemArray.Take(3).Select(x => x?.ToString().ToUpper() ?? ""));
							if (textoInicioFila.Contains("TOTAL")) break;

							if (string.IsNullOrWhiteSpace(string.Join("", fila.ItemArray))) continue;

							// --- DATOS OBLIGATORIOS ---
							string cuentaPredial = MapeadorInteligente.Extraer(fila, mapaBloqueado, "CuentaPredial");
							if (string.IsNullOrWhiteSpace(cuentaPredial) || cuentaPredial.Equals("Cuenta", StringComparison.OrdinalIgnoreCase)) continue;
							if (cuentaPredial.EndsWith(".0")) cuentaPredial = cuentaPredial.Replace(".0", "");

							string anioPredial = MapeadorInteligente.Extraer(fila, mapaBloqueado, "Anio");
							if (string.IsNullOrWhiteSpace(anioPredial)) anioPredial = DateTime.Now.Year.ToString();
							if (anioPredial.Contains("2025") || anioPredial.Contains("2024") || anioPredial.Contains("2023") || anioPredial.Contains("2022") || anioPredial.Contains("2021")) continue;

							string tipoPredio = MapeadorInteligente.Extraer(fila, mapaBloqueado, "TipoPredio").ToUpper().Trim();
							if (tipoPredio == "U" || tipoPredio.StartsWith("URBANO")) tipoPredio = "1";
							else if (tipoPredio == "R" || tipoPredio.StartsWith("RUSTICO")) tipoPredio = "2";
							else if (tipoPredio == "S" || tipoPredio.StartsWith("SUBURBANO")) tipoPredio = "3";
							if (string.IsNullOrWhiteSpace(tipoPredio)) tipoPredio = "1";

							string claveMunicipio = MapeadorInteligente.Extraer(fila, mapaBloqueado, "ClaveMunicipio");
							if (string.IsNullOrWhiteSpace(claveMunicipio) && param != null)
							{
								claveMunicipio = param.ClaveMunicipioDestino > 0 ? param.ClaveMunicipioDestino.ToString() : param.OficinaId.ToString();
							}

							// --- DATOS SEMI-OBLIGATORIOS (Alineados con la bandera 99) ---
							string clasePago = ExtraerSeguro(fila, mapaBloqueado, "ClasePago", "99");

							//string bimestre = MapeadorInteligente.RastrearBimestres(fila, mapaBloqueado);
							string bimestre = ExtraerSeguro(fila, mapaBloqueado, "Bimestre", "");
							if (string.IsNullOrWhiteSpace(bimestre)) bimestre = MapeadorInteligente.RastrearBimestres(fila, mapaBloqueado);

							if (string.IsNullOrWhiteSpace(bimestre)) bimestre = "99";

							// Lógica de Fechas de Excel (Con Fallback Lógico)
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

							// 🚀 2. LLENADO DE LA TABLA
							DataRow nuevaFila = tablaCrudos.NewRow();
							nuevaFila["ClaveMunicipio"] = claveMunicipio;
							nuevaFila["TipoPredio"] = tipoPredio;
							nuevaFila["CuentaPredial"] = cuentaPredial;
							nuevaFila["FolioUnico"] = ExtraerSeguro(fila, mapaBloqueado, "FolioUnico", "");
							nuevaFila["Localidad"] = ExtraerSeguro(fila, mapaBloqueado, "Localidad", "");
							nuevaFila["Calle"] = ExtraerSeguro(fila, mapaBloqueado, "Calle", "");
							nuevaFila["NumExterior"] = ExtraerSeguro(fila, mapaBloqueado, "NumExterior", "");
							nuevaFila["NumInterior"] = ExtraerSeguro(fila, mapaBloqueado, "NumInterior", "");
							nuevaFila["Letra"] = ExtraerSeguro(fila, mapaBloqueado, "Letra", "");
							nuevaFila["Colonia"] = ExtraerSeguro(fila, mapaBloqueado, "Colonia", "");
							nuevaFila["CP"] = ExtraerSeguro(fila, mapaBloqueado, "CP", "");
							nuevaFila["Nombre"] = ExtraerSeguro(fila, mapaBloqueado, "Nombre", "");
							nuevaFila["PrimerApellido"] = ExtraerSeguro(fila, mapaBloqueado, "PrimerApellido", "");
							nuevaFila["SegundoApellido"] = ExtraerSeguro(fila, mapaBloqueado, "SegundoApellido", "");
							nuevaFila["TipoPersona"] = ExtraerSeguro(fila, mapaBloqueado, "TipoPersona", "");
							nuevaFila["RFC"] = ExtraerSeguro(fila, mapaBloqueado, "RFC", "");
							nuevaFila["ClaveRegimenSAT"] = ExtraerSeguro(fila, mapaBloqueado, "ClaveRegimenSAT", "");
							nuevaFila["ClaveUsoSAT"] = ExtraerSeguro(fila, mapaBloqueado, "ClaveUsoSAT", "");
							nuevaFila["CPFiscalSAT"] = ExtraerSeguro(fila, mapaBloqueado, "CPFiscalSAT", "");
							nuevaFila["BaseGravable"] = ExtraerSeguro(fila, mapaBloqueado, "BaseGravable", "0");
							nuevaFila["ClasePago"] = clasePago;
							nuevaFila["Bimestre"] = bimestre;
							nuevaFila["ImpuestoDeterminado"] = ExtraerSeguro(fila, mapaBloqueado, "ImpuestoDeterminado", "0");
							nuevaFila["FechaVigencia"] = fechaVigencia;
							nuevaFila["FolioCarga"] = param.FolioCarga.ToString();

							tablaCrudos.Rows.Add(nuevaFila);
						}

						var resultadoLimpieza = LimpiadorDatos.LimpiarYValidar(tablaCrudos, nombrePestaña, param);

						if (resultadoLimpieza.TablaValidos.Rows.Count > 0)
						{
							// 🚀 3. EJECUCIÓN ASÍNCRONA DE LA INGESTA Y CONSOLIDACIÓN
							List<string> erroresLogicos = await InsertarBulkPadronCompletoAsync(resultadoLimpieza.TablaValidos, param);

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
				LogService.WriteLogAsync("ERROR", param.UsuarioLogin, "ProcesadorPadronExcel", $"Fallo crítico: {ex.Message}").Wait();
				throw;
			}

			return resultadoFinal;
		}

		/// <summary>
		/// Crea la estructura de DataTable que representa el padrón completo con todas las columnas necesarias para la inserción masiva en la base de datos. Esta estructura incluye campos como ClaveMunicipio, TipoPredio, CuentaPredial, FolioUnico, Localidad, Calle, NumExterior, NumInterior, Letra, Colonia, CP, Nombre, PrimerApellido, SegundoApellido, TipoPersona, RFC, ClaveRegimenSAT, ClaveUsoSAT, CPFiscalSAT, BaseGravable, ClasePago, Bimestre, ImpuestoDeterminado, FechaVigencia y FolioCarga.
		/// </summary>
		/// <returns></returns>
		private DataTable CrearEstructuraPadronCompleta()
		{
			var tabla = new DataTable();
			tabla.Columns.Add("ClaveMunicipio", typeof(string));
			tabla.Columns.Add("TipoPredio", typeof(string));
			tabla.Columns.Add("CuentaPredial", typeof(string));
			tabla.Columns.Add("FolioUnico", typeof(string));
			tabla.Columns.Add("Localidad", typeof(string));
			tabla.Columns.Add("Calle", typeof(string));
			tabla.Columns.Add("NumExterior", typeof(string));
			tabla.Columns.Add("NumInterior", typeof(string));
			tabla.Columns.Add("Letra", typeof(string));
			tabla.Columns.Add("Colonia", typeof(string));
			tabla.Columns.Add("CP", typeof(string));
			tabla.Columns.Add("Nombre", typeof(string));
			tabla.Columns.Add("PrimerApellido", typeof(string));
			tabla.Columns.Add("SegundoApellido", typeof(string));
			tabla.Columns.Add("TipoPersona", typeof(string));
			tabla.Columns.Add("RFC", typeof(string));
			tabla.Columns.Add("ClaveRegimenSAT", typeof(string));
			tabla.Columns.Add("ClaveUsoSAT", typeof(string));
			tabla.Columns.Add("CPFiscalSAT", typeof(string));
			tabla.Columns.Add("BaseGravable", typeof(string));
			tabla.Columns.Add("ClasePago", typeof(string));
			tabla.Columns.Add("Bimestre", typeof(string));
			tabla.Columns.Add("ImpuestoDeterminado", typeof(string));
			tabla.Columns.Add("FechaVigencia", typeof(string));
			tabla.Columns.Add("FolioCarga", typeof(string));
			return tabla;
		}

		/// <summary>
		/// Inserta el bloque de datos de forma asíncrona, dispara el SP de Ingesta y luego ejecuta la Consolidación
		/// </summary>
		private async Task<List<string>> InsertarBulkPadronCompletoAsync(DataTable lote, ParametrosCarga param)
		{
			var erroresConsolidacion = new List<string>();

			using (SqlConnection conn = new SqlConnection(CadenaConexion))
			{
				await conn.OpenAsync();

				// PASO 1: Inserción Masiva a Staging
				using (SqlBulkCopy bulkCopy = new SqlBulkCopy(conn))
				{
					bulkCopy.DestinationTableName = "pred_Operacion.Staging_Predial";
					bulkCopy.BatchSize = 10000;
					bulkCopy.BulkCopyTimeout = 120;

					// Mapeo completo
					bulkCopy.ColumnMappings.Add("ClaveMunicipio", "ClaveMunicipio");
					bulkCopy.ColumnMappings.Add("TipoPredio", "TipoPredio");
					bulkCopy.ColumnMappings.Add("CuentaPredial", "CuentaPredial");
					bulkCopy.ColumnMappings.Add("FolioUnico", "FolioUnico");
					bulkCopy.ColumnMappings.Add("Localidad", "Localidad");
					bulkCopy.ColumnMappings.Add("Calle", "Calle");
					bulkCopy.ColumnMappings.Add("NumExterior", "NumExterior");
					bulkCopy.ColumnMappings.Add("NumInterior", "NumInterior");
					bulkCopy.ColumnMappings.Add("Letra", "Letra");
					bulkCopy.ColumnMappings.Add("Colonia", "Colonia");
					bulkCopy.ColumnMappings.Add("CP", "CP");
					bulkCopy.ColumnMappings.Add("Nombre", "Nombre");
					bulkCopy.ColumnMappings.Add("PrimerApellido", "PrimerApellido");
					bulkCopy.ColumnMappings.Add("SegundoApellido", "SegundoApellido");
					bulkCopy.ColumnMappings.Add("TipoPersona", "TipoPersona");
					bulkCopy.ColumnMappings.Add("RFC", "RFC");
					bulkCopy.ColumnMappings.Add("ClaveRegimenSAT", "ClaveRegimenSAT");
					bulkCopy.ColumnMappings.Add("ClaveUsoSAT", "ClaveUsoSAT");
					bulkCopy.ColumnMappings.Add("CPFiscalSAT", "CPFiscalSAT");
					bulkCopy.ColumnMappings.Add("BaseGravable", "BaseGravable");
					bulkCopy.ColumnMappings.Add("ClasePago", "ClasePago");
					bulkCopy.ColumnMappings.Add("Bimestre", "Bimestre");
					bulkCopy.ColumnMappings.Add("ImpuestoDeterminado", "ImpuestoDeterminado");
					bulkCopy.ColumnMappings.Add("FechaVigencia", "FechaVigencia");
					bulkCopy.ColumnMappings.Add("FolioCarga", "FolioCarga");

					await bulkCopy.WriteToServerAsync(lote);
				}

				// PASO 2: Ingesta a PadronDestino (SP Original)
				using (SqlCommand cmd = new SqlCommand("pred_Operacion.sp_ProcesarMergePadron", conn))
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
	}
}