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
	/// Clase encargada de procesar archivos de texto plano (TXT) que contienen información de reducciones. Implementa la interfaz IProcesadorFormato y se encarga de validar el layout del archivo, realizar validaciones de negocio, almacenar temporalmente los registros en una tabla en memoria (DataTable) y finalmente insertar los datos en la base de datos utilizando SqlBulkCopy y un procedimiento almacenado para consolidar la información.
	/// </summary>
	public class ProcesadorReduccionesTXT : IProcesadorFormato
	{
		private readonly string CadenaConexion = ConfiguracionApp.ObtenerCadenaConexion();

		public ResultadoProceso Procesar(string rutaArchivo, ParametrosCarga param)
		{
			var resultado = new ResultadoProceso { ErroresDetalle = new List<string>() };
			DataTable tablaLote = CrearEstructuraReducciones();
			HashSet<string> reduccionesProcesadas = new HashSet<string>();

			LogService.WriteLogAsync("INFO", param.UsuarioLogin, "ProcesadorReduccionesTXT", $"Inicia lectura de archivo de Descuentos. Folio: {param.FolioCarga}");

			using (var reader = new StreamReader(rutaArchivo, Encoding.UTF8))
			{
				string linea;
				int numeroLinea = 0;

				int idxMpio = 0, idxTipo = 1, idxCuenta = 2, idxFolio = 3, idxReduccion = 4;
				bool esPrimerRenglon = true;
				bool tieneEncabezados = false;

				while ((linea = reader.ReadLine()) != null)
				{
					numeroLinea++;
					if (string.IsNullOrWhiteSpace(linea)) continue;

					string[] col = linea.Split('|');

					// =================================================================
					// 🚀 LÓGICA HÍBRIDA (PROPUESTA DEL USUARIO)
					// =================================================================
					if (esPrimerRenglon)
					{
						esPrimerRenglon = false;

						// Evaluamos si es un encabezado (contiene texto descriptivo)
						if (linea.ToUpper().Contains("MUNICIPIO") || linea.ToUpper().Contains("CLAVE") || linea.ToUpper().Contains("CUENTA"))
						{
							tieneEncabezados = true;
							idxFolio = -1; // Lo apagamos hasta confirmar que existe
							idxReduccion = -1; // Lo apagamos hasta confirmar que existe

							for (int i = 0; i < col.Length; i++)
							{
								string header = col[i].ToUpper();
								if (header.Contains("MUNICIPIO") || header.Contains("CLAVE")) idxMpio = i;
								else if (header.Contains("PREDIO")) idxTipo = i;
								else if (header.Contains("CUENTA")) idxCuenta = i;
								else if (header.Contains("FOLIO")) idxFolio = i;
								else if (header.Contains("REDUCCION") || header.Contains("REDUCCIÓN") || header.Contains("TIPO RED")) idxReduccion = i;
							}
							continue; // Saltamos esta línea porque solo eran títulos
						}
						else
						{
							// MODO ESTRICTO (No hay encabezados)
							if (col.Length == 4)
							{
								idxFolio = -1; // Omitieron el folio opcional
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

					// Validar que la fila actual tenga la longitud suficiente según nuestro mapeo
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

					// Extracción opcional del Folio
					string folioUnico = "";
					if (idxFolio != -1 && col.Length > idxFolio)
					{
						folioUnico = col[idxFolio].Trim();
					}

					// =================================================================
					// 3. VALIDACIONES ESTRICTAS DE NEGOCIO (Igual que antes)
					// =================================================================
					if (string.IsNullOrEmpty(cuentaPredial))
					{
						MarcarError(resultado, numeroLinea, "La Cuenta Predial es obligatoria.");
						continue;
					}

					if (!short.TryParse(claveMunicipio, out short claveMun) || claveMun < 1 || claveMun > 217)
					{
						MarcarError(resultado, numeroLinea, $"Clave de municipio '{claveMunicipio}' inválida.");
						continue;
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

					// 4. Evitar filas duplicadas idénticas en el mismo TXT
					string llaveUnica = $"{claveMun}-{tipoPre}-{cuentaPredial}-{tipoRed}";
					if (reduccionesProcesadas.Contains(llaveUnica))
					{
						MarcarError(resultado, numeroLinea, $"La cuenta {cuentaPredial} ya tiene asignado el descuento {tipoRed} en este archivo.");
						continue;
					}
					reduccionesProcesadas.Add(llaveUnica);

					// 5. Agregar a la tabla en memoria (Staging)
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
						InsertarLoteEnBD(tablaLote, param);
						tablaLote.Clear();
					}
				}

				if (tablaLote.Rows.Count > 0) InsertarLoteEnBD(tablaLote, param);
			}

			return resultado;
		}

		private void MarcarError(ResultadoProceso res, int linea, string msg)
		{
			res.RegistrosFallidos++;
			res.ErroresDetalle.Add($"Línea {linea}: {msg}");
		}

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

		private void InsertarLoteEnBD(DataTable lote, ParametrosCarga param)
		{
			using (SqlConnection conn = new SqlConnection(CadenaConexion))
			{
				conn.Open();

				using (SqlBulkCopy bulkCopy = new SqlBulkCopy(conn))
				{
					bulkCopy.DestinationTableName = "pred_Operacion.Staging_Reducciones";
					bulkCopy.WriteToServer(lote);
				}

				// Llamamos al SP que mueve de Staging a la tabla relacional final
				using (SqlCommand cmd = new SqlCommand("pred_Operacion.sp_ProcesarMergeReducciones", conn))
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