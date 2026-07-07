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
	public class ProcesadorPadronExcel : IProcesadorFormato
	{
		private readonly string CadenaConexion = ConfiguracionApp.ObtenerCadenaConexion();

		public ResultadoProceso Procesar(string rutaArchivo, ParametrosCarga param)
		{
			var resultadoFinal = new ResultadoProceso { ErroresDetalle = new List<string>() };

			LogService.WriteLogAsync("INFO", param.UsuarioLogin, "ProcesadorPadronExcel", $"Iniciando Lectura por Regiones. Folio: {param.FolioCarga}").Wait();

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
						string nombrePestaña = tablaExcel.TableName;

						// 🚀 1. LECTURA POR REGIONES DIRECTA
						int filaInicioDatos;
						var mapaCrudo = MapeadorInteligente.ObtenerMapaPorRegiones(tablaExcel, out filaInicioDatos);

						if (mapaCrudo.Count == 0) continue;

						var mapaBloqueado = MapeadorInteligente.ProcesarEncabezadosConMemoria(mapaCrudo);
						DataTable tablaCrudos = CrearEstructuraPadron();

						// 🚀 2. EXTRACCIÓN
						for (int i = filaInicioDatos; i < tablaExcel.Rows.Count; i++)
						{
							var fila = tablaExcel.Rows[i];

							string textoInicioFila = string.Join(" ", fila.ItemArray.Take(3).Select(x => x?.ToString().ToUpper() ?? ""));
							if (textoInicioFila.Contains("TOTAL")) break;

							if (string.IsNullOrWhiteSpace(string.Join("", fila.ItemArray))) continue;

							string cuentaPredial = MapeadorInteligente.Extraer(fila, mapaBloqueado, "CuentaPredial");
							if (string.IsNullOrWhiteSpace(cuentaPredial) || cuentaPredial.Equals("Cuenta", StringComparison.OrdinalIgnoreCase)) continue;

							string anioPredial = MapeadorInteligente.Extraer(fila, mapaBloqueado, "Anio");
							if (anioPredial.Contains("2025") || anioPredial.Contains("2024") || anioPredial.Contains("2023") || anioPredial.Contains("2022") || anioPredial.Contains("2021")) continue;

							DataRow nuevaFila = tablaCrudos.NewRow();
							nuevaFila["ClaveMunicipio"] = MapeadorInteligente.Extraer(fila, mapaBloqueado, "ClaveMunicipio");
							nuevaFila["TipoPredio"] = MapeadorInteligente.Extraer(fila, mapaBloqueado, "TipoPredio");
							nuevaFila["CuentaPredial"] = cuentaPredial;
							nuevaFila["FolioCarga"] = param.FolioCarga.ToString();
							nuevaFila["ClasePago"] = MapeadorInteligente.Extraer(fila, mapaBloqueado, "ClasePago");
							nuevaFila["Bimestre"] = MapeadorInteligente.RastrearBimestres(fila, mapaBloqueado);
							nuevaFila["ImpuestoDeterminado"] = MapeadorInteligente.Extraer(fila, mapaBloqueado, "ImpuestoDeterminado");
							nuevaFila["FechaVigencia"] = MapeadorInteligente.Extraer(fila, mapaBloqueado, "FechaVigencia");

							tablaCrudos.Rows.Add(nuevaFila);
						}

						var resultadoLimpieza = LimpiadorDatos.LimpiarYValidar(tablaCrudos, nombrePestaña, param);

						if (resultadoLimpieza.TablaValidos.Rows.Count > 0)
						{
							InsertarBulkPadron(resultadoLimpieza.TablaValidos);
						}

						resultadoFinal.RegistrosExitosos += resultadoLimpieza.TablaValidos.Rows.Count;
						resultadoFinal.RegistrosFallidos += resultadoLimpieza.TablaRechazados.Rows.Count;
						if (resultadoLimpieza.DetallesErrores != null) resultadoFinal.ErroresDetalle.AddRange(resultadoLimpieza.DetallesErrores);
					}
				}
			}
			catch (Exception ex)
			{
				LogService.WriteLogAsync("ERROR", param.UsuarioLogin, "ProcesadorPadronExcel", $"Fallo crítico: {ex.Message}").Wait();
				throw;
			}

			return resultadoFinal;
		}

		private DataTable CrearEstructuraPadron()
		{
			DataTable tabla = new DataTable();
			tabla.Columns.Add("ClaveMunicipio", typeof(string));
			tabla.Columns.Add("TipoPredio", typeof(string));
			tabla.Columns.Add("CuentaPredial", typeof(string));
			tabla.Columns.Add("ClasePago", typeof(string));
			tabla.Columns.Add("Bimestre", typeof(string));
			tabla.Columns.Add("ImpuestoDeterminado", typeof(string));
			tabla.Columns.Add("FechaVigencia", typeof(string));
			tabla.Columns.Add("FolioCarga", typeof(string));
			return tabla;
		}

		private void InsertarBulkPadron(DataTable lote)
		{
			foreach (DataRow row in lote.Rows)
			{
				if (row["FechaVigencia"] != DBNull.Value && string.IsNullOrWhiteSpace(row["FechaVigencia"].ToString())) row["FechaVigencia"] = DBNull.Value;
				if (row["ImpuestoDeterminado"] != DBNull.Value && string.IsNullOrWhiteSpace(row["ImpuestoDeterminado"].ToString())) row["ImpuestoDeterminado"] = "0";
				if (row["Bimestre"] != DBNull.Value && string.IsNullOrWhiteSpace(row["Bimestre"].ToString())) row["Bimestre"] = "0";
				if (row["ClasePago"] != DBNull.Value && string.IsNullOrWhiteSpace(row["ClasePago"].ToString())) row["ClasePago"] = "1";
			}

			using (SqlConnection conn = new SqlConnection(CadenaConexion))
			{
				conn.Open();
				using (SqlBulkCopy bulkCopy = new SqlBulkCopy(conn))
				{
					bulkCopy.DestinationTableName = "pred_Operacion.Staging_Predial";
					bulkCopy.BatchSize = 10000;
					bulkCopy.BulkCopyTimeout = 120;

					bulkCopy.ColumnMappings.Add("ClaveMunicipio", "ClaveMunicipio");
					bulkCopy.ColumnMappings.Add("TipoPredio", "TipoPredio");
					bulkCopy.ColumnMappings.Add("CuentaPredial", "CuentaPredial");
					bulkCopy.ColumnMappings.Add("ClasePago", "ClasePago");
					bulkCopy.ColumnMappings.Add("Bimestre", "Bimestre");
					bulkCopy.ColumnMappings.Add("ImpuestoDeterminado", "ImpuestoDeterminado");
					bulkCopy.ColumnMappings.Add("FechaVigencia", "FechaVigencia");
					bulkCopy.ColumnMappings.Add("FolioCarga", "FolioCarga");

					bulkCopy.WriteToServer(lote);
				}
			}
		}
	}
}