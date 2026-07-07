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
	/// Procesador de pagos universal que implementa la interfaz IProcesadorFormato. Este procesador realiza la lectura, mapeo, limpieza y validación de archivos de pagos, y luego inserta los datos válidos en la base de datos mediante una operación de inserción masiva.
	/// </summary>
	public class ProcesadorPagosUniversal : IProcesadorFormato
	{
		private readonly string CadenaConexion = ConfiguracionApp.ObtenerCadenaConexion();

		public ResultadoProceso Procesar(string rutaArchivo, ParametrosCarga param)
		{
			var resultadoFinal = new ResultadoProceso { ErroresDetalle = new List<string>() };

			LogService.WriteLogAsync("WARN", param.UsuarioLogin, "ProcesadorPagosUniversal", $"[TRACE] Iniciando lectura inteligente de TXT/CSV. Folio: {param.FolioCarga}").Wait();

			try
			{
				// 1. Convertimos el TXT/CSV a un DataTable usando tu Lector Universal
				var resultadoLectura = LectorUniversal.LeerArchivo(rutaArchivo, Path.GetExtension(rutaArchivo));
				DataTable tablaTXT = resultadoLectura.TablaCruda;

				if (tablaTXT == null || tablaTXT.Rows.Count == 0)
					return resultadoFinal;

				// 🚀 2. REUTILIZAMOS EL MOTOR DE REGIONES (¡Funciona igual para TXT!)
				int filaInicioDatos;
				var mapaCrudo = MapeadorInteligente.ObtenerMapaPorRegiones(tablaTXT, out filaInicioDatos);

				if (mapaCrudo.Count == 0)
					throw new Exception("No se encontraron encabezados válidos en el archivo TXT/CSV.");

				var mapaBloqueado = MapeadorInteligente.ProcesarEncabezadosConMemoria(mapaCrudo);
				// 🚀 CANDADO FINANCIERO 1: Exigir la columna de pago
				if (!mapaBloqueado.Columnas.ContainsKey("ImpuestoDeterminado"))
				{
					throw new Exception("El archivo carece de una columna identificable para el Monto/Pago (Ej. PAGO, IMPORTE, TOTAL, IMPUESTO). Al ser una carga de Pagos, este dato es estrictamente obligatorio.");
				}
				DataTable tablaCrudos = CrearEstructuraRaw();

				// 🚀 3. EL PRE-LAVADO BLINDADO (Idéntico a Excel)
				for (int i = filaInicioDatos; i < tablaTXT.Rows.Count; i++)
				{
					var fila = tablaTXT.Rows[i];

					if (string.IsNullOrWhiteSpace(string.Join("", fila.ItemArray))) continue;

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

					string clasePago = MapeadorInteligente.Extraer(fila, mapaBloqueado, "ClasePago");
					if (string.IsNullOrWhiteSpace(clasePago)) clasePago = "1";

					string bimestre = MapeadorInteligente.RastrearBimestres(fila, mapaBloqueado);
					if (string.IsNullOrWhiteSpace(bimestre)) bimestre = "0";

					string claveMunicipio = MapeadorInteligente.Extraer(fila, mapaBloqueado, "ClaveMunicipio");
					if (string.IsNullOrWhiteSpace(claveMunicipio) && param != null)
					{
						claveMunicipio = param.ClaveMunicipioDestino > 0 ? param.ClaveMunicipioDestino.ToString() : param.OficinaId.ToString();
					}

					string fechaVigencia = MapeadorInteligente.Extraer(fila, mapaBloqueado, "FechaVigencia").Trim();
					if (double.TryParse(fechaVigencia, out double diasExcel) && diasExcel > 10000 && !fechaVigencia.Contains("-") && !fechaVigencia.Contains("/"))
					{
						fechaVigencia = DateTime.FromOADate(diasExcel).ToString("yyyy-MM-dd");
					}
					else if (DateTime.TryParse(fechaVigencia, new System.Globalization.CultureInfo("es-MX"), System.Globalization.DateTimeStyles.None, out DateTime fechaParseada))
					{
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

				var resultadoLimpieza = LimpiadorDatos.LimpiarYValidar(tablaCrudos, "TXT_Pagos", param);

				if (resultadoLimpieza.TablaValidos.Rows.Count > 0)
				{
					InsertarBulk(resultadoLimpieza.TablaValidos);
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
		//public ResultadoProceso Procesar(string rutaArchivo, ParametrosCarga param)
		//{
		//	var resultadoFinal = new ResultadoProceso { ErroresDetalle = new List<string>() };

		//	LogService.WriteLogAsync("WARN", param.UsuarioLogin, "ProcesadorPagosUniversal", $"[TRACE] Iniciando lectura inteligente de TXT/CSV. Folio: {param.FolioCarga}").Wait();

		//	// =========================================================================
		//	// FASE 1: LECTURA CRUDA (Lee CSV, TXT o Excel)
		//	// =========================================================================
		//	var lectura = LectorUniversal.LeerArchivo(rutaArchivo, Path.GetExtension(rutaArchivo));
		//	if (lectura.ErroresEstructurales.Count > 0)
		//	{
		//		resultadoFinal.RegistrosFallidos = 1;
		//		resultadoFinal.ErroresDetalle.AddRange(lectura.ErroresEstructurales);
		//		return resultadoFinal;
		//	}

		//	// =========================================================================
		//	// FASE 2: MAPEO INTELIGENTE Y RADAR MULTI-FILA
		//	// =========================================================================
		//	int filaEncabezadoReal = -1;

		//	// 1. RADAR: Escaneamos las primeras 50 filas buscando la palabra CUENTA
		//	for (int i = 0; i < Math.Min(50, lectura.TablaCruda.Rows.Count); i++)
		//	{
		//		var filaTextos = lectura.TablaCruda.Rows[i].ItemArray.Select(x => x?.ToString().Trim().ToUpper() ?? "");
		//		string textoFilaComplet = string.Join(" ", filaTextos);

		//		if (textoFilaComplet.Contains("CUENTA") || textoFilaComplet.Contains("PREDIAL") || textoFilaComplet.Contains("CTA"))
		//		{
		//			filaEncabezadoReal = i;
		//			break;
		//		}
		//	}

		//	// 2. Si no encontró los encabezados tras buscar, aborta suavemente.
		//	if (filaEncabezadoReal == -1)
		//	{
		//		resultadoFinal.RegistrosFallidos = 1;
		//		resultadoFinal.ErroresDetalle.Add("Error de Mapeo: No se encontraron los encabezados obligatorios (Ej. CUENTA PREDIAL) en el archivo.");
		//		return resultadoFinal;
		//	}

		//	// 3. SÚPER-MAPA: Leemos el encabezado detectado y 2 filas hacia abajo
		//	var mapaColumnas = MapeadorInteligente.ObtenerMapaColumnasMultiFila(lectura.TablaCruda, filaEncabezadoReal, 3);

		//	// 4. Estandarizamos diciendo que los datos empiezan justo debajo del encabezado
		//	DataTable tablaMapeada = MapeadorInteligente.EstandarizarTabla(lectura.TablaCruda, mapaColumnas, filaEncabezadoReal + 1);

		//	// =========================================================================
		//	// FASE 3: LIMPIEZA Y AUTOCORRECCIÓN (Aduana de Reglas de Negocio)
		//	// =========================================================================
		//	var limpieza = LimpiadorDatos.LimpiarYValidar(tablaMapeada, lectura.ContextoPestaña, param);

		//	// =========================================================================
		//	// FASE 4: PREPARACIÓN PARA SQL (Reducir a las 5 columnas de Staging)
		//	// =========================================================================
		//	DataTable tablaStaging = CrearEstructuraPagos();
		//	HashSet<string> pagosProcesados = new HashSet<string>();

		//	foreach (DataRow filaValida in limpieza.TablaValidos.Rows)
		//	{
		//		string claveMun = filaValida["ClaveMunicipio"].ToString();
		//		string tipoPre = filaValida["TipoPredio"].ToString();
		//		string cuenta = filaValida["CuentaPredial"].ToString();
		//		string bimestre = filaValida["Bimestre"].ToString();

		//		// Evitar procesar el mismo pago dos veces en el mismo archivo
		//		string llaveUnica = $"{claveMun}-{tipoPre}-{cuenta}-{bimestre}";
		//		if (pagosProcesados.Contains(llaveUnica)) continue;
		//		pagosProcesados.Add(llaveUnica);

		//		// Pasamos solo los datos que requiere Staging_Etiquetado
		//		tablaStaging.Rows.Add(claveMun, tipoPre, cuenta, param.FolioCarga, bimestre);
		//	}

		//	// =========================================================================
		//	// FASE 5: INSERCIÓN MASIVA
		//	// =========================================================================
		//	if (tablaStaging.Rows.Count > 0)
		//	{
		//		InsertarLoteEnBD(tablaStaging, param);
		//	}

		//	// Consolidamos los resultados para el correo/log
		//	resultadoFinal.RegistrosExitosos = tablaStaging.Rows.Count;
		//	resultadoFinal.RegistrosFallidos = limpieza.TablaRechazados.Rows.Count;
		//	resultadoFinal.ErroresDetalle.AddRange(limpieza.DetallesErrores);

		//	LogService.WriteLogAsync("INFO", param.UsuarioLogin, "ProcesadorPagosUniversal",
		//		$"Fin. Éxitos: {resultadoFinal.RegistrosExitosos}, Errores: {resultadoFinal.RegistrosFallidos}, Ignorados (Viejos): {limpieza.RegistrosIgnorados}");
		//	resultadoFinal.TablaRechazados = limpieza.TablaRechazados;
		//	return resultadoFinal;
		//}

		private DataTable CrearEstructuraPagos()
		{
			var tabla = new DataTable();
			tabla.Columns.Add("ClaveMunicipio", typeof(string));
			tabla.Columns.Add("TipoPredio", typeof(string));
			tabla.Columns.Add("CuentaPredial", typeof(string));
			tabla.Columns.Add("FolioCarga", typeof(int));
			tabla.Columns.Add("Bimestre", typeof(string));
			return tabla;
		}

		private void InsertarLoteEnBD(DataTable lote, ParametrosCarga param)
		{
			using (SqlConnection conn = new SqlConnection(CadenaConexion))
			{
				conn.Open();

				using (SqlBulkCopy bulkCopy = new SqlBulkCopy(conn))
				{
					bulkCopy.DestinationTableName = "pred_Operacion.Staging_Etiquetado";
					bulkCopy.BulkCopyTimeout = 120;
					bulkCopy.WriteToServer(lote);
				}

				// Llamamos al SP que marca las cuentas como pagadas
				using (SqlCommand cmd = new SqlCommand("pred_Operacion.sp_ProcesarMergeEtiquetado", conn))
				{
					cmd.CommandType = CommandType.StoredProcedure;
					cmd.CommandTimeout = 180;
					cmd.Parameters.AddWithValue("@FolioCarga", param.FolioCarga);
					cmd.ExecuteNonQuery();
				}
			}
		}
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

		private void InsertarBulk(DataTable lote)
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
					// 🚀 ALINEACIÓN DE TABLA
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