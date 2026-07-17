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
	/// Clase encargada de procesar archivos de texto plano (TXT) con el formato específico del padrón catastral.
	/// </summary>
	public class ProcesadorPadronTXT : IProcesadorFormato
	{
		private readonly string CadenaConexion = ConfiguracionApp.ObtenerCadenaConexion();

		/// <summary>
		/// Método principal asíncrono para procesar el archivo de texto plano del padrón catastral.
		/// </summary>
		/// <param name="rutaArchivo"></param>
		/// <param name="param"></param>
		/// <returns></returns>
		public async Task<ResultadoProceso> ProcesarAsync(string rutaArchivo, ParametrosCarga param)
		{
			var resultado = new ResultadoProceso { ErroresDetalle = new List<string>() };
			DataTable tablaLote = CrearEstructuraPadron();

			LogService.WriteLogAsync("INFO", param.UsuarioLogin, "ProcesadorPadronTXT", $"Inicia lectura de archivo Folio: {param.FolioCarga}").Wait();

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

					// 🚀 DETECCIÓN DEL DELIMITADOR (Solo se ejecuta en la primera línea válida)
					if (!delimitadorDetectado)
					{
						if (linea.Contains("|")) delimitador = '|';
						else if (linea.Contains(",")) delimitador = ',';
						else if (linea.Contains("\t")) delimitador = '\t';

						delimitadorDetectado = true;
						LogService.WriteLogAsync("INFO", param.UsuarioLogin, "ProcesadorPadronTXT", $"Delimitador detectado automáticamente: '{delimitador}'").Wait();
					}

					// Partimos la línea usando el delimitador que el sistema descubrió
					string[] col = linea.Split(delimitador);

					// 1. Validar Layout estricto de 24 columnas
					if (col.Length != 24)
					{
						MarcarError(resultado, numeroLinea, $"Columnas incorrectas. Se esperaban 24, llegaron {col.Length} usando el separador '{delimitador}'.");
						continue;
					}

					// Extraemos los componentes clave
					string claveMunicipio = col[0].Trim();
					string tipoPredio = col[1].Trim();
					string cuentaPredial = col[2].Trim();

					if (string.IsNullOrEmpty(cuentaPredial))
					{
						MarcarError(resultado, numeroLinea, "El número de cuenta predial es obligatorio.");
						continue;
					}

					// ====================================================================
					// 🚀 NUEVO: VALIDACIÓN ESTRICTA DE TIPOS NUMÉRICOS Y CATÁLOGOS
					// ====================================================================

					// Columna 1: Clave Municipio (Obligatorio, numérico de 1 a 217)
					if (!short.TryParse(claveMunicipio, out short claveMun) || claveMun < 1 || claveMun > 217)
					{
						// Fallback de seguridad (Opcional, pero recomendado en TXT también)
						if (param.ClaveMunicipioDestino > 0)
						{
							claveMun = (short)param.ClaveMunicipioDestino;
						}
						else
						{
							MarcarError(resultado, numeroLinea, "Clave de municipio inválida (Debe ser numérico entre 1 y 217).");
							continue;
						}
					}

					// Columna 2: Tipo de Predio (Obligatorio, numérico de 1 a 3)
					if (!byte.TryParse(tipoPredio, out byte tipoPre) || tipoPre < 1 || tipoPre > 3)
					{
						MarcarError(resultado, numeroLinea, "Tipo de predio inválido (1=Urbano, 2=Rústico, 3=Suburbano).");
						continue;
					}

					// Columna 4: Folio Único (Opcional, numérico BIGINT)
					string folioUnicoStr = col[3].Trim();
					if (folioUnicoStr.Equals("NULL", StringComparison.OrdinalIgnoreCase) ||
						folioUnicoStr.Equals("NA", StringComparison.OrdinalIgnoreCase) ||
						folioUnicoStr.Equals("-", StringComparison.OrdinalIgnoreCase))
					{
						folioUnicoStr = string.Empty;
					}

					if (!string.IsNullOrWhiteSpace(folioUnicoStr))
					{
						if (!long.TryParse(folioUnicoStr, out _))
						{
							MarcarError(resultado, numeroLinea, $"Folio Único inválido. Se esperaba numérico pero se recibió: '{folioUnicoStr}'");
							continue;
						}
					}

					// Columna 15: Tipo de Persona (Opcional, pero si viene debe ser 1 o 2)
					if (!string.IsNullOrWhiteSpace(col[14]))
					{
						if (!byte.TryParse(col[14].Trim(), out byte tipoPer) || (tipoPer != 1 && tipoPer != 2))
						{
							MarcarError(resultado, numeroLinea, "Tipo de Persona inválida (1=Física, 2=Moral).");
							continue;
						}
					}

					// 🚀 LÓGICA DE BANDERAS 99 (Clase de Pago y Bimestre)
					string clasePagoStr = col[20].Trim();
					string bimestreStr = col[21].Trim();

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

					if (!string.IsNullOrWhiteSpace(bimestreStr))
					{
						if (!byte.TryParse(bimestreStr, out bimestre) || bimestre > 6)
						{
							MarcarError(resultado, numeroLinea, "Bimestre inválido (Debe ser un número del 0 al 6).");
							continue;
						}
					}

					// ====================================================================

					// Validaciones de moneda y fechas
					decimal baseGravable = 0;
					if (!string.IsNullOrWhiteSpace(col[19]) && !Utilerias.TryParseMoneda(col[19], out baseGravable))
					{
						MarcarError(resultado, numeroLinea, "Base gravable inválida.");
						continue;
					}

					if (!Utilerias.TryParseMoneda(col[22], out decimal impuestoPagar))
					{
						MarcarError(resultado, numeroLinea, "Impuesto determinado inválido.");
						continue;
					}

					DateTime fechaVigencia = DateTime.Now; // Por defecto asumimos la fecha de carga
					string fechaTxt = col[23].Trim();

					if (!string.IsNullOrWhiteSpace(fechaTxt))
					{
						// Forzamos estrictamente el formato de México (DD/MM/AAAA) para evitar inversiones de mes/día
						if (!DateTime.TryParse(fechaTxt, new System.Globalization.CultureInfo("es-MX"), System.Globalization.DateTimeStyles.None, out fechaVigencia))
						{
							MarcarError(resultado, numeroLinea, $"Fecha de vigencia inválida ('{fechaTxt}'). Se esperaba formato de México (DD/MM/AAAA).");
							continue;
						}
					}

					// 3. Se agregan los datos limpios al lote (Todo va como string hacia Staging)
					tablaLote.Rows.Add(
						claveMun.ToString(),                  // 1. Ya validado
						tipoPre.ToString(),                   // 2. Ya validado
						cuentaPredial,                        // 3.
						Utilerias.LimpiarCadena(folioUnicoStr, 50), // 4 Usamos la variable limpia
						Utilerias.LimpiarCadena(col[4], 150), // 5. Localidad
						Utilerias.LimpiarCadena(col[5], 150), // 6. Calle
						Utilerias.LimpiarCadena(col[6], 20),  // 7. Num Ext
						Utilerias.LimpiarCadena(col[7], 20),  // 8. Num Int
						Utilerias.LimpiarCadena(col[8], 10),  // 9. Letra
						Utilerias.LimpiarCadena(col[9], 150), // 10. Colonia
						Utilerias.LimpiarCadena(col[10], 10), // 11. CP (Conserva ceros)
						Utilerias.LimpiarCadena(col[11], 150),// 12. Nombre / Razon Social
						Utilerias.LimpiarCadena(col[12], 100),// 13. Apellido 1
						Utilerias.LimpiarCadena(col[13], 100),// 14. Apellido 2
						Utilerias.LimpiarCadena(col[14], 5),  // 15. Tipo Persona (Validada 1 o 2)
						Utilerias.LimpiarCadena(col[15], 15), // 16. RFC
						Utilerias.LimpiarCadena(col[16], 10), // 17. Regimen SAT
						Utilerias.LimpiarCadena(col[17], 10), // 18. Uso SAT
						Utilerias.LimpiarCadena(col[18], 10), // 19. CP Fiscal (Conserva ceros)
						baseGravable,                         // 20. Base Gravable
						clasePago.ToString(),                 // 21. Ya validado o Bandera 99
						bimestre.ToString(),                  // 22. Ya validado o Bandera 99
						impuestoPagar,                        // 23. Impuesto Determinado
						fechaVigencia,                        // 24. Fecha Vigencia
						param.FolioCarga                      // Columna de Control
					);

					resultado.RegistrosExitosos++;

					// 4. Inserción masiva si se alcanza el tamaño del lote
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

			LogService.WriteLogAsync("INFO", param.UsuarioLogin, "ProcesadorPadronTXT",
				$"Fin. Éxitos: {resultado.RegistrosExitosos}, Errores: {resultado.ErroresDetalle.Count}").Wait();

			return resultado;
		}

		/// <summary>
		/// Marca un error en el resultado del proceso, incrementando el contador de registros fallidos y agregando un detalle del error.
		/// </summary>
		private void MarcarError(ResultadoProceso res, int linea, string msg)
		{
			res.RegistrosFallidos++;
			res.ErroresDetalle.Add($"Línea {linea}: {msg}");
		}

		/// <summary>
		/// Método privado que crea la estructura de un DataTable para almacenar temporalmente los datos del padrón antes de ser insertados en la base de datos.
		/// </summary>
		private DataTable CrearEstructuraPadron()
		{
			var tabla = new DataTable();
			// Dejamos las columnas base como strings para el mapeo rápido a Staging_Predial (VARCHAR)
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
			tabla.Columns.Add("BaseGravable", typeof(decimal));
			tabla.Columns.Add("ClasePago", typeof(string));
			tabla.Columns.Add("Bimestre", typeof(string));
			tabla.Columns.Add("ImpuestoDeterminado", typeof(decimal));
			tabla.Columns.Add("FechaVigencia", typeof(DateTime));
			tabla.Columns.Add("FolioCarga", typeof(int));
			return tabla;
		}

		/// <summary>
		/// Método privado asíncrono que realiza la inserción masiva en Staging, ejecuta la ingesta al Padrón, y finalmente consolida los adeudos devolviendo los errores.
		/// </summary>
		private async Task<List<string>> InsertarLoteEnBDAsync(DataTable lote, ParametrosCarga param)
		{
			var erroresConsolidacion = new List<string>();

			try
			{
				using (SqlConnection conn = new SqlConnection(CadenaConexion))
				{
					await conn.OpenAsync();

					// PASO 1: Inyectar masivamente a la tabla Staging
					using (SqlBulkCopy bulkCopy = new SqlBulkCopy(conn))
					{
						bulkCopy.DestinationTableName = "pred_Operacion.Staging_Predial";
						bulkCopy.BatchSize = 10000;
						bulkCopy.BulkCopyTimeout = 120;
						await bulkCopy.WriteToServerAsync(lote);
					}

					// PASO 2: Llamada al Procedimiento Almacenado que hace el MERGE (Ingesta Inicial)
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
								erroresConsolidacion.Add($"[Consolidación TXT] Cuenta {cuenta}: {mensaje}");
							}
						}
					}
				}
			}
			catch (SqlException ex)
			{
				LogService.WriteLogAsync("ERROR", param.UsuarioLogin, "SqlBulkCopy/SP TXT", ex.Message).Wait();
				throw;
			}

			return erroresConsolidacion;
		}
	}
}