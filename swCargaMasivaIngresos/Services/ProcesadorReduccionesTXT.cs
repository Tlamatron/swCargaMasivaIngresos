using swCargaMasivaIngresos.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.Text;

namespace swCargaMasivaIngresos.Services
{
	public class ProcesadorReduccionesTXT : IProcesadorFormato
	{
		private readonly string AppName = System.Configuration.ConfigurationManager.AppSettings["NombAplicacion"] ?? "APICargaMasivaIngresos";
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

				while ((linea = reader.ReadLine()) != null)
				{
					numeroLinea++;
					if (string.IsNullOrWhiteSpace(linea)) continue;

					string[] col = linea.Split('|');

					// 1. Validar Layout estricto de 5 columnas
					if (col.Length != 5)
					{
						MarcarError(resultado, numeroLinea, $"Columnas incorrectas. Se esperaban 5, llegaron {col.Length}.");
						continue;
					}

					// 2. Extraer y limpiar
					string claveMunicipio = col[0].Trim();
					string tipoPredio = col[1].Trim();
					string cuentaPredial = col[2].Trim();
					string folioUnico = col[3].Trim();
					string tipoReduccion = col[4].Trim();

					// 3. VALIDACIONES ESTRICTAS DE NEGOCIO
					if (string.IsNullOrEmpty(cuentaPredial))
					{
						MarcarError(resultado, numeroLinea, "La Cuenta Predial es obligatoria.");
						continue;
					}

					if (!short.TryParse(claveMunicipio, out short claveMun) || claveMun < 1 || claveMun > 217)
					{
						MarcarError(resultado, numeroLinea, "Clave de municipio inválida (1 a 217).");
						continue;
					}

					if (!byte.TryParse(tipoPredio, out byte tipoPre) || tipoPre < 1 || tipoPre > 3)
					{
						MarcarError(resultado, numeroLinea, "Tipo de predio inválido (1=Urbano, 2=Rústico, 3=Suburbano).");
						continue;
					}

					if (!byte.TryParse(tipoReduccion, out byte tipoRed) || tipoRed < 1)
					{
						MarcarError(resultado, numeroLinea, "Tipo de Reducción inválido. Debe ser un número de catálogo válido.");
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