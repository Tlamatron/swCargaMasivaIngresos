using swCargaMasivaIngresos.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.Text;

namespace swCargaMasivaIngresos.Services
{
	public class ProcesadorPagosTXT : IProcesadorFormato
	{
		private readonly string AppName = System.Configuration.ConfigurationManager.AppSettings["NombAplicacion"] ?? "APICargaMasivaIngresos";
		private readonly string CadenaConexion = ConfiguracionApp.ObtenerCadenaConexion();

		public ResultadoProceso Procesar(string rutaArchivo, ParametrosCarga param)
		{
			var resultado = new ResultadoProceso { ErroresDetalle = new List<string>() };
			DataTable tablaLote = CrearEstructuraPagos();
			HashSet<string> pagosProcesados = new HashSet<string>();

			LogService.WriteLogAsync(AppName, "INFO", param.UsuarioLogin, "ProcesadorPagosTXT", $"Inicia lectura de archivo de Pagos Locales (Etiquetado). Folio: {param.FolioCarga}");

			using (var reader = new StreamReader(rutaArchivo, Encoding.UTF8))
			{
				string linea;
				int numeroLinea = 0;

				while ((linea = reader.ReadLine()) != null)
				{
					numeroLinea++;
					if (string.IsNullOrWhiteSpace(linea)) continue;

					string[] col = linea.Split('|');

					// 1. Validar que traiga el layout de 24 columnas
					if (col.Length != 24)
					{
						MarcarError(resultado, numeroLinea, $"Columnas incorrectas. Se esperaban 24, llegaron {col.Length}.");
						continue;
					}

					// 2. Extraemos SOLAMENTE las llaves (El resto de columnas se ignoran para optimizar)
					string claveMunicipio = col[0].Trim();
					string tipoPredio = col[1].Trim();
					string cuentaPredial = col[2].Trim();
					string strBimestre = col[21].Trim(); // 🚀 EXTRAEMOS EL BIMESTRE

					// 3. Validaciones
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

					// 4. Evitar procesar el mismo pago dos veces (¡Agregamos el Bimestre a la llave!)
					string llaveUnica = $"{claveMun}-{tipoPre}-{cuentaPredial}-{bimestre}";
					if (pagosProcesados.Contains(llaveUnica))
					{
						continue;
					}
					pagosProcesados.Add(llaveUnica);

					// 5. Agregar a la tabla en memoria
					tablaLote.Rows.Add(
						claveMun.ToString(),
						tipoPre.ToString(),
						cuentaPredial,
						param.FolioCarga,
						bimestre.ToString() // 🚀 LO INYECTAMOS EN STAGING
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
	}
}