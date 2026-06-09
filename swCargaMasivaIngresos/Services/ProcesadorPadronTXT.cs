using swCargaMasivaIngresos.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.Text;

namespace swCargaMasivaIngresos.Services
{
	/// <summary>
	/// Clase encargada de procesar archivos de texto plano (TXT) con el formato específico del padrón catastral.
	/// </summary>
	public class ProcesadorPadronTXT : IProcesadorFormato
	{
		private readonly string AppName = System.Configuration.ConfigurationManager.AppSettings["NombAplicacion"] ?? "APICargaMasivaIngresos";
		private readonly string CadenaConexion = System.Configuration.ConfigurationManager.ConnectionStrings["ConexionSQL"].ConnectionString;

		public ResultadoProceso Procesar(string rutaArchivo, ParametrosCarga param)
		{
			var resultado = new ResultadoProceso { ErroresDetalle = new List<string>() };
			DataTable tablaLote = CrearEstructuraPadron();
			HashSet<string> cuentasProcesadas = new HashSet<string>();

			LogService.WriteLogAsync(AppName, "INFO", param.UsuarioLogin, "ProcesadorPadronTXT", $"Inicia lectura de archivo Folio: {param.FolioCarga}");

			using (var reader = new StreamReader(rutaArchivo, Encoding.UTF8))
			{
				string linea;
				int numeroLinea = 0;

				while ((linea = reader.ReadLine()) != null)
				{
					numeroLinea++;
					if (string.IsNullOrWhiteSpace(linea)) continue;

					string[] col = linea.Split('|');

					// 1. Validar Layout estricto de 24 columnas
					if (col.Length != 24)
					{
						MarcarError(resultado, numeroLinea, $"Columnas incorrectas. Se esperaban 24, llegaron {col.Length}.");
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
						MarcarError(resultado, numeroLinea, "Clave de municipio inválida (Debe ser numérico entre 1 y 217).");
						continue;
					}

					// Columna 2: Tipo de Predio (Obligatorio, numérico de 1 a 3)
					if (!byte.TryParse(tipoPredio, out byte tipoPre) || tipoPre < 1 || tipoPre > 3)
					{
						MarcarError(resultado, numeroLinea, "Tipo de predio inválido (1=Urbano, 2=Rústico, 3=Suburbano).");
						continue;
					}

					// Columna 4: Folio Único (Opcional, pero si viene debe ser numérico BIGINT)
					if (!string.IsNullOrWhiteSpace(col[3]) && !long.TryParse(col[3].Trim(), out _))
					{
						MarcarError(resultado, numeroLinea, "Folio Único inválido (Debe ser exclusivamente numérico).");
						continue;
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

					// Columna 21: Clase de Pago (Obligatorio, 1 o 2)
					if (!byte.TryParse(col[20].Trim(), out byte clasePago) || (clasePago != 1 && clasePago != 2))
					{
						MarcarError(resultado, numeroLinea, "Clase de Pago inválida (1=Anual, 2=Bimestral).");
						continue;
					}

					// Columna 22: Bimestre (Obligatorio, 0 al 6)
					if (!byte.TryParse(col[21].Trim(), out byte bimestre) || bimestre > 6)
					{
						MarcarError(resultado, numeroLinea, "Bimestre inválido (Debe ser un número del 0 al 6).");
						continue;
					}

					// ====================================================================

					// Validar duplicados exactos en el archivo (Llave Compuesta)
					//string llaveUnica = $"{claveMun}-{tipoPre}-{cuentaPredial}";
					string llaveUnica = $"{claveMun}-{tipoPre}-{cuentaPredial}-{bimestre}";
					if (cuentasProcesadas.Contains(llaveUnica))
					{
						//MarcarError(resultado, numeroLinea, $"El predio con Cuenta {cuentaPredial} y Tipo {tipoPre} viene duplicado en el archivo.");
						MarcarError(resultado, numeroLinea, $"El predio con Cuenta {cuentaPredial} tiene el Bimestre {bimestre} duplicado en el archivo.");
						continue;
					}
					cuentasProcesadas.Add(llaveUnica);

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

					if (!Utilerias.TryParseFecha(col[23], out DateTime fechaVigencia))
					{
						MarcarError(resultado, numeroLinea, "Fecha de vigencia inválida.");
						continue;
					}

					// 3. Se agregan los datos limpios al lote (Todo va como string hacia Staging)
					tablaLote.Rows.Add(
						claveMun.ToString(),                  // 1. Ya validado
						tipoPre.ToString(),                   // 2. Ya validado
						cuentaPredial,                        // 3. Cuenta Predial (Conserva ceros)
						Utilerias.LimpiarCadena(col[3], 50),  // 4. Folio único
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
						clasePago.ToString(),                 // 21. Ya validado
						bimestre.ToString(),                  // 22. Ya validado
						impuestoPagar,                        // 23. Impuesto Determinado
						fechaVigencia,                        // 24. Fecha Vigencia
						param.FolioCarga                      // Columna de Control
					);

					resultado.RegistrosExitosos++;

					// 4. Inserción masiva si se alcanza el tamaño del lote
					if (tablaLote.Rows.Count >= 10000)
					{
						InsertarLoteEnBD(tablaLote, param);
						tablaLote.Clear();
					}
				}

				if (tablaLote.Rows.Count > 0) InsertarLoteEnBD(tablaLote, param);
			}

			LogService.WriteLogAsync(AppName, "INFO", param.UsuarioLogin, "ProcesadorPadronTXT",
				$"Fin. Éxitos: {resultado.RegistrosExitosos}, Errores: {resultado.ErroresDetalle.Count}");

			return resultado;
		}

		private void MarcarError(ResultadoProceso res, int linea, string msg)
		{
			res.RegistrosFallidos++;
			res.ErroresDetalle.Add($"Línea {linea}: {msg}");
		}

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
			tabla.Columns.Add("FolioCarga", typeof(string));
			return tabla;
		}

		private void InsertarLoteEnBD(DataTable lote, ParametrosCarga param)
		{
			try
			{
				using (SqlConnection conn = new SqlConnection(CadenaConexion))
				{
					conn.Open();

					// PASO 1: Inyectar masivamente a la tabla Staging (Este se mantiene con BulkCopy por rendimiento)
					using (SqlBulkCopy bulkCopy = new SqlBulkCopy(conn))
					{
						bulkCopy.DestinationTableName = "dbo.Staging_Predial";
						bulkCopy.BatchSize = 10000;
						bulkCopy.BulkCopyTimeout = 120;
						bulkCopy.WriteToServer(lote);
					}

					// PASO 2: Llamada al Procedimiento Almacenado que hace el MERGE
					using (SqlCommand cmd = new SqlCommand("dbo.sp_ProcesarMergePadron", conn))
					{
						cmd.CommandType = CommandType.StoredProcedure;
						cmd.CommandTimeout = 180; // 3 minutos máximo para procesar millones de registros
						cmd.Parameters.AddWithValue("@FolioCarga", param.FolioCarga);

						cmd.ExecuteNonQuery();
					}
				}
			}
			catch (SqlException ex)
			{
				LogService.WriteLogAsync(AppName, "ERROR", param.UsuarioLogin, "SqlBulkCopy/SP", ex.Message).Wait();
				throw;
			}
		}
	}
}