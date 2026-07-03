using ExcelDataReader; // 🚀 Requiere el paquete NuGet: ExcelDataReader
using swCargaMasivaIngresos.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.IO;

namespace swCargaMasivaIngresos.Services
{
	public class ProcesadorPagosExcel : IProcesadorFormato
	{
		private readonly string AppName = System.Configuration.ConfigurationManager.AppSettings["NombAplicacion"] ?? "APICargaMasivaIngresos";
		private readonly string CadenaConexion = ConfiguracionApp.ObtenerCadenaConexion();

		public ResultadoProceso Procesar(string rutaArchivo, ParametrosCarga param)
		{
			var resultado = new ResultadoProceso { ErroresDetalle = new List<string>() };
			DataTable tablaLote = CrearEstructuraPagos();
			HashSet<string> pagosProcesados = new HashSet<string>();

			LogService.WriteLogAsync("INFO", param.UsuarioLogin, "ProcesadorPagosExcel", $"Inicia lectura de archivo Excel de Pagos. Folio: {param.FolioCarga}");

			using (var stream = File.Open(rutaArchivo, FileMode.Open, FileAccess.Read))
			using (var reader = ExcelReaderFactory.CreateReader(stream))
			{
				int numeroLinea = 0;

				// 💡 Opcional: Si tus usuarios van a subir el Excel con una fila de encabezados, 
				// descomenta la siguiente línea para que se salte la primera fila:
				// reader.Read(); numeroLinea++;

				while (reader.Read())
				{
					numeroLinea++;

					// 1. Validar que el Excel tenga las 24 columnas
					if (reader.FieldCount < 24)
					{
						MarcarError(resultado, numeroLinea, $"Columnas incorrectas en Excel. Se esperaban al menos 24.");
						continue;
					}

					// 2. Extraer datos directamente por su índice (0 a 23)
					// Utilizamos GetValue().ToString() para evitar errores si la celda es numérica o nula
					string claveMunicipio = reader.GetValue(0)?.ToString().Trim();
					string tipoPredio = reader.GetValue(1)?.ToString().Trim();
					string cuentaPredial = reader.GetValue(2)?.ToString().Trim();
					string strBimestre = reader.GetValue(21)?.ToString().Trim();

					// 3. Validaciones de negocio (Las mismas que el TXT)
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
					if (!byte.TryParse(strBimestre, out byte bimestre) || bimestre > 6)
					{
						MarcarError(resultado, numeroLinea, "Bimestre inválido (Debe ser un número del 0 al 6).");
						continue;
					}

					// 4. Lógica del HashSet para duplicados
					string llaveUnica = $"{claveMun}-{tipoPre}-{cuentaPredial}-{bimestre}";
					if (pagosProcesados.Contains(llaveUnica)) continue;

					pagosProcesados.Add(llaveUnica);

					// 5. Agregar a memoria
					tablaLote.Rows.Add(
						claveMun.ToString(),
						tipoPre.ToString(),
						cuentaPredial,
						param.FolioCarga, // 🚀 Ya viene como int desde tu parámetro
						bimestre.ToString()
					);

					resultado.RegistrosExitosos++;

					// 6. Inyección masiva
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
			res.ErroresDetalle.Add($"Fila {linea}: {msg}");
		}

		private DataTable CrearEstructuraPagos()
		{
			var tabla = new DataTable();
			tabla.Columns.Add("ClaveMunicipio", typeof(string));
			tabla.Columns.Add("TipoPredio", typeof(string));
			tabla.Columns.Add("CuentaPredial", typeof(string));
			tabla.Columns.Add("FolioCarga", typeof(int)); // 🚀 Con el int
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