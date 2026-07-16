using ExcelDataReader;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;

namespace swCargaMasivaIngresos.Services
{
	/// <summary>
	/// Objeto para transportar la tabla cruda y los metadatos descubiertos (ej. Nombre de la pestaña de Excel)
	/// </summary>
	public class ResultadoLecturaCruda
	{
		/// <summary>
		/// Tabla cruda en memoria que contiene los datos leídos del archivo físico (Excel, TXT, CSV).
		/// </summary>
		public DataTable TablaCruda { get; set; }

		/// <summary>
		/// Guarda el contexto de la pestaña o sección del archivo leído, como "ENERO 2026", "URBANO", etc.
		/// </summary>
		public string ContextoPestaña { get; set; }

		/// <summary>
		/// Guarda los errores estructurales encontrados durante la lectura del archivo físico.
		/// </summary>
		public List<string> ErroresEstructurales { get; set; } = new List<string>();
	}

	/// <summary>
	/// Fase 1 del ETL: Extrae la información de cualquier archivo físico y la convierte en DataTables en memoria.
	/// </summary>
	public static class LectorUniversal
	{
		/// <summary>
		/// Lee un archivo físico (Excel, TXT, CSV) y devuelve una LISTA de Resultados (Soporta múltiples pestañas).
		/// </summary>
		/// <param name="rutaArchivo"></param>
		/// <param name="extension"></param>
		/// <returns></returns>
		public static List<ResultadoLecturaCruda> LeerArchivo(string rutaArchivo, string extension)
		{
			var resultados = new List<ResultadoLecturaCruda>();
			string ext = extension.ToLower().Trim();

			try
			{
				if (ext == ".xlsx" || ext == ".xls")
				{
					// 🚀 Devuelve una lista con todas las pestañas válidas
					resultados = LeerExcel(rutaArchivo);
				}
				else if (ext == ".txt" || ext == ".csv")
				{
					// Los TXT/CSV solo tienen "una pestaña", así que lo agregamos como elemento único
					resultados.Add(LeerTextoPlano(rutaArchivo));
				}
				else
				{
					var resError = new ResultadoLecturaCruda();
					resError.ErroresEstructurales.Add($"Extensión no soportada: {ext}");
					resultados.Add(resError);
				}
			}
			catch (Exception ex)
			{
				var resError = new ResultadoLecturaCruda();
				resError.ErroresEstructurales.Add($"Error fatal al leer el archivo físico: {ex.Message}");
				resultados.Add(resError);
			}

			return resultados;
		}

		/// <summary>
		/// Lee un archivo Excel (.xlsx o .xls) iterando por todas sus hojas (pestañas).
		/// </summary>
		/// <param name="rutaArchivo"></param>
		/// <returns></returns>
		private static List<ResultadoLecturaCruda> LeerExcel(string rutaArchivo)
		{
			var listaResultados = new List<ResultadoLecturaCruda>();

			using (var stream = File.Open(rutaArchivo, FileMode.Open, FileAccess.Read))
			using (var reader = ExcelReaderFactory.CreateReader(stream))
			{
				// Convertimos todo el Excel a un DataSet crudo
				var result = reader.AsDataSet(new ExcelDataSetConfiguration()
				{
					ConfigureDataTable = (_) => new ExcelDataTableConfiguration()
					{
						UseHeaderRow = false // Lo leemos sin encabezados, el Mapeador buscará dónde empiezan realmente
					}
				});

				if (result.Tables.Count > 0)
				{
					// 🚀 ITERAMOS POR CADA PESTAÑA DEL EXCEL
					foreach (DataTable tabla in result.Tables)
					{
						// Ignoramos pestañas que el usuario haya dejado completamente en blanco
						if (tabla.Rows.Count == 0) continue;

						listaResultados.Add(new ResultadoLecturaCruda
						{
							TablaCruda = tabla,
							// Extraemos el nombre real de ESTA pestaña en específico
							ContextoPestaña = tabla.TableName?.ToUpper().Trim()
						});
					}
				}

				// Si por alguna razón todas las pestañas estaban vacías
				if (listaResultados.Count == 0)
				{
					var resError = new ResultadoLecturaCruda();
					resError.ErroresEstructurales.Add("El archivo Excel está vacío o no tiene hojas con datos.");
					listaResultados.Add(resError);
				}
			}

			return listaResultados;
		}

		/// <summary>
		/// Lee un archivo de texto plano (.txt o .csv) con detección automática de delimitadores.
		/// </summary>
		/// <param name="rutaArchivo"></param>
		/// <returns></returns>
		private static ResultadoLecturaCruda LeerTextoPlano(string rutaArchivo)
		{
			var resultado = new ResultadoLecturaCruda { TablaCruda = new DataTable(), ContextoPestaña = "TXT_CSV" };
			var lineas = new List<string[]>();
			char delimitadorDescubierto = '|'; // Por defecto

			using (var reader = new StreamReader(rutaArchivo, Encoding.UTF8))
			{
				string primeraLinea = null;
				while ((primeraLinea = reader.ReadLine()) != null)
				{
					if (!string.IsNullOrWhiteSpace(primeraLinea)) break;
				}

				if (string.IsNullOrWhiteSpace(primeraLinea))
					return resultado;

				if (primeraLinea.Contains("|")) delimitadorDescubierto = '|';
				else if (primeraLinea.Contains(",")) delimitadorDescubierto = ',';
				else if (primeraLinea.Contains("\t")) delimitadorDescubierto = '\t';

				lineas.Add(primeraLinea.Split(delimitadorDescubierto));

				string lineaActual;
				while ((lineaActual = reader.ReadLine()) != null)
				{
					if (!string.IsNullOrWhiteSpace(lineaActual))
					{
						lineas.Add(lineaActual.Split(delimitadorDescubierto));
					}
				}
			}

			int maxColumnas = lineas.Max(l => l.Length);
			for (int i = 0; i < maxColumnas; i++)
			{
				resultado.TablaCruda.Columns.Add($"Columna_{i}");
			}

			foreach (var filaArr in lineas)
			{
				var row = resultado.TablaCruda.NewRow();
				for (int i = 0; i < filaArr.Length; i++)
				{
					row[i] = filaArr[i]?.Trim();
				}
				resultado.TablaCruda.Rows.Add(row);
			}

			return resultado;
		}
	}
}