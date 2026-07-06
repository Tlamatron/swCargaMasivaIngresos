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
		// Usamos la misma forma de conexión original de tu proyecto
		private readonly string CadenaConexion = ConfiguracionApp.ObtenerCadenaConexion();

		public ResultadoProceso Procesar(string rutaArchivo, ParametrosCarga param)
		{
			var resultadoFinal = new ResultadoProceso { ErroresDetalle = new List<string>() };

			LogService.WriteLogAsync("INFO", param.UsuarioLogin, "ProcesadorPadronExcel", $"Iniciando escaneo multi-pestaña para Padrón. Folio: {param.FolioCarga}").Wait();

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

						// 🚀 1. RADAR: Buscamos el inicio de la tabla (hasta 50 filas para saltar logos)
						for (int i = 0; i < Math.Min(50, tablaExcel.Rows.Count); i++)
						{
							var filaTextos = tablaExcel.Rows[i].ItemArray.Select(x => x?.ToString().Trim().ToUpper() ?? "");
							string textoFilaComplet = string.Join(" ", filaTextos);

							// Agregamos "CTA" y "CONTRIBUYENTE" por si el municipio nombra así a la columna
							if (textoFilaComplet.Contains("CUENTA") || textoFilaComplet.Contains("PREDIAL") || textoFilaComplet.Contains("CTA") || textoFilaComplet.Contains("CONTRIBUYENTE"))
							{
								filaEncabezadoReal = i;
								break;
							}
						}

						// Si no hay encabezados, es una pestaña de notas o vacía
						if (filaEncabezadoReal == -1) continue;

						// 🚀 2. CREAMOS LA ESTRUCTURA ORIGINAL Y SEGURA
						DataTable tablaCrudos = CrearEstructuraPadron();

						// 🚀 3. SÚPER-MAPA (Soporta archivos organizados o con sub-cabeceras como Ayotoxco)
						var mapaColumnas = MapeadorInteligente.ObtenerMapaColumnasMultiFila(tablaExcel, filaEncabezadoReal, 3);

						for (int i = filaEncabezadoReal + 1; i < tablaExcel.Rows.Count; i++)
						{
							var fila = tablaExcel.Rows[i];

							// Condición de paro: Fila de sumatorias o completamente vacía
							string textoInicioFila = string.Join(" ", fila.ItemArray.Take(3).Select(x => x?.ToString().ToUpper() ?? ""));
							if (textoInicioFila.Contains("TOTAL") || string.IsNullOrWhiteSpace(string.Join("", fila.ItemArray))) break;

							string cuentaPredial = MapeadorInteligente.ExtraerValor(fila, mapaColumnas, "CUENTA", "PREDIAL", "CTA", "CTA.", "CLAVE");

							// Ignoramos sub-encabezados que se colaron (ej. la palabra "Cuenta" repetida) o filas sin cuenta
							if (string.IsNullOrWhiteSpace(cuentaPredial) || cuentaPredial.Equals("Cuenta", StringComparison.OrdinalIgnoreCase)) continue;

							DataRow nuevaFila = tablaCrudos.NewRow();

							// 🚀 LA TRINIDAD OBLIGATORIA (Clave, Tipo y Cuenta)
							nuevaFila["ClaveMunicipio"] = MapeadorInteligente.ExtraerValor(fila, mapaColumnas, "CLAVE DEL MUNICIPIO", "MUNICIPIO", "CVEMUN");
							nuevaFila["TipoPredio"] = MapeadorInteligente.ExtraerValor(fila, mapaColumnas, "TIPO DE PREDIO", "PREDIO", "TIPO");
							nuevaFila["CuentaPredial"] = cuentaPredial;
							nuevaFila["FolioCarga"] = param.FolioCarga.ToString();

							// 🚀 DATOS EXTRAÍDOS (Si no vienen, se quedan vacíos pacíficamente)
							nuevaFila["ClasePago"] = MapeadorInteligente.ExtraerValor(fila, mapaColumnas, "CLASE DE PAGO", "CLASE");
							nuevaFila["Bimestre"] = MapeadorInteligente.RastrearBimestres(fila, mapaColumnas);

							// Si no pagó (porque es padrón puro) el impuesto será 0
							string impuestoStr = MapeadorInteligente.ExtraerValor(fila, mapaColumnas, "SALDO", "TOTAL", "2026", "IMPUESTO", "IMPORTE", "PAGO");
							nuevaFila["ImpuestoDeterminado"] = string.IsNullOrWhiteSpace(impuestoStr) ? "0" : impuestoStr;

							nuevaFila["FechaVigencia"] = MapeadorInteligente.ExtraerValor(fila, mapaColumnas, "FECHA", "VIGENCIA", "DIA", "DÍA");

							tablaCrudos.Rows.Add(nuevaFila);
						}

						// 🚀 4. LA ADUANA (Al ser Tipo 1, el LimpiadorDatos sabrá que NO debe borrar los registros con $0)
						var resultadoLimpieza = LimpiadorDatos.LimpiarYValidar(tablaCrudos, nombrePestaña, param);

						if (resultadoLimpieza.TablaValidos.Rows.Count > 0)
						{
							InsertarBulkPadron(resultadoLimpieza.TablaValidos);
						}

						resultadoFinal.RegistrosExitosos += resultadoLimpieza.TablaValidos.Rows.Count;
						resultadoFinal.RegistrosFallidos += resultadoLimpieza.TablaRechazados.Rows.Count;

						if (resultadoLimpieza.DetallesErrores != null)
							resultadoFinal.ErroresDetalle.AddRange(resultadoLimpieza.DetallesErrores);
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

		// ==============================================================================
		// 🛡️ ESTRUCTURA SEGURA: Exactamente lo que espera el Limpiador y SQL Server
		// ==============================================================================
		private DataTable CrearEstructuraPadron()
		{
			// Todo en typeof(string) para que el "LimpiadorDatos" pueda procesar textos basura sin que C# explote
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

		// ==============================================================================
		// 🛡️ INSERCIÓN BLINDADA
		// ==============================================================================
		private void InsertarBulkPadron(DataTable lote)
		{
			using (SqlConnection conn = new SqlConnection(CadenaConexion))
			{
				conn.Open();
				using (SqlBulkCopy bulkCopy = new SqlBulkCopy(conn))
				{
					bulkCopy.DestinationTableName = "pred_Operacion.Staging_Predial";
					bulkCopy.BatchSize = 10000;
					bulkCopy.BulkCopyTimeout = 120;

					// 🚀 MAPEO EXPLÍCITO: Le decimos exactamente qué columnas enviar, ignorando "MotivoRechazo"
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