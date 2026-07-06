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
	public class ProcesadorPagosExcel : IProcesadorFormato
	{
		private readonly string CadenaConexion = ConfiguracionApp.ObtenerCadenaConexion();

		public ResultadoProceso Procesar(string rutaArchivo, ParametrosCarga param)
		{
			var resultadoFinal = new ResultadoProceso { ErroresDetalle = new List<string>() };

			LogService.WriteLogAsync("INFO", param.UsuarioLogin, "ProcesadorPagosExcel", $"Iniciando Radar de Excel. Folio: {param.FolioCarga}").Wait();

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
						int filaEncabezadoReal = -1;

						// 🚀 1. RADAR CON FILTRO DE DENSIDAD (Evita títulos masivos de una sola celda)
						for (int i = 0; i < Math.Min(50, tablaExcel.Rows.Count); i++)
						{
							var celdasLlenas = tablaExcel.Rows[i].ItemArray
								.Select(x => x?.ToString().Trim() ?? "")
								.Where(x => !string.IsNullOrWhiteSpace(x))
								.ToList();

							string textoFilaComplet = string.Join(" ", celdasLlenas).ToUpper();

							if (celdasLlenas.Count >= 3 && (textoFilaComplet.Contains("CUENTA") ||
								textoFilaComplet.Contains("PREDIAL") || textoFilaComplet.Contains("CTA")))
							{
								filaEncabezadoReal = i;
								break;
							}
						}

						if (filaEncabezadoReal == -1) continue;

						DataTable tablaCrudos = CrearEstructuraRaw();
						var mapaColumnas = MapeadorInteligente.ObtenerMapaColumnasMultiFila(tablaExcel, filaEncabezadoReal, 3);

						for (int i = filaEncabezadoReal + 1; i < tablaExcel.Rows.Count; i++)
						{
							var fila = tablaExcel.Rows[i];

							string textoInicioFila = string.Join(" ", fila.ItemArray.Take(3).Select(x => x?.ToString().ToUpper() ?? ""));
							if (textoInicioFila.Contains("TOTAL") || string.IsNullOrWhiteSpace(string.Join("", fila.ItemArray))) break;

							string cuentaPredial = MapeadorInteligente.ExtraerValor(fila, mapaColumnas, "CUENTA", "PREDIAL", "CTA", "CTA.", "CLAVE");
							if (string.IsNullOrWhiteSpace(cuentaPredial) || cuentaPredial == "Cuenta") continue;

							// 🚀 2. FILTRO ESTRICTO DE AÑO (Ignora deudas pasadas de Amixtlán)
							string anioPredial = MapeadorInteligente.ExtraerValor(fila, mapaColumnas, "AÑO", "EJERCICIO");
							if (!string.IsNullOrWhiteSpace(anioPredial) && !anioPredial.Contains("2026")) continue;

							DataRow nuevaFila = tablaCrudos.NewRow();
							nuevaFila["ClaveMunicipio"] = MapeadorInteligente.ExtraerValor(fila, mapaColumnas, "CLAVE DEL MUNICIPIO", "MUNICIPIO", "CVEMUN");
							nuevaFila["TipoPredio"] = MapeadorInteligente.ExtraerValor(fila, mapaColumnas, "TIPO DE PREDIO", "PREDIO", "TIPO");
							nuevaFila["CuentaPredial"] = cuentaPredial;
							nuevaFila["ClasePago"] = MapeadorInteligente.ExtraerValor(fila, mapaColumnas, "CLASE DE PAGO", "CLASE");
							nuevaFila["Bimestre"] = MapeadorInteligente.RastrearBimestres(fila, mapaColumnas);
							nuevaFila["ImpuestoDeterminado"] = MapeadorInteligente.ExtraerValor(fila, mapaColumnas, "SALDO", "TOTAL", "2026", "PAGO", "IMPUESTO", "IMPORTE");
							nuevaFila["FechaVigencia"] = MapeadorInteligente.ExtraerValor(fila, mapaColumnas, "FECHA", "VIGENCIA", "DIA");

							tablaCrudos.Rows.Add(nuevaFila);
						}

						var resultadoLimpieza = LimpiadorDatos.LimpiarYValidar(tablaCrudos, nombrePestaña, param);

						if (resultadoLimpieza.TablaValidos.Rows.Count > 0)
						{
							InsertarBulk(resultadoLimpieza.TablaValidos);
						}

						resultadoFinal.RegistrosExitosos += resultadoLimpieza.TablaValidos.Rows.Count;
						resultadoFinal.RegistrosFallidos += resultadoLimpieza.TablaRechazados.Rows.Count;
						if (resultadoLimpieza.DetallesErrores != null) resultadoFinal.ErroresDetalle.AddRange(resultadoLimpieza.DetallesErrores);
					}
				}
			}
			catch (Exception ex)
			{
				LogService.WriteLogAsync("ERROR", param.UsuarioLogin, "ProcesadorPagosExcel", $"Fallo crítico: {ex.Message}").Wait();
				throw;
			}

			return resultadoFinal;
		}

		private DataTable CrearEstructuraRaw()
		{
			DataTable dt = new DataTable();
			dt.Columns.Add("ClaveMunicipio", typeof(string));
			dt.Columns.Add("TipoPredio", typeof(string));
			dt.Columns.Add("CuentaPredial", typeof(string));
			dt.Columns.Add("ClasePago", typeof(string));
			dt.Columns.Add("Bimestre", typeof(string));
			dt.Columns.Add("ImpuestoDeterminado", typeof(string));
			dt.Columns.Add("FechaVigencia", typeof(string));
			dt.Columns.Add("FolioCarga", typeof(string));
			return dt;
		}

		private void InsertarBulk(DataTable lote)
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