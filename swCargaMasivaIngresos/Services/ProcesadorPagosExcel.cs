using ExcelDataReader; 
using swCargaMasivaIngresos.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace swCargaMasivaIngresos.Services
{
	/// <summary>
	/// Procesa archivos Excel de pagos, aplicando validaciones de negocio, evitando duplicados y realizando inserciones masivas en la base de datos.
	/// </summary>
	public class ProcesadorPagosExcel : IProcesadorFormato
	{
		private readonly string AppName = System.Configuration.ConfigurationManager.AppSettings["NombAplicacion"] ?? "APICargaMasivaIngresos";
		private readonly string CadenaConexion = ConfiguracionApp.ObtenerCadenaConexion();

		//public ResultadoProceso Procesar_v01(string rutaArchivo, ParametrosCarga param)
		//{
		//	var resultado = new ResultadoProceso { ErroresDetalle = new List<string>() };
		//	DataTable tablaLote = CrearEstructuraPagos();
		//	HashSet<string> pagosProcesados = new HashSet<string>();

		//	LogService.WriteLogAsync("INFO", param.UsuarioLogin, "ProcesadorPagosExcel", $"Inicia lectura de archivo Excel de Pagos. Folio: {param.FolioCarga}");

		//	using (var stream = File.Open(rutaArchivo, FileMode.Open, FileAccess.Read))
		//	using (var reader = ExcelReaderFactory.CreateReader(stream))
		//	{
		//		int numeroLinea = 0;

		//		// 💡 Opcional: Si tus usuarios van a subir el Excel con una fila de encabezados, 
		//		// descomenta la siguiente línea para que se salte la primera fila:
		//		// reader.Read(); numeroLinea++;

		//		while (reader.Read())
		//		{
		//			numeroLinea++;

		//			// 1. Validar que el Excel tenga las 24 columnas
		//			if (reader.FieldCount < 24)
		//			{
		//				MarcarError(resultado, numeroLinea, $"Columnas incorrectas en Excel. Se esperaban al menos 24.");
		//				continue;
		//			}

		//			// 2. Extraer datos directamente por su índice (0 a 23)
		//			// Utilizamos GetValue().ToString() para evitar errores si la celda es numérica o nula
		//			string claveMunicipio = reader.GetValue(0)?.ToString().Trim();
		//			string tipoPredio = reader.GetValue(1)?.ToString().Trim();
		//			string cuentaPredial = reader.GetValue(2)?.ToString().Trim();
		//			string strBimestre = reader.GetValue(21)?.ToString().Trim();

		//			// 3. Validaciones de negocio (Las mismas que el TXT)
		//			if (string.IsNullOrEmpty(cuentaPredial))
		//			{
		//				MarcarError(resultado, numeroLinea, "La Cuenta Predial es obligatoria.");
		//				continue;
		//			}
		//			if (!short.TryParse(claveMunicipio, out short claveMun) || claveMun < 1 || claveMun > 217)
		//			{
		//				MarcarError(resultado, numeroLinea, "Clave de municipio inválida (1 a 217).");
		//				continue;
		//			}
		//			if (!byte.TryParse(tipoPredio, out byte tipoPre) || tipoPre < 1 || tipoPre > 3)
		//			{
		//				MarcarError(resultado, numeroLinea, "Tipo de predio inválido (1=Urbano, 2=Rústico, 3=Suburbano).");
		//				continue;
		//			}
		//			if (!byte.TryParse(strBimestre, out byte bimestre) || bimestre > 6)
		//			{
		//				MarcarError(resultado, numeroLinea, "Bimestre inválido (Debe ser un número del 0 al 6).");
		//				continue;
		//			}

		//			// 4. Lógica del HashSet para duplicados
		//			string llaveUnica = $"{claveMun}-{tipoPre}-{cuentaPredial}-{bimestre}";
		//			if (pagosProcesados.Contains(llaveUnica)) continue;

		//			pagosProcesados.Add(llaveUnica);

		//			// 5. Agregar a memoria
		//			tablaLote.Rows.Add(
		//				claveMun.ToString(),
		//				tipoPre.ToString(),
		//				cuentaPredial,
		//				param.FolioCarga, // 🚀 Ya viene como int desde tu parámetro
		//				bimestre.ToString()
		//			);

		//			resultado.RegistrosExitosos++;

		//			// 6. Inyección masiva
		//			if (tablaLote.Rows.Count >= 10000)
		//			{
		//				InsertarLoteEnBD(tablaLote, param);
		//				tablaLote.Clear();
		//			}
		//		}

		//		if (tablaLote.Rows.Count > 0) InsertarLoteEnBD(tablaLote, param);
		//	}

		//	return resultado;
		//}

		/// <summary>
		/// Procesa un archivo Excel de pagos, aplicando validaciones de negocio, evitando duplicados y realizando inserciones masivas en la base de datos.
		/// </summary>
		/// <param name="rutaArchivo"></param>
		/// <param name="param"></param>
		/// <returns></returns>
		public ResultadoProceso Procesar(string rutaArchivo, ParametrosCarga param)
		{
			var resultadoFinal = new ResultadoProceso { ErroresDetalle = new List<string>() };

			LogService.WriteLogAsync("INFO", param.UsuarioLogin, "ProcesadorPagosExcel", $"Iniciando Radar de Excel. Folio: {param.FolioCarga}").Wait();

			try
			{
				using (var stream = File.Open(rutaArchivo, FileMode.Open, FileAccess.Read))
				using (var reader = ExcelReaderFactory.CreateReader(stream))
				{
					// 1. Leemos todo sin forzar la Fila 1 como encabezado
					var dataSet = reader.AsDataSet(new ExcelDataSetConfiguration
					{
						ConfigureDataTable = _ => new ExcelDataTableConfiguration { UseHeaderRow = false }
					});

					// 2. Iteramos pestaña por pestaña (Soporte para Ayotoxco)
					foreach (DataTable tablaExcel in dataSet.Tables)
					{
						string nombrePestaña = tablaExcel.TableName;
						int filaEncabezadoReal = -1;

						// 🚀 RADAR DE METADATOS: Escanear ruido para encontrar el encabezado real
						for (int i = 0; i < Math.Min(50, tablaExcel.Rows.Count); i++)
						{
							var filaTextos = tablaExcel.Rows[i].ItemArray.Select(x => x?.ToString().Trim().ToUpper() ?? "");
							string textoFilaComplet = string.Join(" ", filaTextos);

							if (textoFilaComplet.Contains("CUENTA") || textoFilaComplet.Contains("PREDIAL") ||
								textoFilaComplet.Contains("CTA") || textoFilaComplet.Contains("CONTRIBUYENTE"))
							{
								filaEncabezadoReal = i;
								break;
							}
						}

						if (filaEncabezadoReal == -1) continue; // Si la pestaña está vacía, la ignoramos

						// 3. PREPARAR TABLA STAGING CRUDOS
						DataTable tablaCrudos = CrearEstructuraRaw();
						// 🚀 1. USAMOS EL SÚPER-MAPA (Atrapa la fila base y las 2 de abajo)
						var mapaColumnas = MapeadorInteligente.ObtenerMapaColumnasMultiFila(tablaExcel, filaEncabezadoReal, 3);

						// 4. EXTRACCIÓN (Todo se lee como texto, evitando errores de INT)
						for (int i = filaEncabezadoReal + 1; i < tablaExcel.Rows.Count; i++)
						{
							var fila = tablaExcel.Rows[i];

							// 🚀 2. Paro Seguro: Solo buscamos "TOTAL" en las primeras 3 columnas 
							string textoInicioFila = string.Join(" ", fila.ItemArray.Take(3).Select(x => x?.ToString().ToUpper() ?? ""));
							if (textoInicioFila.Contains("TOTAL")) break;

							// Paro Seguro 2: Fila completamente vacía
							if (string.IsNullOrWhiteSpace(string.Join("", fila.ItemArray))) break;

							// Mapeo Ampliado
							string cuentaPredial = MapeadorInteligente.ExtraerValor(fila, mapaColumnas, "CUENTA", "PREDIAL", "CTA", "CTA.", "CLAVE");

							// 🚀 3. Ignoramos filas basura (como las sub-cabeceras de "BIMESTRES" o "1,2,3") que no traen una cuenta numérica
							if (string.IsNullOrWhiteSpace(cuentaPredial) || cuentaPredial == "Cuenta") continue;

							DataRow nuevaFila = tablaCrudos.NewRow();
							nuevaFila["ClaveMunicipio"] = MapeadorInteligente.ExtraerValor(fila, mapaColumnas, "CLAVE DEL MUNICIPIO", "MUNICIPIO", "CVEMUN");
							nuevaFila["TipoPredio"] = MapeadorInteligente.ExtraerValor(fila, mapaColumnas, "TIPO DE PREDIO", "PREDIO", "TIPO");
							nuevaFila["CuentaPredial"] = cuentaPredial;
							nuevaFila["ClasePago"] = MapeadorInteligente.ExtraerValor(fila, mapaColumnas, "CLASE DE PAGO", "CLASE");
							nuevaFila["Bimestre"] = MapeadorInteligente.RastrearBimestres(fila, mapaColumnas);

							// 🚀 4. PRIORIDAD DE TOTALES: Atrapamos el "SALDO", luego el "TOTAL", o el "2026"
							nuevaFila["ImpuestoDeterminado"] = MapeadorInteligente.ExtraerValor(fila, mapaColumnas, "SALDO", "TOTAL", "2026", "PAGO", "IMPUESTO", "IMPORTE", "MONTO");
							nuevaFila["FechaVigencia"] = MapeadorInteligente.ExtraerValor(fila, mapaColumnas, "FECHA", "VIGENCIA", "DIA", "DÍA");

							tablaCrudos.Rows.Add(nuevaFila);
						}

						// 🚀 5. PASO POR LA ADUANA (Inferencia de Ayotoxco en acción)
						var resultadoLimpieza = LimpiadorDatos.LimpiarYValidar(tablaCrudos, nombrePestaña, param);
						
						// 6. INSERCIÓN MASIVA A BD
						if (resultadoLimpieza.TablaValidos.Rows.Count > 0)
						{
							InsertarBulk(resultadoLimpieza.TablaValidos);
						}

						// 7. ACUMULACIÓN DE MÉTRICAS GLOBALES PARA EL CORREO
						resultadoFinal.RegistrosExitosos += resultadoLimpieza.TablaValidos.Rows.Count;
						resultadoFinal.RegistrosFallidos += resultadoLimpieza.TablaRechazados.Rows.Count;

						if (resultadoLimpieza.DetallesErrores != null)
						{
							resultadoFinal.ErroresDetalle.AddRange(resultadoLimpieza.DetallesErrores);
						}
					}
				}
			}
			catch (Exception ex)
			{
				LogService.WriteLogAsync("ERROR", param.UsuarioLogin, "ProcesadorPagosExcel", $"Fallo crítico: {ex.Message}").Wait();
				throw; // Dejamos que el MotorPrincipalCarga lo atrape para que envíe el correo de Error Fatal
			}

			return resultadoFinal;
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

		//public async Task<RespuestaCarga> ProcesarAsync(Stream fileStream, ParametrosCarga param)
		//{
		//	var respuesta = new RespuestaCarga();
		//	var listaRegistros = new List<PadronEstandarDTO>();

		//	try
		//	{
		//		using (var reader = ExcelReaderFactory.CreateReader(fileStream))
		//		{
		//			// 🚀 1. Leemos todo sin asumir que la Fila 1 es el encabezado
		//			var dataSet = reader.AsDataSet(new ExcelDataSetConfiguration
		//			{
		//				ConfigureDataTable = _ => new ExcelDataTableConfiguration { UseHeaderRow = false }
		//			});

		//			// 🚀 2. Iteramos por TODAS las pestañas del libro
		//			foreach (DataTable tabla in dataSet.Tables)
		//			{
		//				string nombrePestaña = tabla.TableName.ToUpper();

		//				// Deducimos el Tipo de Predio inicial por el nombre de la hoja
		//				int tipoPredioPestaña = nombrePestaña.Contains("URBANO") ? 1 : (nombrePestaña.Contains("RUSTICO") || nombrePestaña.Contains("RÚSTICO") ? 2 : 0);

		//				int filaEncabezadoReal = -1;
		//				int clasePagoMetadato = 0; // 1 = Anual, 2 = Bimestral

		//				// 🚀 3. RADAR DE METADATOS: Escaneamos las primeras 20 filas
		//				for (int i = 0; i < Math.Min(20, tabla.Rows.Count); i++)
		//				{
		//					var filaTemp = tabla.Rows[i];
		//					string textoFilaComplet = string.Join(" ", filaTemp.ItemArray).ToUpper();

		//					// Buscamos palabras clave en todo el ruido del encabezado del municipio
		//					if (textoFilaComplet.Contains("ANUAL")) clasePagoMetadato = 1;
		//					if (textoFilaComplet.Contains("BIMESTRAL")) clasePagoMetadato = 2;
		//					if (tipoPredioPestaña == 0 && textoFilaComplet.Contains("URBANO")) tipoPredioPestaña = 1;

		//					// 🚀 4. ANCLAJE: Detectamos dónde empiezan realmente las columnas
		//					if (textoFilaComplet.Contains("CUENTA") || textoFilaComplet.Contains("PREDIAL") || textoFilaComplet.Contains("CONTRIBUYENTE"))
		//					{
		//						filaEncabezadoReal = i;
		//						break;
		//					}
		//				}

		//				// Si esta pestaña no tiene un encabezado válido (ej. pestaña vacía o de notas), la ignoramos
		//				if (filaEncabezadoReal == -1) continue;

		//				// 🚀 5. MAPEO DINÁMICO: No importa si empieza en 'B' o 'C', leemos los índices reales
		//				var mapaColumnas = MapeadorInteligente.ObtenerMapaColumnas(tabla.Rows[filaEncabezadoReal]);

		//				// 🚀 6. EXTRACCIÓN DE DATOS
		//				for (int i = filaEncabezadoReal + 1; i < tabla.Rows.Count; i++)
		//				{
		//					var fila = tabla.Rows[i];
		//					string validacionFin = string.Join("", fila.ItemArray).ToUpper();

		//					// Condición de paro: Si llegamos a los Totales o a filas completamente vacías
		//					if (string.IsNullOrWhiteSpace(validacionFin) || validacionFin.Contains("TOTAL"))
		//						break;

		//					string cuentaPredial = MapeadorInteligente.ExtraerValor(fila, mapaColumnas, "CUENTA", "PREDIAL");
		//					if (string.IsNullOrWhiteSpace(cuentaPredial)) continue;

		//					var registro = new PadronEstandarDTO
		//					{
		//						CuentaPredial = LimpiadorDatos.LimpiarCuenta(cuentaPredial),
		//						// Asignamos lo que descubrió el Radar (Fallback: 1 Anual / 1 Urbano)
		//						TipoPredio = (byte)(tipoPredioPestaña > 0 ? tipoPredioPestaña : 1),
		//						ClasePago = clasePagoMetadato > 0 ? clasePagoMetadato : 1
		//					};

		//					// Rastrear Bimestres
		//					if (registro.ClasePago == 2)
		//					{
		//						registro.BimestresPagados = MapeadorInteligente.RastrearBimestres(fila, mapaColumnas);
		//					}

		//					listaRegistros.Add(registro);
		//				}
		//			}
		//		}

		//		// Aquí iría tu pase por LimpiadorDatos e inserción en Staging...
		//		respuesta.ProcesadosCorrectamente = listaRegistros.Count;
		//	}
		//	catch (Exception ex)
		//	{
		//		await LogService.WriteLogAsync("ERROR", param.UsuarioLogin, "ProcesadorPagosExcel", $"Fallo crítico: {ex.Message}");
		//		respuesta.Errores = 1;
		//	}

		//	return respuesta;
		//}


		// Estructura idéntica a lo que espera LimpiadorDatos
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

		/// <summary>
		/// Almacena en la base de datos los registros válidos utilizando SqlBulkCopy para eficiencia.
		/// </summary>
		/// <param name="tablaValidos"></param>
		private void InsertarBulk(DataTable tablaValidos)
		{
			using (SqlConnection conn = new SqlConnection(CadenaConexion))
			{
				conn.Open();
				using (SqlBulkCopy bulkCopy = new SqlBulkCopy(conn))
				{
					bulkCopy.DestinationTableName = "pred_Operacion.Staging_Predial";

					// Mapeo Exacto para ignorar el Motivo de Rechazo y otras columnas sueltas
					bulkCopy.ColumnMappings.Add("ClaveMunicipio", "ClaveMunicipio");
					bulkCopy.ColumnMappings.Add("TipoPredio", "TipoPredio");
					bulkCopy.ColumnMappings.Add("CuentaPredial", "CuentaPredial");
					bulkCopy.ColumnMappings.Add("ClasePago", "ClasePago");
					bulkCopy.ColumnMappings.Add("Bimestre", "Bimestre");
					bulkCopy.ColumnMappings.Add("ImpuestoDeterminado", "ImpuestoDeterminado");
					bulkCopy.ColumnMappings.Add("FechaVigencia", "FechaVigencia");
					bulkCopy.ColumnMappings.Add("FolioCarga", "FolioCarga");

					bulkCopy.WriteToServer(tablaValidos);
				}
			}
		}
	}
}