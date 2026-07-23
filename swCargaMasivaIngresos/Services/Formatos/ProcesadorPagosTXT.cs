using swCargaMasivaIngresos.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace swCargaMasivaIngresos.Services
{
	/// <summary>
	/// Clase ProcesadorPagosTXT que implementa la interfaz IProcesadorFormato. Este procesador se encarga de leer archivos de texto (TXT) que contienen datos de pagos, mapearlos, limpiarlos y validarlos, y finalmente insertar los registros válidos en la base de datos mediante una operación de inserción masiva.
	/// </summary>
	public class ProcesadorPagosTXT : IProcesadorFormato
	{
		private readonly string CadenaConexion = ConfiguracionApp.ObtenerCadenaConexion();

		/// <summary>
		/// Método principal asíncrono que procesa un archivo de pagos en formato TXT. Lee el archivo línea por línea, valida cada registro, y si es válido, lo agrega a una tabla en memoria. Realiza inserción masiva y ejecuta la consolidación.
		/// </summary>
		/// <param name="rutaArchivo"></param>
		/// <param name="param"></param>
		/// <returns></returns>
		public async Task<ResultadoProceso> ProcesarAsync(string rutaArchivo, ParametrosCarga param)
		{
			var resultado = new ResultadoProceso { ErroresDetalle = new List<string>() };
			DataTable tablaLote = CrearEstructuraPagos();

			LogService.WriteLogAsync("INFO", param.UsuarioLogin, "ProcesadorPagosTXT", $"Inicia lectura de archivo Folio: {param.FolioCarga}").Wait();

			using (var reader = new StreamReader(rutaArchivo, Encoding.UTF8))
			{
				string linea;
				int numeroLinea = 0;

				// 🚀 VARIABLES PARA DETECCIÓN DINÁMICA
				char delimitador = '|'; // Pipe por defecto
				bool delimitadorDetectado = false;

				while ((linea = await reader.ReadLineAsync()) != null)
				{
					numeroLinea++;
					if (string.IsNullOrWhiteSpace(linea)) continue;

					// DETECCIÓN DEL DELIMITADOR (Solo se ejecuta en la primera línea válida)
					if (!delimitadorDetectado)
					{
						if (linea.Contains("|")) delimitador = '|';
						else if (linea.Contains(",")) delimitador = ',';
						else if (linea.Contains("\t")) delimitador = '\t';

						delimitadorDetectado = true;
						LogService.WriteLogAsync("INFO", param.UsuarioLogin, "ProcesadorPagosTXT", $"Delimitador detectado automáticamente: '{delimitador}'").Wait();
					}

					// Partimos la línea usando el delimitador que el sistema descubrió
					string[] col = linea.Split(delimitador);

					// 1. Validar Layout (Permitir el estricto de 24 columnas o el reducido de 5/6 columnas)
					if (col.Length != 24 && col.Length < 5)
					{
						MarcarError(resultado, numeroLinea, $"Columnas incorrectas. Se esperaban 24 o al menos 5, llegaron {col.Length} usando el separador '{delimitador}'.");
						continue;
					}

					// Extraemos los componentes clave
					// 2. Extraer componentes clave de forma dinámica
					string claveMunicipio = col[0].Trim();
					string tipoPredio = col[1].Trim();
					string cuentaPredial = col[2].Trim();

					string clasePagoStr = "";
					string strBimestre = "";
					string impuestoDeterminadoStr = "0";

					// Si es el layout completo de 24 columnas
					if (col.Length == 24)
					{
						clasePagoStr = col[20].Trim();
						strBimestre = col[21].Trim();
						impuestoDeterminadoStr = col[22].Trim();
					}
					// Si es el layout reducido sin encabezados (Mínimo 5 columnas)
					else if (col.Length >= 5 && col.Length < 24)
					{
						clasePagoStr = col[3].Trim();
						strBimestre = col[4].Trim();
						if (col.Length >= 6) impuestoDeterminadoStr = col[5].Trim(); // Por si incluyen el monto al final
					}

					// 3. Validaciones de Negocio Obligatorias
					if (string.IsNullOrEmpty(cuentaPredial))
					{
						MarcarError(resultado, numeroLinea, "La Cuenta Predial es obligatoria.");
						continue;
					}

					// Fallback Seguro de Municipio (Igual que en Padrón)
					if (!short.TryParse(claveMunicipio, out short claveMun) || claveMun < 1 || claveMun > 217)
					{
						if (param.ClaveMunicipioDestino > 0)
						{
							claveMun = (short)param.ClaveMunicipioDestino; // Cast explícito aplicado
						}
						else
						{
							MarcarError(resultado, numeroLinea, "Clave de municipio inválida (1 a 217).");
							continue;
						}
					}

					if (!byte.TryParse(tipoPredio, out byte tipoPre) || tipoPre < 1 || tipoPre > 3)
					{
						MarcarError(resultado, numeroLinea, "Tipo de predio inválido (1=Urbano, 2=Rústico, 3=Suburbano).");
						continue;
					}

					// 🚀 LÓGICA DE BANDERAS 99 (Clase de Pago y Bimestre)
					byte clasePago = 99; // Bandera por defecto
					byte bimestre = 99;  // Bandera por defecto

					if (!string.IsNullOrWhiteSpace(clasePagoStr))
					{
						if (!byte.TryParse(clasePagoStr, out clasePago) || (clasePago != 1 && clasePago != 2))
						{
							MarcarError(resultado, numeroLinea, "Clase de Pago inválida (1=Anual, 2=Bimestral).");
							continue;
						}
					}

					if (!string.IsNullOrWhiteSpace(strBimestre))
					{
						if (!byte.TryParse(strBimestre, out bimestre) || bimestre > 6)
						{
							MarcarError(resultado, numeroLinea, "Bimestre inválido (Debe ser un número del 0 al 6).");
							continue;
						}
					}

					decimal impuestoDeterminadoDec = 0m;
					if (!string.IsNullOrWhiteSpace(impuestoDeterminadoStr))
					{
						decimal.TryParse(impuestoDeterminadoStr, out impuestoDeterminadoDec);
					}

					// 5. Agregar a la tabla en memoria
					tablaLote.Rows.Add(
						claveMun.ToString(),
						tipoPre.ToString(),
						cuentaPredial,
						param.FolioCarga,
						bimestre.ToString(),
						clasePago.ToString(),
						impuestoDeterminadoDec,
						DateTime.Now.ToString("yyyy-MM-dd")
					);

					resultado.RegistrosExitosos++;

					if (tablaLote.Rows.Count >= 10000)
					{
						List<string> erroresLogicos = await InsertarLoteEnBDAsync(tablaLote, param);
						if (erroresLogicos.Count > 0) resultado.ErroresDetalle.AddRange(erroresLogicos);
						tablaLote.Clear();
					}
				}

				if (tablaLote.Rows.Count > 0)
				{
					List<string> erroresLogicos = await InsertarLoteEnBDAsync(tablaLote, param);
					if (erroresLogicos.Count > 0) resultado.ErroresDetalle.AddRange(erroresLogicos);
				}
			}

			return resultado;
		}

		/// <summary>
		/// Marca un error en el resultado del proceso, incrementando el contador de registros fallidos y agregando un mensaje de error detallado.
		/// </summary>
		private void MarcarError(ResultadoProceso res, int linea, string msg)
		{
			res.RegistrosFallidos++;
			res.ErroresDetalle.Add($"Línea {linea}: {msg}");
		}

		/// <summary>
		/// Método privado que crea la estructura de un DataTable para almacenar temporalmente los datos de pagos antes de ser insertados en la base de datos.
		/// </summary>
		private DataTable CrearEstructuraPagos()
		{
			var tabla = new DataTable();
			tabla.Columns.Add("ClaveMunicipio", typeof(string));
			tabla.Columns.Add("TipoPredio", typeof(string));
			tabla.Columns.Add("CuentaPredial", typeof(string));
			tabla.Columns.Add("FolioCarga", typeof(int));
			tabla.Columns.Add("Bimestre", typeof(string));
			tabla.Columns.Add("ClasePago", typeof(string));
			tabla.Columns.Add("ImpuestoDeterminado", typeof(decimal));
			tabla.Columns.Add("FechaVigencia", typeof(string));
			return tabla;
		}

		/// <summary>
		/// Método privado asíncrono que realiza la inserción masiva de un lote de registros en Staging, ejecuta la ingesta y consolida los pagos (capturando los errores).
		/// </summary>
		private async Task<List<string>> InsertarLoteEnBDAsync(DataTable lote, ParametrosCarga param)
		{
			var erroresConsolidacion = new List<string>();

			try
			{
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

					// PASO 2: Ingesta (Merge Original)
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
								erroresConsolidacion.Add($"[Consolidación TXT] Cuenta {cuenta}: {mensaje}");
							}
						}
					}
				}
			}
			catch (SqlException ex)
			{
				LogService.WriteLogAsync("ERROR", param.UsuarioLogin, "SqlBulkCopy/SP Pagos TXT", ex.Message).Wait();
				throw;
			}

			return erroresConsolidacion;
		}
	}
}