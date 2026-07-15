using ExcelDataReader;
using swCargaMasivaIngresos.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.Linq;

namespace swCargaMasivaIngresos.Services
{
	public class ProcesadorReduccionesExcel : IProcesadorFormato
	{
		private readonly string CadenaConexion = ConfiguracionApp.ObtenerCadenaConexion();

		public ResultadoProceso Procesar(string rutaArchivo, ParametrosCarga param)
		{
			var resultadoFinal = new ResultadoProceso { ErroresDetalle = new List<string>() };

			LogService.WriteLogAsync("INFO", param.UsuarioLogin, "ProcesadorReduccionesExcel", $"Iniciando Lectura Inteligente de Descuentos. Folio: {param.FolioCarga}").Wait();

			try
			{
				using (var stream = File.Open(rutaArchivo, FileMode.Open, FileAccess.Read))
				using (var reader = ExcelReaderFactory.CreateReader(stream))
				{
					var dataSet = reader.AsDataSet(new ExcelDataSetConfiguration
					{
						ConfigureDataTable = _ => new ExcelDataTableConfiguration { UseHeaderRow = false }
					});

					foreach (DataTable tablaExcel in dataSet.Tables)
					{
						int filaInicioDatos;
						var mapaCrudo = MapeadorInteligente.ObtenerMapaPorRegiones(tablaExcel, out filaInicioDatos);

						if (mapaCrudo.Count == 0) continue;

						var mapaBloqueado = MapeadorInteligente.ProcesarEncabezadosConMemoria(mapaCrudo);
						DataTable tablaCrudos = CrearEstructuraReducciones();

						//HashSet<string> reduccionesProcesadas = new HashSet<string>();

						for (int i = filaInicioDatos; i < tablaExcel.Rows.Count; i++)
						{
							var fila = tablaExcel.Rows[i];
							if (string.IsNullOrWhiteSpace(string.Join("", fila.ItemArray))) continue;

							// 🚀 EXTRACCIÓN SEGURA (No importa el orden ni si faltan columnas como Folio Único)
							string cuentaPredial = ExtraerSeguro(fila, mapaBloqueado, "CuentaPredial", "");
							if (string.IsNullOrWhiteSpace(cuentaPredial) || cuentaPredial.Equals("Cuenta", StringComparison.OrdinalIgnoreCase)) continue;
							if (cuentaPredial.EndsWith(".0")) cuentaPredial = cuentaPredial.Replace(".0", "");

							string tipoPredio = ExtraerSeguro(fila, mapaBloqueado, "TipoPredio", "").ToUpper().Trim();
							if (tipoPredio == "U" || tipoPredio.StartsWith("URBANO")) tipoPredio = "1";
							else if (tipoPredio == "R" || tipoPredio.StartsWith("RUSTICO") || tipoPredio.StartsWith("RÚSTICO")) tipoPredio = "2";
							else if (tipoPredio == "S" || tipoPredio.StartsWith("SUBURBANO") || tipoPredio.StartsWith("SUB")) tipoPredio = "3";
							if (string.IsNullOrWhiteSpace(tipoPredio)) tipoPredio = "1";

							string claveMunicipio = ExtraerSeguro(fila, mapaBloqueado, "ClaveMunicipio", "");
							if (string.IsNullOrWhiteSpace(claveMunicipio) && param != null)
							{
								claveMunicipio = param.ClaveMunicipioDestino > 0 ? param.ClaveMunicipioDestino.ToString() : param.OficinaId.ToString();
							}

							string folioUnico = ExtraerSeguro(fila, mapaBloqueado, "FolioUnico", "");
							string tipoReduccion = ExtraerSeguro(fila, mapaBloqueado, "TipoReduccion", "").Trim();
							if (tipoReduccion.EndsWith(".0")) tipoReduccion = tipoReduccion.Replace(".0", "");

							// 🚀 VALIDACIONES DE NEGOCIO
							if (!short.TryParse(claveMunicipio, out short claveMun) || claveMun < 1 || claveMun > 217)
							{
								resultadoFinal.RegistrosFallidos++;
								resultadoFinal.ErroresDetalle.Add($"Fila {i + 1}: Clave de municipio inválida.");
								continue;
							}

							if (!byte.TryParse(tipoReduccion, out byte tipoRed) || tipoRed < 1)
							{
								resultadoFinal.RegistrosFallidos++;
								resultadoFinal.ErroresDetalle.Add($"Fila {i + 1}: Tipo de Reducción inválido. Valor encontrado: '{tipoReduccion}'. Debe ser numérico.");
								continue;
							}

							// 🚀 FILTRO ANTI-DUPLICADOS
							//string llaveUnica = $"{claveMun}-{tipoPredio}-{cuentaPredial}-{tipoRed}";
							//if (reduccionesProcesadas.Contains(llaveUnica))
							//{
							//	resultadoFinal.RegistrosFallidos++;
							//	resultadoFinal.ErroresDetalle.Add($"Fila {i + 1}: La cuenta {cuentaPredial} ya tiene asignado el descuento {tipoRed} en este archivo.");
							//	continue;
							//}
							//reduccionesProcesadas.Add(llaveUnica);

							DataRow nuevaFila = tablaCrudos.NewRow();
							nuevaFila["ClaveMunicipio"] = claveMun.ToString();
							nuevaFila["TipoPredio"] = tipoPredio;
							nuevaFila["CuentaPredial"] = cuentaPredial;
							nuevaFila["FolioUnico"] = Utilerias.LimpiarCadena(folioUnico, 50);
							nuevaFila["TipoReduccion"] = tipoRed.ToString();
							nuevaFila["FolioCarga"] = param.FolioCarga;

							tablaCrudos.Rows.Add(nuevaFila);
						}

						if (tablaCrudos.Rows.Count > 0)
						{
							InsertarBulk(tablaCrudos, param);
							resultadoFinal.RegistrosExitosos += tablaCrudos.Rows.Count;
						}
					}
				}
			}
			catch (Exception ex)
			{
				LogService.WriteLogAsync("ERROR", param.UsuarioLogin, "ProcesadorReduccionesExcel", $"Fallo crítico: {ex.Message}").Wait();
				throw;
			}

			return resultadoFinal;
		}

		private string ExtraerSeguro(DataRow fila, MapeadorInteligente.MapaOficial mapa, string columna, string valorPorDefecto = "")
		{
			try
			{
				string valor = MapeadorInteligente.Extraer(fila, mapa, columna);
				return string.IsNullOrWhiteSpace(valor) ? valorPorDefecto : valor.Trim();
			}
			catch
			{
				return valorPorDefecto;
			}
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

		private void InsertarBulk(DataTable lote, ParametrosCarga param)
		{
			using (SqlConnection conn = new SqlConnection(CadenaConexion))
			{
				conn.Open();
				using (SqlBulkCopy bulkCopy = new SqlBulkCopy(conn))
				{
					bulkCopy.DestinationTableName = "pred_Operacion.Staging_Reducciones";

					bulkCopy.ColumnMappings.Add("ClaveMunicipio", "ClaveMunicipio");
					bulkCopy.ColumnMappings.Add("TipoPredio", "TipoPredio");
					bulkCopy.ColumnMappings.Add("CuentaPredial", "CuentaPredial");
					bulkCopy.ColumnMappings.Add("FolioUnico", "FolioUnico");
					bulkCopy.ColumnMappings.Add("TipoReduccion", "TipoReduccion");
					bulkCopy.ColumnMappings.Add("FolioCarga", "FolioCarga");

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