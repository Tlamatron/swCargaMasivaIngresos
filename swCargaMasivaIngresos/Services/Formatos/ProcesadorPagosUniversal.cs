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

			// Aseguramos tener la extensión
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

					if (mapaCrudo.Count == 0)
					{
						resultadoFinal.ErroresDetalle.Add($"No se encontraron encabezados válidos en la pestaña: {hoja.ContextoPestaña}");
						continue;
					}

					var mapaBloqueado = MapeadorInteligente.ProcesarEncabezadosConMemoria(mapaCrudo);
					DataTable tablaCrudos = CrearEstructuraRaw();

					for (int i = filaInicioDatos; i < hoja.TablaCruda.Rows.Count; i++)
					{
						var fila = hoja.TablaCruda.Rows[i];
						if (string.IsNullOrWhiteSpace(string.Join("", fila.ItemArray))) continue;

						// --- EXTRACCIÓN SEGURA ---
						string cuentaPredial = ExtraerSeguro(fila, mapaBloqueado, "CuentaPredial", "");
						if (string.IsNullOrWhiteSpace(cuentaPredial) || cuentaPredial.Equals("Cuenta", StringComparison.OrdinalIgnoreCase)) continue;
						if (cuentaPredial.EndsWith(".0")) cuentaPredial = cuentaPredial.Replace(".0", "");

						string anioPredial = ExtraerSeguro(fila, mapaBloqueado, "Anio", "");
						if (string.IsNullOrWhiteSpace(anioPredial)) anioPredial = DateTime.Now.Year.ToString();
						if (anioPredial.Contains("2025") || anioPredial.Contains("2024") || anioPredial.Contains("2023") || anioPredial.Contains("2022") || anioPredial.Contains("2021")) continue;

						string tipoPredio = ExtraerSeguro(fila, mapaBloqueado, "TipoPredio", "").ToUpper().Trim();
						if (tipoPredio == "U" || tipoPredio.StartsWith("URBANO")) tipoPredio = "1";
						else if (tipoPredio == "R" || tipoPredio.StartsWith("RUSTICO") || tipoPredio.StartsWith("RÚSTICO")) tipoPredio = "2";
						else if (tipoPredio == "S" || tipoPredio.StartsWith("SUBURBANO") || tipoPredio.StartsWith("SUB")) tipoPredio = "3";
						if (string.IsNullOrWhiteSpace(tipoPredio)) tipoPredio = "1";

						string clasePago = ExtraerSeguro(fila, mapaBloqueado, "ClasePago", "99");
						string bimestre = MapeadorInteligente.RastrearBimestres(fila, mapaBloqueado);
						if (string.IsNullOrWhiteSpace(bimestre)) bimestre = "99";

						string claveMunicipio = ExtraerSeguro(fila, mapaBloqueado, "ClaveMunicipio", "");
						if (string.IsNullOrWhiteSpace(claveMunicipio) && param != null)
						{
							claveMunicipio = param.ClaveMunicipioDestino > 0 ? param.ClaveMunicipioDestino.ToString() : param.OficinaId.ToString();
						}

						string fechaVigencia = ExtraerSeguro(fila, mapaBloqueado, "FechaVigencia", "").Trim();
						if (string.IsNullOrWhiteSpace(fechaVigencia)) fechaVigencia = DateTime.Now.ToString("yyyy-MM-dd");
						else if (double.TryParse(fechaVigencia, out double diasExcel) && diasExcel > 10000 && !fechaVigencia.Contains("-") && !fechaVigencia.Contains("/")) fechaVigencia = DateTime.FromOADate(diasExcel).ToString("yyyy-MM-dd");
						else if (DateTime.TryParse(fechaVigencia, new System.Globalization.CultureInfo("es-MX"), System.Globalization.DateTimeStyles.None, out DateTime fechaParseada)) fechaVigencia = fechaParseada.ToString("yyyy-MM-dd");
						else fechaVigencia = DateTime.Now.ToString("yyyy-MM-dd");

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

					var resultadoLimpieza = LimpiadorDatos.LimpiarYValidar(tablaCrudos, hoja.ContextoPestaña, param);

					if (resultadoLimpieza.TablaValidos.Rows.Count > 0)
					{
						// Ejecución asíncrona de Ingesta y Consolidación
						List<string> erroresLogicos = await InsertarBulkAsync(resultadoLimpieza.TablaValidos, param);

						if (erroresLogicos.Any())
						{
							resultadoFinal.ErroresDetalle.AddRange(erroresLogicos);
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
			return dt;
		}

		/// <summary>
		/// Inserta asíncronamente los registros en la base de datos (Staging), llama al SP de ingesta, 
		/// ejecuta el SP de Consolidación de adeudos y captura cualquier error lógico devuelto.
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

				// PASO 2: Ingesta a PadronDestino
				using (SqlCommand cmd = new SqlCommand("pred_Operacion.sp_ProcesarMergeEtiquetado", conn))
				{
					cmd.CommandType = CommandType.StoredProcedure;
					cmd.CommandTimeout = 180;
					cmd.Parameters.AddWithValue("@FolioCarga", param.FolioCarga);
					await cmd.ExecuteNonQueryAsync();
				}

				// 🚀 PASO 3: CONSOLIDACIÓN Y CAPTURA DE ERRORES
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