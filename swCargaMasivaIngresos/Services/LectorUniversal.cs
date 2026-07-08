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
		/// Tabla cruda en memoria que contiene los datos leídos del archivo físico (Excel, TXT, CSV). Esta tabla no tiene encabezados definidos y puede contener errores estructurales que deben ser manejados posteriormente.
		/// </summary>
		public DataTable TablaCruda { get; set; }
		/// <summary>
		/// Guarda el contexto de la pestaña o sección del archivo leído, como "ENERO 2026", "URBANO", etc. Este valor se extrae principalmente de archivos Excel y puede ser útil para identificar la fuente o el propósito de los datos leídos.
		/// </summary>
		public string ContextoPestaña { get; set; }

		/// <summary>
		/// Guarda los errores estructurales encontrados durante la lectura del archivo físico, como problemas de formato, delimitadores incorrectos o extensiones no soportadas. Estos errores deben ser revisados antes de proceder con el mapeo y la validación de los datos.
		/// </summary>
		public List<string> ErroresEstructurales { get; set; } = new List<string>();
	}

	/// <summary>
	/// Fase 1 del ETL: Extrae la información de cualquier archivo físico y la convierte en un DataTable en memoria.
	/// </summary>
	public static class LectorUniversal
	{
		/// <summary>
		/// Lee un archivo físico (Excel, TXT, CSV) y devuelve un objeto ResultadoLecturaCruda que contiene la tabla cruda en memoria, el contexto de la pestaña y cualquier error estructural encontrado durante la lectura.
		/// </summary>
		/// <param name="rutaArchivo"></param>
		/// <param name="extension"></param>
		/// <returns></returns>
		public static ResultadoLecturaCruda LeerArchivo(string rutaArchivo, string extension)
		{
			var resultado = new ResultadoLecturaCruda();
			string ext = extension.ToLower().Trim();

			try
			{
				if (ext == ".xlsx" || ext == ".xls")
				{
					resultado = LeerExcel(rutaArchivo);
				}
				else if (ext == ".txt" || ext == ".csv")
				{
					resultado = LeerTextoPlano(rutaArchivo);
				}
				else
				{
					resultado.ErroresEstructurales.Add($"Extensión no soportada: {ext}");
				}
			}
			catch (Exception ex)
			{
				resultado.ErroresEstructurales.Add($"Error fatal al leer el archivo físico: {ex.Message}");
			}

			return resultado;
		}

		/// <summary>
		/// Lee un archivo Excel (.xlsx o .xls) y devuelve un objeto ResultadoLecturaCruda que contiene la tabla cruda en memoria, el contexto de la pestaña y cualquier error estructural encontrado durante la lectura.
		/// </summary>
		/// <param name="rutaArchivo"></param>
		/// <returns></returns>
		private static ResultadoLecturaCruda LeerExcel(string rutaArchivo)
		{
			var resultado = new ResultadoLecturaCruda { TablaCruda = new DataTable() };

			using (var stream = File.Open(rutaArchivo, FileMode.Open, FileAccess.Read))
			using (var reader = ExcelReaderFactory.CreateReader(stream))
			{
				// Extraemos el nombre de la primera pestaña válida como contexto
				resultado.ContextoPestaña = reader.Name?.ToUpper().Trim();

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
					resultado.TablaCruda = result.Tables[0];
				}
				else
				{
					resultado.ErroresEstructurales.Add("El archivo Excel está vacío o no tiene hojas.");
				}
			}
			return resultado;
		}

		/// <summary>
		/// Lee un archivo de texto plano (.txt o .csv) y devuelve un objeto ResultadoLecturaCruda que contiene la tabla cruda en memoria, el contexto de la pestaña y cualquier error estructural encontrado durante la lectura.
		/// </summary>
		/// <param name="rutaArchivo"></param>
		/// <returns></returns>
		private static ResultadoLecturaCruda LeerTextoPlano(string rutaArchivo)
		{
			var resultado = new ResultadoLecturaCruda { TablaCruda = new DataTable() };
			var lineas = new List<string[]>();
			char delimitadorDescubierto = '|'; // Por defecto

			using (var reader = new StreamReader(rutaArchivo, Encoding.UTF8))
			{
				string primeraLinea = reader.ReadLine();
				if (string.IsNullOrWhiteSpace(primeraLinea))
					return resultado; // Archivo vacío

				// 🚀 SOLUCIÓN AL CSV MAL FORMADO:
				// Evaluamos si viene separado por comas, pipes o tabuladores basándonos en la primera línea
				if (primeraLinea.Contains("|")) delimitadorDescubierto = '|';
				else if (primeraLinea.Contains(",")) delimitadorDescubierto = ',';
				else if (primeraLinea.Contains("\t")) delimitadorDescubierto = '\t';

				// Reprocesamos la primera línea
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

			// Normalizamos el número de columnas basado en la fila más ancha encontrada
			int maxColumnas = lineas.Max(l => l.Length);
			for (int i = 0; i < maxColumnas; i++)
			{
				resultado.TablaCruda.Columns.Add($"Columna_{i}");
			}

			// Llenamos el DataTable
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