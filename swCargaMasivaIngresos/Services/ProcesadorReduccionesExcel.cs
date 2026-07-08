using ExcelDataReader;
using swCargaMasivaIngresos.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.IO;

namespace swCargaMasivaIngresos.Services
{
	/// <summary>
	/// Clase ProcesadorReduccionesExcel que implementa la interfaz IProcesadorFormato. Este procesador se encarga de leer archivos Excel que contienen datos de reducciones, mapearlos, limpiarlos y validarlos, y finalmente insertar los registros válidos en la base de datos mediante una operación de inserción masiva.
	/// </summary>
	public class ProcesadorReduccionesExcel : IProcesadorFormato
	{
		private readonly string CadenaConexion = ConfiguracionApp.ObtenerCadenaConexion();

		/// <summary>
		/// Método principal que procesa un archivo Excel de reducciones. Lee el archivo, valida los datos, y realiza la inserción masiva en la base de datos.
		/// </summary>
		/// <param name="rutaArchivo"></param>
		/// <param name="param"></param>
		/// <returns></returns>
		public ResultadoProceso Procesar(string rutaArchivo, ParametrosCarga param)
		{
			var resultado = new ResultadoProceso { ErroresDetalle = new List<string>() };
			DataTable tablaLote = CrearEstructuraReducciones();
			HashSet<string> reduccionesProcesadas = new HashSet<string>();

			LogService.WriteLogAsync("WARN", param.UsuarioLogin, "ProcesadorReduccionesExcel", $"Inicia lectura de archivo Excel de Descuentos. Folio: {param.FolioCarga}");

			using (var stream = File.Open(rutaArchivo, FileMode.Open, FileAccess.Read))
			using (var reader = ExcelReaderFactory.CreateReader(stream))
			{
				int numeroLinea = 0;

				// 💡 Si el Excel trae encabezados, descomenta esta línea para saltar la fila 1:
				// reader.Read(); numeroLinea++;

				while (reader.Read())
				{
					numeroLinea++;

					// 1. Validar Layout estricto de 5 columnas
					if (reader.FieldCount < 5)
					{
						MarcarError(resultado, numeroLinea, $"Columnas incorrectas. Se esperaban al menos 5.");
						continue;
					}

					// 2. Extraer valores
					string claveMunicipio = reader.GetValue(0)?.ToString().Trim();
					string tipoPredio = reader.GetValue(1)?.ToString().Trim();
					string cuentaPredial = reader.GetValue(2)?.ToString().Trim();
					string folioUnico = reader.GetValue(3)?.ToString().Trim();
					string tipoReduccion = reader.GetValue(4)?.ToString().Trim();

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

					// 4. Evitar duplicados
					string llaveUnica = $"{claveMun}-{tipoPre}-{cuentaPredial}-{tipoRed}";
					if (reduccionesProcesadas.Contains(llaveUnica))
					{
						MarcarError(resultado, numeroLinea, $"La cuenta {cuentaPredial} ya tiene asignado el descuento {tipoRed} en este archivo.");
						continue;
					}
					reduccionesProcesadas.Add(llaveUnica);

					// 5. Agregar a la tabla
					tablaLote.Rows.Add(
						claveMun.ToString(),
						tipoPre.ToString(),
						cuentaPredial,
						Utilerias.LimpiarCadena(folioUnico, 50),
						tipoRed.ToString(),
						param.FolioCarga // 🚀 Ya es INT
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
		/// Marca un error en el resultado del proceso, incrementando el contador de registros fallidos y agregando un mensaje detallado del error.
		/// </summary>
		/// <param name="res"></param>
		/// <param name="linea"></param>
		/// <param name="msg"></param>
		private void MarcarError(ResultadoProceso res, int linea, string msg)
		{
			res.RegistrosFallidos++;
			res.ErroresDetalle.Add($"Fila {linea}: {msg}");
		}

		/// <summary>
		/// Método privado que crea la estructura de un DataTable para almacenar temporalmente los datos de reducciones antes de ser insertados en la base de datos. Define las columnas necesarias y sus tipos de datos.
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
			tabla.Columns.Add("FolioCarga", typeof(int)); // 🚀 Columna actualizada a INT
			return tabla;
		}

		/// <summary>
		/// Método privado que realiza la inserción masiva de un lote de datos en la base de datos utilizando SqlBulkCopy. Después de insertar los datos, llama a un procedimiento almacenado para procesar el merge de reducciones.
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
					bulkCopy.DestinationTableName = "pred_Operacion.Staging_Reducciones";
					bulkCopy.WriteToServer(lote);
				}

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