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
	/// Clase encargada de procesar archivos de pagos en formato TXT o CSV de manera universal. Esta clase implementa la interfaz IProcesadorFormato y proporciona métodos para leer, mapear, limpiar y validar los datos del archivo, así como para insertar los registros válidos en la base de datos mediante operaciones bulk y procedimientos almacenados.
	/// </summary>
	public class ProcesadorPagosUniversal : IProcesadorFormato
	{
		private readonly string CadenaConexion = ConfiguracionApp.ObtenerCadenaConexion();

		/// <summary>
		/// Método principal para procesar un archivo de pagos en formato TXT o CSV. Este método realiza la lectura del archivo, mapea los encabezados, limpia y valida los datos, y finalmente inserta los registros válidos en la base de datos.
		/// </summary>
		/// <param name="rutaArchivo"></param>
		/// <param name="param"></param>
		/// <returns></returns>
		public ResultadoProceso Procesar(string rutaArchivo, ParametrosCarga param)
		{
			var resultadoFinal = new ResultadoProceso { ErroresDetalle = new List<string>() };

			LogService.WriteLogAsync("WARN", param.UsuarioLogin, "ProcesadorPagosUniversal", $"[TRACE] Iniciando lectura inteligente de TXT/CSV. Folio: {param.FolioCarga}").Wait();

			try
			{
				var resultadoLectura = LectorUniversal.LeerArchivo(rutaArchivo, Path.GetExtension(rutaArchivo));
				DataTable tablaTXT = resultadoLectura.TablaCruda;

				if (tablaTXT == null || tablaTXT.Rows.Count == 0) return resultadoFinal;

				int filaInicioDatos;
				var mapaCrudo = MapeadorInteligente.ObtenerMapaPorRegiones(tablaTXT, out filaInicioDatos);

				if (mapaCrudo.Count == 0)
					throw new Exception("No se encontraron encabezados válidos en el archivo TXT/CSV.");

				var mapaBloqueado = MapeadorInteligente.ProcesarEncabezadosConMemoria(mapaCrudo);
				DataTable tablaCrudos = CrearEstructuraRaw();

				for (int i = filaInicioDatos; i < tablaTXT.Rows.Count; i++)
				{
					var fila = tablaTXT.Rows[i];
					if (string.IsNullOrWhiteSpace(string.Join("", fila.ItemArray))) continue;

					// 🚀 EXTRACCIÓN SEGURA
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

					string clasePago = ExtraerSeguro(fila, mapaBloqueado, "ClasePago", "1");
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
					nuevaFila["ImpuestoDeterminado"] = ExtraerSeguro(fila, mapaBloqueado, "ImpuestoDeterminado", "");
					nuevaFila["FechaVigencia"] = fechaVigencia;

					tablaCrudos.Rows.Add(nuevaFila);
				}

				var resultadoLimpieza = LimpiadorDatos.LimpiarYValidar(tablaCrudos, "TXT_Pagos", param);

				if (resultadoLimpieza.TablaValidos.Rows.Count > 0)
				{
					InsertarBulk(resultadoLimpieza.TablaValidos, param);
				}

				resultadoFinal.RegistrosExitosos += resultadoLimpieza.TablaValidos.Rows.Count;
				resultadoFinal.RegistrosFallidos += resultadoLimpieza.TablaRechazados.Rows.Count;
				if (resultadoLimpieza.DetallesErrores != null) resultadoFinal.ErroresDetalle.AddRange(resultadoLimpieza.DetallesErrores);
			}
			catch (Exception ex)
			{
				LogService.WriteLogAsync("ERROR", param.UsuarioLogin, "ProcesadorPagosUniversal", $"Fallo crítico: {ex.Message}").Wait();
				throw;
			}

			return resultadoFinal;
		}

		/// <summary>
		/// Método auxiliar para extraer valores de una fila de datos de manera segura, manejando posibles excepciones y proporcionando un valor por defecto en caso de error o valor nulo.
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
		/// Método que crea y devuelve la estructura de un DataTable para almacenar temporalmente los datos crudos del archivo TXT/CSV antes de ser procesados e insertados en la base de datos. La estructura incluye columnas como ClaveMunicipio, TipoPredio, CuentaPredial, ClasePago, Bimestre, ImpuestoDeterminado, FechaVigencia y FolioCarga.
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
		/// Método que realiza la inserción masiva de registros válidos en la base de datos utilizando SqlBulkCopy y posteriormente llama a un procedimiento almacenado para procesar los datos insertados. Este método asegura que los datos se transfieran de manera eficiente y segura a la tabla de staging correspondiente.
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