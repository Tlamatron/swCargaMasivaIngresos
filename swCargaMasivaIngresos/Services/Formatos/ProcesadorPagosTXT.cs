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
	/// Clase ProcesadorPagosTXT que implementa la interfaz IProcesadorFormato. Este procesador se encarga de leer archivos de texto (TXT) que contienen datos de pagos, mapearlos, limpiarlos y validarlos, y finalmente insertar los registros válidos en la base de datos mediante una operación de inserción masiva.
	/// </summary>
	public class ProcesadorPagosTXT : IProcesadorFormato
	{
		private readonly string CadenaConexion = ConfiguracionApp.ObtenerCadenaConexion();

		/// <summary>
		/// Método principal que procesa un archivo de pagos en formato TXT. Lee el archivo línea por línea, valida cada registro, y si es válido, lo agrega a una tabla en memoria. Una vez que se alcanza un cierto número de registros o al finalizar la lectura del archivo, se realiza una inserción masiva en la base de datos.
		/// </summary>
		/// <param name="rutaArchivo"></param>
		/// <param name="param"></param>
		/// <returns></returns>
		public ResultadoProceso Procesar(string rutaArchivo, ParametrosCarga param)
		{
			var resultado = new ResultadoProceso { ErroresDetalle = new List<string>() };
			DataTable tablaLote = CrearEstructuraPagos();
			HashSet<string> pagosProcesados = new HashSet<string>();

			LogService.WriteLogAsync("INFO", param.UsuarioLogin, "ProcesadorPagosTXT", $"Inicia lectura de archivo de Pagos Locales (Etiquetado). Folio: {param.FolioCarga}");

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
					string strBimestre = col[21].Trim();
					string clasePago = col[20].Trim(); 
					string impuestoDeterminado = col[22].Trim();
					//string descuento = col[23].Trim(); 

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
						bimestre.ToString(),
						string.IsNullOrWhiteSpace(clasePago) ? DBNull.Value : (object)clasePago,
						string.IsNullOrWhiteSpace(impuestoDeterminado) ? DBNull.Value : (object)impuestoDeterminado
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

		/// <summary>
		/// Marca un error en el resultado del proceso, incrementando el contador de registros fallidos y agregando un mensaje de error detallado.
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
		/// Método privado que crea la estructura de un DataTable para almacenar temporalmente los datos de pagos antes de ser insertados en la base de datos. Define las columnas necesarias y sus tipos de datos.
		/// </summary>
		/// <returns></returns>
		private DataTable CrearEstructuraPagos()
		{
			var tabla = new DataTable();
			tabla.Columns.Add("ClaveMunicipio", typeof(string));
			tabla.Columns.Add("TipoPredio", typeof(string));
			tabla.Columns.Add("CuentaPredial", typeof(string));
			tabla.Columns.Add("FolioCarga", typeof(int));
			tabla.Columns.Add("Bimestre", typeof(string));

			tabla.Columns.Add("ClasePago", typeof(string));
			tabla.Columns.Add("ImpuestoDeterminado", typeof(string));
			return tabla;
		}

		/// <summary>
		/// Método privado que realiza la inserción masiva de un lote de registros en la base de datos utilizando SqlBulkCopy. Después de insertar los registros, llama a un procedimiento almacenado para procesar los datos insertados.
		/// </summary>
		/// <param name="lote"></param>
		/// <param name="param"></param>
		private void InsertarLoteEnBD(DataTable lote, ParametrosCarga param)
		{
			using (SqlConnection conn = new SqlConnection(CadenaConexion))
			{
				conn.Open();

				using (SqlBulkCopy bulkCopy = new SqlBulkCopy(conn))
				{
					// OJO: Asegúrate de que la tabla 'Staging_Etiquetado' tenga estas columnas nuevas en SQL Server
					bulkCopy.DestinationTableName = "pred_Operacion.Staging_Etiquetado";
					bulkCopy.BatchSize = 10000;  // Vital para alta demanda
					bulkCopy.BulkCopyTimeout = 120; // Previene timeouts por bloqueos

					// 🚀 MAPEO EXPLÍCITO (Blindaje contra desorden de columnas en BD)
					bulkCopy.ColumnMappings.Add("ClaveMunicipio", "ClaveMunicipio");
					bulkCopy.ColumnMappings.Add("TipoPredio", "TipoPredio");
					bulkCopy.ColumnMappings.Add("CuentaPredial", "CuentaPredial");
					bulkCopy.ColumnMappings.Add("FolioCarga", "FolioCarga");
					bulkCopy.ColumnMappings.Add("Bimestre", "Bimestre");
					bulkCopy.ColumnMappings.Add("ClasePago", "ClasePago");
					bulkCopy.ColumnMappings.Add("ImpuestoDeterminado", "ImpuestoDeterminado");
					//bulkCopy.ColumnMappings.Add("Descuento", "Descuento"); // Usa el nombre exacto de tu BD

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