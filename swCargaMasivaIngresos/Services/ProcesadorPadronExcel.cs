using ExcelDataReader;
using swCargaMasivaIngresos.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using static System.Net.WebRequestMethods;
using File = System.IO.File;

namespace swCargaMasivaIngresos.Services
{
	/// <summary>
	/// Clase ProcesadorPadronExcel que implementa la interfaz IProcesadorFormato. Este procesador se encarga de leer archivos Excel que contienen datos de padrón, mapearlos, limpiarlos y validarlos, y finalmente insertar los registros válidos en la base de datos mediante una operación de inserción masiva.
	/// </summary>
	public class ProcesadorPadronExcel : IProcesadorFormato
	{
		private readonly string CadenaConexion = ConfiguracionApp.ObtenerCadenaConexion();

		/// <summary>
		/// Método Procesar que recibe la ruta del archivo Excel y los parámetros de carga, y realiza la lectura, mapeo, limpieza, validación e inserción de los datos en la base de datos.
		/// </summary>
		/// <param name="rutaArchivo"></param>
		/// <param name="param"></param>
		/// <returns></returns>
		public ResultadoProceso Procesar(string rutaArchivo, ParametrosCarga param)
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

							// --- DATOS SEMI-OBLIGATORIOS (Tienen fallback de seguridad) ---
							string clasePago = MapeadorInteligente.Extraer(fila, mapaBloqueado, "ClasePago");
							if (string.IsNullOrWhiteSpace(clasePago)) clasePago = "1";

							string bimestre = MapeadorInteligente.RastrearBimestres(fila, mapaBloqueado);
							if (string.IsNullOrWhiteSpace(bimestre)) bimestre = "0";

							// Lógica de Fechas de Excel (Con Fallback Lógico)
							string fechaVigencia = MapeadorInteligente.Extraer(fila, mapaBloqueado, "FechaVigencia").Trim();

							if (string.IsNullOrWhiteSpace(fechaVigencia))
							{
								// 🚀 REGLA DE NEGOCIO: Si el municipio no manda fecha, la vigencia es el día de la carga.
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

							// 🚀 2. LLENADO DE LA TABLA (Con extracción segura de las 24 columnas)
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
							InsertarBulkPadronCompleto(resultadoLimpieza.TablaValidos, param);
						}

						resultadoFinal.RegistrosExitosos += resultadoLimpieza.TablaValidos.Rows.Count;
						resultadoFinal.RegistrosFallidos += resultadoLimpieza.TablaRechazados.Rows.Count;
						if (resultadoLimpieza.DetallesErrores != null) resultadoFinal.ErroresDetalle.AddRange(resultadoLimpieza.DetallesErrores);
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
		/// Método privado que crea la estructura de un DataTable para almacenar temporalmente los datos del padrón antes de ser insertados en la base de datos. Define las columnas necesarias y sus tipos de datos.
		/// </summary>
		/// <returns></returns>
		private DataTable CrearEstructuraPadron()
		{
			DataTable tabla = new DataTable();
			tabla.Columns.Add("ClaveMunicipio", typeof(string));
			tabla.Columns.Add("TipoPredio", typeof(string));
			tabla.Columns.Add("CuentaPredial", typeof(string));
			tabla.Columns.Add("ClasePago", typeof(string));
			tabla.Columns.Add("Bimestre", typeof(string));
			tabla.Columns.Add("ImpuestoDeterminado", typeof(string));
			tabla.Columns.Add("FechaVigencia", typeof(string));
			tabla.Columns.Add("FolioCarga", typeof(string));
			return tabla;
		}

		/// <summary>
		/// Crea la estructura de 24 columnas para la tabla Staging_Predial
		/// </summary>
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
		/// Inserta el bloque de datos y dispara el SP de consolidación
		/// </summary>
		private void InsertarBulkPadronCompleto(DataTable lote, ParametrosCarga param)
		{
			using (SqlConnection conn = new SqlConnection(CadenaConexion))
			{
				conn.Open();

				using (SqlBulkCopy bulkCopy = new SqlBulkCopy(conn))
				{
					bulkCopy.DestinationTableName = "pred_Operacion.Staging_Predial";
					bulkCopy.BatchSize = 10000;
					bulkCopy.BulkCopyTimeout = 120;

					// 🚀 3. EL MAPEO DE LAS 24 COLUMNAS PARA SQL SERVER
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

					bulkCopy.WriteToServer(lote);
				}

				using (SqlCommand cmd = new SqlCommand("pred_Operacion.sp_ProcesarMergePadron", conn))
				{
					cmd.CommandType = CommandType.StoredProcedure;
					cmd.CommandTimeout = 180;
					cmd.Parameters.AddWithValue("@FolioCarga", param.FolioCarga);
					cmd.ExecuteNonQuery();
				}
			}
		}
		/// <summary>
		/// Método privado que realiza la inserción masiva de los registros válidos del padrón en la base de datos utilizando SqlBulkCopy. Se asegura de mapear correctamente las columnas del DataTable a las columnas de la tabla de destino en la base de datos.
		/// </summary>
		/// <param name="lote"></param>
		private void InsertarBulkPadron(DataTable lote, ParametrosCarga param)
		{
			foreach (DataRow row in lote.Rows)
			{
				if (row["FechaVigencia"] != DBNull.Value && string.IsNullOrWhiteSpace(row["FechaVigencia"].ToString())) row["FechaVigencia"] = DBNull.Value;
				if (row["ImpuestoDeterminado"] != DBNull.Value && string.IsNullOrWhiteSpace(row["ImpuestoDeterminado"].ToString())) row["ImpuestoDeterminado"] = "0";
				if (row["Bimestre"] != DBNull.Value && string.IsNullOrWhiteSpace(row["Bimestre"].ToString())) row["Bimestre"] = "0";
				if (row["ClasePago"] != DBNull.Value && string.IsNullOrWhiteSpace(row["ClasePago"].ToString())) row["ClasePago"] = "1";
			}

			using (SqlConnection conn = new SqlConnection(CadenaConexion))
			{
				conn.Open();

				// 1. ADUANA: Metemos los datos a Staging
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

				// 🚀 2. CONSOLIDACIÓN: El eslabón que faltaba
				using (SqlCommand cmd = new SqlCommand("pred_Operacion.sp_ProcesarMergePadron", conn))
				{
					cmd.CommandType = CommandType.StoredProcedure;
					cmd.CommandTimeout = 180;
					cmd.Parameters.AddWithValue("@FolioCarga", param.FolioCarga);
					cmd.ExecuteNonQuery();
				}
			}
		}
		// Agrega este método al final de la clase ProcesadorPadronExcel
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
		private void InsertarBulkPadron_v01(DataTable lote)
		{
			foreach (DataRow row in lote.Rows)
			{
				if (row["FechaVigencia"] != DBNull.Value && string.IsNullOrWhiteSpace(row["FechaVigencia"].ToString())) row["FechaVigencia"] = DBNull.Value;
				if (row["ImpuestoDeterminado"] != DBNull.Value && string.IsNullOrWhiteSpace(row["ImpuestoDeterminado"].ToString())) row["ImpuestoDeterminado"] = "0";
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