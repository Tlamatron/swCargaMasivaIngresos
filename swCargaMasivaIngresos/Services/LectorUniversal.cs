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
		public DataTable TablaCruda { get; set; }
		public string ContextoPestaña { get; set; } // Guardará "ENERO 2026", "URBANO", etc.
		public List<string> ErroresEstructurales { get; set; } = new List<string>();
	}

	/// <summary>
	/// Fase 1 del ETL: Extrae la información de cualquier archivo físico y la convierte en un DataTable en memoria.
	/// </summary>
	public static class LectorUniversal
	{
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