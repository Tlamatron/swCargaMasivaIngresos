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

						// 🚀 1. RADAR: Buscamos el inicio de la tabla
						for (int i = 0; i < Math.Min(50, tablaExcel.Rows.Count); i++)
						{
							var filaTextos = tablaExcel.Rows[i].ItemArray.Select(x => x?.ToString().Trim().ToUpper() ?? "");
							string textoFilaComplet = string.Join(" ", filaTextos);

							if (textoFilaComplet.Contains("CUENTA") || textoFilaComplet.Contains("PREDIAL") || textoFilaComplet.Contains("CTA"))
							{
								filaEncabezadoReal = i;
								break;
							}
						}

						if (filaEncabezadoReal == -1) continue;

						DataTable tablaCrudos = CrearEstructuraPadron();

						// 🚀 2. SÚPER-MAPA (Soporta archivos con y sin sub-cabeceras)
						var mapaColumnas = MapeadorInteligente.ObtenerMapaColumnasMultiFila(tablaExcel, filaEncabezadoReal, 3);

						for (int i = filaEncabezadoReal + 1; i < tablaExcel.Rows.Count; i++)
						{
							var fila = tablaExcel.Rows[i];

							string textoInicioFila = string.Join(" ", fila.ItemArray.Take(3).Select(x => x?.ToString().ToUpper() ?? ""));
							if (textoInicioFila.Contains("TOTAL") || string.IsNullOrWhiteSpace(string.Join("", fila.ItemArray))) break;

							string cuentaPredial = MapeadorInteligente.ExtraerValor(fila, mapaColumnas, "CUENTA", "PREDIAL", "CTA", "CTA.", "CLAVE");
							if (string.IsNullOrWhiteSpace(cuentaPredial) || cuentaPredial.Equals("Cuenta", StringComparison.OrdinalIgnoreCase)) continue;

							DataRow nuevaFila = tablaCrudos.NewRow();

							// 🚀 OBLIGATORIOS (La Trinidad)
							nuevaFila["ClaveMunicipio"] = MapeadorInteligente.ExtraerValor(fila, mapaColumnas, "CLAVE DEL MUNICIPIO", "MUNICIPIO", "CVEMUN");
							nuevaFila["TipoPredio"] = MapeadorInteligente.ExtraerValor(fila, mapaColumnas, "TIPO DE PREDIO", "PREDIO", "TIPO");
							nuevaFila["CuentaPredial"] = cuentaPredial;
							nuevaFila["FolioCarga"] = param.FolioCarga.ToString();

							// 🚀 OPCIONALES (Si no vienen en el Excel, se quedan vacíos pacíficamente)
							nuevaFila["NombrePropietario"] = MapeadorInteligente.ExtraerValor(fila, mapaColumnas, "NOMBRE", "PROPIETARIO", "CONTRIBUYENTE");
							nuevaFila["Calle"] = MapeadorInteligente.ExtraerValor(fila, mapaColumnas, "CALLE", "DIRECCION", "DOMICILIO");
							nuevaFila["Colonia"] = MapeadorInteligente.ExtraerValor(fila, mapaColumnas, "COLONIA", "FRACCIONAMIENTO");
							nuevaFila["Localidad"] = MapeadorInteligente.ExtraerValor(fila, mapaColumnas, "LOCALIDAD", "POBLACION");

							string baseGravStr = MapeadorInteligente.ExtraerValor(fila, mapaColumnas, "BASE GRAVABLE", "BASE", "VALOR CATASTRAL");
							nuevaFila["BaseGravable"] = string.IsNullOrWhiteSpace(baseGravStr) ? "0" : baseGravStr;

							nuevaFila["ClasePago"] = MapeadorInteligente.ExtraerValor(fila, mapaColumnas, "CLASE DE PAGO", "CLASE");
							nuevaFila["Bimestre"] = MapeadorInteligente.RastrearBimestres(fila, mapaColumnas);

							string impuestoStr = MapeadorInteligente.ExtraerValor(fila, mapaColumnas, "SALDO", "TOTAL", "2026", "IMPUESTO", "IMPORTE");
							nuevaFila["ImpuestoDeterminado"] = string.IsNullOrWhiteSpace(impuestoStr) ? "0" : impuestoStr;

							tablaCrudos.Rows.Add(nuevaFila);
						}

						// 🚀 3. LA ADUANA (El LimpiadorDatos sabrá que es Tipo 1 y NO borrará a los que tienen $0)
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
			// Transformamos a STRING para evitar fallos si el municipio escribe texto donde van números
			DataTable tabla = new DataTable();
			tabla.Columns.Add("ClaveMunicipio", typeof(string));
			tabla.Columns.Add("TipoPredio", typeof(string));
			tabla.Columns.Add("CuentaPredial", typeof(string));
			tabla.Columns.Add("NombrePropietario", typeof(string));
			tabla.Columns.Add("Calle", typeof(string));
			tabla.Columns.Add("NumInt", typeof(string));
			tabla.Columns.Add("NumExt", typeof(string));
			tabla.Columns.Add("Colonia", typeof(string));
			tabla.Columns.Add("Localidad", typeof(string));
			tabla.Columns.Add("NombrePropietario2", typeof(string));
			tabla.Columns.Add("TipoPersona", typeof(string));
			tabla.Columns.Add("RFC", typeof(string));
			tabla.Columns.Add("ClaveRegimenSAT", typeof(string));
			tabla.Columns.Add("ClaveUsoSAT", typeof(string));
			tabla.Columns.Add("CPFiscalSAT", typeof(string));
			tabla.Columns.Add("BaseGravable", typeof(string));
			tabla.Columns.Add("ClasePago", typeof(string));
			tabla.Columns.Add("Bimestre", typeof(string));
			tabla.Columns.Add("ImpuestoDeterminado", typeof(string));
			tabla.Columns.Add("FechaVigencia", typeof(string));
			tabla.Columns.Add("FolioCarga", typeof(string));
			return tabla;
		}

		private void InsertarBulkPadron(DataTable lote)
		{
			using (SqlConnection conn = new SqlConnection(CadenaConexion))
			{
				conn.Open();
				using (SqlBulkCopy bulkCopy = new SqlBulkCopy(conn))
				{
					bulkCopy.DestinationTableName = "pred_Operacion.Staging_Predial";

					// Mapeamos automáticamente todas las columnas extraídas hacia SQL Server
					foreach (DataColumn col in lote.Columns)
					{
						if (col.ColumnName != "MotivoRechazo")
						{
							bulkCopy.ColumnMappings.Add(col.ColumnName, col.ColumnName);
						}
					}
					bulkCopy.WriteToServer(lote);
				}
			}
		}
	}
}