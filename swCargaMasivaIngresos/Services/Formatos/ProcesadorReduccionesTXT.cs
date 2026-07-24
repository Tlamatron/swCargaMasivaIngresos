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
	/// Clase encargada de procesar archivos de texto plano (TXT) que contienen información de reducciones. 
	/// Implementa la interfaz IProcesadorFormato de forma asíncrona.
	/// </summary>
	public class ProcesadorReduccionesTXT : IProcesadorFormato
	{
		private readonly string CadenaConexion = ConfiguracionApp.ObtenerCadenaConexion();

		/// <summary>
		/// Método principal asíncrono que procesa el archivo TXT de reducciones, validando el layout y ejecutando la carga masiva.
		/// </summary>
		public async Task<ResultadoProceso> ProcesarAsync(string rutaArchivo, ParametrosCarga param)
		{
			var resultado = new ResultadoProceso { ErroresDetalle = new List<string>() };
			DataTable tablaLote = CrearEstructuraReducciones();

			LogService.WriteLogAsync("INFO", param.UsuarioLogin, "ProcesadorReduccionesTXT", $"Inicia lectura de archivo de Descuentos. Folio: {param.FolioCarga}").Wait();

			using (var reader = new StreamReader(rutaArchivo, Encoding.UTF8))
			{
				string linea;
				int numeroLinea = 0;

				int idxMpio = 0, idxTipo = 1, idxCuenta = 2, idxFolio = 3, idxReduccion = 4;
				bool esPrimerRenglon = true;
				bool tieneEncabezados = false;

				while ((linea = await reader.ReadLineAsync()) != null)
				{
					numeroLinea++;
					if (string.IsNullOrWhiteSpace(linea)) continue;

					string[] col = linea.Split('|');

					// =================================================================
					// 🚀 LÓGICA HÍBRIDA (Detección de Encabezados)
					// =================================================================
					if (esPrimerRenglon)
					{
						esPrimerRenglon = false;

						// Evaluamos si es un encabezado
						if (linea.ToUpper().Contains("MUNICIPIO") || linea.ToUpper().Contains("CLAVE") || linea.ToUpper().Contains("CUENTA"))
						{
							tieneEncabezados = true;
							idxFolio = -1;
							idxReduccion = -1;

							for (int i = 0; i < col.Length; i++)
							{
								string header = col[i].ToUpper();
								if (header.Contains("MUNICIPIO") || header.Contains("CLAVE")) idxMpio = i;
								else if (header.Contains("PREDIO")) idxTipo = i;
								else if (header.Contains("CUENTA")) idxCuenta = i;
								else if (header.Contains("FOLIO")) idxFolio = i;
								else if (header.Contains("REDUCCION") || header.Contains("REDUCCIÓN") || header.Contains("TIPO RED")) idxReduccion = i;
							}
							continue;
						}
						else
						{
							// MODO ESTRICTO (No hay encabezados)
							if (col.Length == 4)
							{
								idxFolio = -1;
								idxReduccion = 3;
							}
							else if (col.Length < 4)
							{
								MarcarError(resultado, numeroLinea, "El archivo sin encabezados no cumple con el layout mínimo estricto de 4 columnas.");
								continue;
							}
						}
					}

					// =================================================================
					// 1. VALIDACIÓN DE MAPEO
					// =================================================================
					if (tieneEncabezados && (idxMpio == -1 || idxTipo == -1 || idxCuenta == -1 || idxReduccion == -1))
					{
						MarcarError(resultado, numeroLinea, "El archivo tiene encabezados, pero no se encontraron las columnas obligatorias (Municipio, Predio, Cuenta, Reducción).");
						continue;
					}

					int maxIndexRequired = Math.Max(idxMpio, Math.Max(idxTipo, Math.Max(idxCuenta, idxReduccion)));
					if (col.Length <= maxIndexRequired)
					{
						MarcarError(resultado, numeroLinea, "La fila no contiene los datos suficientes según la estructura mapeada.");
						continue;
					}

					// =================================================================
					// 2. EXTRACCIÓN SEGURA
					// =================================================================
					string claveMunicipio = col[idxMpio].Trim();
					string tipoPredio = col[idxTipo].Trim();
					string cuentaPredial = col[idxCuenta].Trim();
					string tipoReduccion = col[idxReduccion].Trim();

					string folioUnico = "";
					if (idxFolio != -1 && col.Length > idxFolio)
					{
						folioUnico = col[idxFolio].Trim();
					}

					// =================================================================
					// 3. VALIDACIONES ESTRICTAS DE NEGOCIO
					// =================================================================
					if (string.IsNullOrEmpty(cuentaPredial))
					{
						MarcarError(resultado, numeroLinea, "La Cuenta Predial es obligatoria.");
						continue;
					}

					// 🚀 FALLBACK SEGURO DE MUNICIPIO
					if (!short.TryParse(claveMunicipio, out short claveMun) || claveMun < 1 || claveMun > 217)
					{
						if (param.ClaveMunicipioDestino > 0)
						{
							claveMun = (short)param.ClaveMunicipioDestino; // Cast explícito
						}
						else
						{
							MarcarError(resultado, numeroLinea, $"Clave de municipio '{claveMunicipio}' inválida.");
							continue;
						}
					}

					if (!byte.TryParse(tipoPredio, out byte tipoPre) || tipoPre < 1 || tipoPre > 3)
					{
						MarcarError(resultado, numeroLinea, "Tipo de predio inválido (1=Urbano, 2=Rústico, 3=Suburbano).");
						continue;
					}

					if (!byte.TryParse(tipoReduccion, out byte tipoRed) || tipoRed < 1)
					{
						MarcarError(resultado, numeroLinea, "Tipo de Reducción inválido.");
						continue;
					}

					// 5. Agregar a la tabla en memoria
					tablaLote.Rows.Add(
						claveMun.ToString(),
						tipoPre.ToString(),
						cuentaPredial,
						Utilerias.LimpiarCadena(folioUnico, 50),
						tipoRed.ToString(),
						param.FolioCarga
					);

					resultado.RegistrosExitosos++;

					if (tablaLote.Rows.Count >= 10000)
					{
						List<string> erroresLogicos = await InsertarLoteEnBDAsync(tablaLote, param);
						if (erroresLogicos.Count > 0)
						{
							resultado.ErroresDetalle.AddRange(erroresLogicos);
							resultado.RegistrosFallidos += erroresLogicos.Count;
							resultado.RegistrosExitosos -= erroresLogicos.Count; // Restamos los falsos éxitos
						}
						tablaLote.Clear();
					}
				}

				if (tablaLote.Rows.Count > 0)
				{
					List<string> erroresLogicos = await InsertarLoteEnBDAsync(tablaLote, param);
					if (erroresLogicos.Count > 0)
					{
						resultado.ErroresDetalle.AddRange(erroresLogicos);
						resultado.RegistrosFallidos += erroresLogicos.Count;
						resultado.RegistrosExitosos -= erroresLogicos.Count; // Restamos los falsos éxitos
					}
					tablaLote.Clear();
				}
			}

			return resultado;
		}

		/// <summary>
		/// Método privado que marca un error en el resultado del proceso, incrementando el contador de registros fallidos y agregando un mensaje de error detallado con la línea correspondiente.
		/// </summary>
		/// <param name="res"></param>
		/// <param name="linea"></param>
		/// <param name="msg"></param>
		private void MarcarError(ResultadoProceso res, int linea, string msg)
		{
			res.RegistrosFallidos++;
			res.ErroresDetalle.Add($"Línea {linea}: {msg}");
		}

		/// <summary>
		/// Método privado que crea y devuelve la estructura de DataTable para almacenar temporalmente los registros de reducciones antes de insertarlos en la base de datos. La tabla contiene las columnas necesarias para representar cada registro de reducción, incluyendo ClaveMunicipio, TipoPredio, CuentaPredial, FolioUnico, TipoReduccion y FolioCarga.
		/// </summary>
		/// <returns></returns>
		private DataTable CrearEstructuraReducciones()
		{
			var tabla = new DataTable();
			tabla.Columns.Add("ClaveMunicipio", typeof(string));
			tabla.Columns.Add("TipoPredio", typeof(string));
			tabla.Columns.Add("CuentaPredial", typeof(string));
			tabla.Columns.Add("FolioUnico", typeof(string));
			tabla.Columns.Add("TipoReduccion", typeof(string));
			tabla.Columns.Add("FolioCarga", typeof(int));
			return tabla;
		}

		/// <summary>
		/// Método privado asíncrono para ejecutar la ingesta masiva de reducciones.
		/// Nota: La consolidación lógica queda comentada hasta que se defina la regla de negocio.
		/// </summary>
		private async Task<List<string>> InsertarLoteEnBDAsync(DataTable lote, ParametrosCarga param)
		{
			var errores = new List<string>();
			try
			{
				using (SqlConnection conn = new SqlConnection(CadenaConexion))
				{
					await conn.OpenAsync();

					using (SqlBulkCopy bulkCopy = new SqlBulkCopy(conn))
					{
						bulkCopy.DestinationTableName = "pred_Operacion.Staging_Reducciones";
						bulkCopy.BatchSize = 10000;
						bulkCopy.BulkCopyTimeout = 120;

						await bulkCopy.WriteToServerAsync(lote);
					}

					using (SqlCommand cmd = new SqlCommand("pred_Operacion.sp_ProcesarMergeReducciones", conn))
					{
						cmd.CommandType = CommandType.StoredProcedure;
						cmd.CommandTimeout = 180;
						cmd.Parameters.AddWithValue("@FolioCarga", param.FolioCarga);

						//await cmd.ExecuteNonQueryAsync();
						using (var reader = await cmd.ExecuteReaderAsync())
						{
							while (await reader.ReadAsync())
							{
								errores.Add($"[Reducciones TXT] Cuenta {reader["CuentaPredial"]}: {reader["MensajeError"]}");
							}
						}
					}
				}
			}
			catch (SqlException ex)
			{
				LogService.WriteLogAsync("ERROR", param.UsuarioLogin, "SqlBulkCopy/SP Reducciones TXT", ex.Message).Wait();
				throw;
			}
			return errores;
		}
	}
}