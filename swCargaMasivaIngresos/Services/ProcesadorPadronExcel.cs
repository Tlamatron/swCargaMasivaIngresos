using ExcelDataReader;
using swCargaMasivaIngresos.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.IO;

namespace swCargaMasivaIngresos.Services
{
	public class ProcesadorPadronExcel : IProcesadorFormato
	{
		private readonly string AppName = System.Configuration.ConfigurationManager.AppSettings["NombAplicacion"] ?? "APICargaMasivaIngresos";
		private readonly string CadenaConexion = ConfiguracionApp.ObtenerCadenaConexion();

		public ResultadoProceso Procesar(string rutaArchivo, ParametrosCarga param)
		{
			var resultado = new ResultadoProceso { ErroresDetalle = new List<string>() };
			DataTable tablaLote = CrearEstructuraPadron();
			HashSet<string> cuentasProcesadas = new HashSet<string>();

			LogService.WriteLogAsync(AppName, "INFO", param.UsuarioLogin, "ProcesadorPadronExcel", $"Inicia lectura de archivo Excel (Padrón). Folio: {param.FolioCarga}");

			using (var stream = File.Open(rutaArchivo, FileMode.Open, FileAccess.Read))
			using (var reader = ExcelReaderFactory.CreateReader(stream))
			{
				int numeroLinea = 0;

				// 💡 Si el Excel trae encabezados, descomenta esta línea para saltar la fila 1:
				// reader.Read(); numeroLinea++;

				while (reader.Read())
				{
					numeroLinea++;

					// 1. Validar que tenga al menos 24 columnas
					if (reader.FieldCount < 24)
					{
						MarcarError(resultado, numeroLinea, $"Columnas incorrectas. Se esperaban al menos 24.");
						continue;
					}

					// Extraemos los componentes clave (Celdas 0, 1, 2, 21)
					string claveMunicipio = reader.GetValue(0)?.ToString().Trim();
					string tipoPredio = reader.GetValue(1)?.ToString().Trim();
					string cuentaPredial = reader.GetValue(2)?.ToString().Trim();
					string strBimestre = reader.GetValue(21)?.ToString().Trim();

					if (string.IsNullOrEmpty(cuentaPredial))
					{
						MarcarError(resultado, numeroLinea, "El número de cuenta predial es obligatorio.");
						continue;
					}

					// ====================================================================
					// VALIDACIONES ESTRICTAS
					// ====================================================================
					if (!short.TryParse(claveMunicipio, out short claveMun) || claveMun < 1 || claveMun > 217)
					{
						MarcarError(resultado, numeroLinea, "Clave de municipio inválida (Debe ser numérico entre 1 y 217).");
						continue;
					}

					if (!byte.TryParse(tipoPredio, out byte tipoPre) || tipoPre < 1 || tipoPre > 3)
					{
						MarcarError(resultado, numeroLinea, "Tipo de predio inválido (1=Urbano, 2=Rústico, 3=Suburbano).");
						continue;
					}

					string folioUnicoStr = reader.GetValue(3)?.ToString().Trim();
					if (!string.IsNullOrWhiteSpace(folioUnicoStr) && !long.TryParse(folioUnicoStr, out _))
					{
						MarcarError(resultado, numeroLinea, "Folio Único inválido (Debe ser exclusivamente numérico).");
						continue;
					}

					string tipoPersonaStr = reader.GetValue(14)?.ToString().Trim();
					if (!string.IsNullOrWhiteSpace(tipoPersonaStr))
					{
						if (!byte.TryParse(tipoPersonaStr, out byte tipoPer) || (tipoPer != 1 && tipoPer != 2))
						{
							MarcarError(resultado, numeroLinea, "Tipo de Persona inválida (1=Física, 2=Moral).");
							continue;
						}
					}

					string strClasePago = reader.GetValue(20)?.ToString().Trim();
					if (!byte.TryParse(strClasePago, out byte clasePago) || (clasePago != 1 && clasePago != 2))
					{
						MarcarError(resultado, numeroLinea, "Clase de Pago inválida (1=Anual, 2=Bimestral).");
						continue;
					}

					if (!byte.TryParse(strBimestre, out byte bimestre) || bimestre > 6)
					{
						MarcarError(resultado, numeroLinea, "Bimestre inválido (Debe ser un número del 0 al 6).");
						continue;
					}

					// Validar duplicados exactos
					string llaveUnica = $"{claveMun}-{tipoPre}-{cuentaPredial}-{bimestre}";
					if (cuentasProcesadas.Contains(llaveUnica))
					{
						MarcarError(resultado, numeroLinea, $"El predio con Cuenta {cuentaPredial} tiene el Bimestre {bimestre} duplicado en el archivo.");
						continue;
					}
					cuentasProcesadas.Add(llaveUnica);

					// Lógica robusta para Base Gravable (Celda 19) e Impuesto (Celda 22)
					decimal baseGravable = 0;
					var valBase = reader.GetValue(19);
					if (valBase != null)
					{
						if (valBase is double d) baseGravable = (decimal)d;
						else if (!Utilerias.TryParseMoneda(valBase.ToString(), out baseGravable))
						{
							MarcarError(resultado, numeroLinea, "Base gravable inválida.");
							continue;
						}
					}

					decimal impuestoPagar = 0;
					var valImpuesto = reader.GetValue(22);
					if (valImpuesto is double dImp) impuestoPagar = (decimal)dImp;
					else if (!Utilerias.TryParseMoneda(valImpuesto?.ToString(), out impuestoPagar))
					{
						MarcarError(resultado, numeroLinea, "Impuesto determinado inválido.");
						continue;
					}

					// Lógica robusta para Fecha (Celda 23)
					DateTime fechaVigencia;
					var valFecha = reader.GetValue(23);
					if (valFecha is DateTime dt) fechaVigencia = dt;
					else if (!Utilerias.TryParseFecha(valFecha?.ToString(), out fechaVigencia))
					{
						MarcarError(resultado, numeroLinea, "Fecha de vigencia inválida.");
						continue;
					}

					// ====================================================================

					// 3. Se agregan los datos limpios al lote
					tablaLote.Rows.Add(
						claveMun.ToString(),
						tipoPre.ToString(),
						cuentaPredial,
						Utilerias.LimpiarCadena(folioUnicoStr, 50),
						Utilerias.LimpiarCadena(reader.GetValue(4)?.ToString(), 150),
						Utilerias.LimpiarCadena(reader.GetValue(5)?.ToString(), 150),
						Utilerias.LimpiarCadena(reader.GetValue(6)?.ToString(), 20),
						Utilerias.LimpiarCadena(reader.GetValue(7)?.ToString(), 20),
						Utilerias.LimpiarCadena(reader.GetValue(8)?.ToString(), 10),
						Utilerias.LimpiarCadena(reader.GetValue(9)?.ToString(), 150),
						Utilerias.LimpiarCadena(reader.GetValue(10)?.ToString(), 10),
						Utilerias.LimpiarCadena(reader.GetValue(11)?.ToString(), 150),
						Utilerias.LimpiarCadena(reader.GetValue(12)?.ToString(), 100),
						Utilerias.LimpiarCadena(reader.GetValue(13)?.ToString(), 100),
						Utilerias.LimpiarCadena(tipoPersonaStr, 5),
						Utilerias.LimpiarCadena(reader.GetValue(15)?.ToString(), 15),
						Utilerias.LimpiarCadena(reader.GetValue(16)?.ToString(), 10),
						Utilerias.LimpiarCadena(reader.GetValue(17)?.ToString(), 10),
						Utilerias.LimpiarCadena(reader.GetValue(18)?.ToString(), 10),
						baseGravable,
						clasePago.ToString(),
						bimestre.ToString(),
						impuestoPagar,
						fechaVigencia,
						param.FolioCarga // 🚀 Ya usa el parámetro como int
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
			res.ErroresDetalle.Add($"Fila {linea}: {msg}");
		}

		private DataTable CrearEstructuraPadron()
		{
			var tabla = new DataTable();
			tabla.Columns.Add("ClaveMunicipio", typeof(string));
			tabla.Columns.Add("TipoPredio", typeof(string));
			tabla.Columns.Add("CuentaPredial", typeof(string));
			tabla.Columns.Add("FolioUnico", typeof(string));
			tabla.Columns.Add("Localidad", typeof(string));
			tabla.Columns.Add("Calle", typeof(string));
			tabla.Columns.Add("NumExterior", typeof(string));
			tabla.Columns.Add("NumInterior", typeof(string));
			tabla.Columns.Add("Letra", typeof(string));
			tabla.Columns.Add("Colonia", typeof(string));
			tabla.Columns.Add("CP", typeof(string));
			tabla.Columns.Add("Nombre", typeof(string));
			tabla.Columns.Add("PrimerApellido", typeof(string));
			tabla.Columns.Add("SegundoApellido", typeof(string));
			tabla.Columns.Add("TipoPersona", typeof(string));
			tabla.Columns.Add("RFC", typeof(string));
			tabla.Columns.Add("ClaveRegimenSAT", typeof(string));
			tabla.Columns.Add("ClaveUsoSAT", typeof(string));
			tabla.Columns.Add("CPFiscalSAT", typeof(string));
			tabla.Columns.Add("BaseGravable", typeof(decimal));
			tabla.Columns.Add("ClasePago", typeof(string));
			tabla.Columns.Add("Bimestre", typeof(string));
			tabla.Columns.Add("ImpuestoDeterminado", typeof(decimal));
			tabla.Columns.Add("FechaVigencia", typeof(DateTime));
			tabla.Columns.Add("FolioCarga", typeof(int)); // 🚀 Columna actualizada a INT
			return tabla;
		}

		private void InsertarLoteEnBD(DataTable lote, ParametrosCarga param)
		{
			using (SqlConnection conn = new SqlConnection(CadenaConexion))
			{
				conn.Open();
				using (SqlBulkCopy bulkCopy = new SqlBulkCopy(conn))
				{
					bulkCopy.DestinationTableName = "pred_Operacion.Staging_Predial";
					bulkCopy.BatchSize = 10000;
					bulkCopy.BulkCopyTimeout = 120;
					bulkCopy.WriteToServer(lote);
				}

				using (SqlCommand cmd = new SqlCommand("pred_Operacion.sp_ProcesarMergePadron", conn))
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