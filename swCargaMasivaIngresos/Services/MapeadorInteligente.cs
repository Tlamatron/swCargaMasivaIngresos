using System;
using System.Collections.Generic;
using System.Data;

namespace swCargaMasivaIngresos.Services
{
	/// <summary>
	/// Realiza el mapeo inteligente de columnas en archivos de datos, permitiendo la extracción dinámica de valores y la estandarización de tablas para su posterior procesamiento.
	/// </summary>
	public static class MapeadorInteligente
	{
		/// <summary>
		/// 1. ESCÁNER ESTÁNDAR (Para archivos TXT o Excels sencillos de 1 sola fila)
		/// </summary>
		/// <param name="filaEncabezado"></param>
		/// <returns></returns>
		public static Dictionary<string, int> ObtenerMapaColumnas(DataRow filaEncabezado)
		{
			var mapa = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
			for (int i = 0; i < filaEncabezado.ItemArray.Length; i++)
			{
				string nombreColumna = filaEncabezado[i]?.ToString().Trim().ToUpper();
				if (!string.IsNullOrWhiteSpace(nombreColumna))
				{
					// Limpiamos saltos de línea (Alt+Enter)
					nombreColumna = nombreColumna.Replace("\r", " ").Replace("\n", " ").Replace("  ", " ");
					if (!mapa.ContainsKey(nombreColumna)) mapa.Add(nombreColumna, i);
				}
			}
			return mapa;
		}

		/// <summary>
		/// 2. ESCÁNER MULTI-FILA (Para formatos complejos tipo Ayotoxco con sub-cabeceras)
		/// </summary>
		/// <param name="tabla"></param>
		/// <param name="indiceFilaInicio"></param>
		/// <param name="numFilas"></param>
		/// <returns></returns>
		public static Dictionary<string, int> ObtenerMapaColumnasMultiFila(DataTable tabla, int indiceFilaInicio, int numFilas)
		{
			var mapa = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
			int cols = tabla.Columns.Count;

			for (int c = 0; c < cols; c++)
			{
				// Leemos hacia abajo para atrapar sub-encabezados (Ej. 1, 2, 3, SALDO)
				for (int f = 0; f < numFilas && (indiceFilaInicio + f) < tabla.Rows.Count; f++)
				{
					string nombreColumna = tabla.Rows[indiceFilaInicio + f][c]?.ToString().Trim().ToUpper();
					if (!string.IsNullOrWhiteSpace(nombreColumna))
					{
						nombreColumna = nombreColumna.Replace("\r", " ").Replace("\n", " ").Replace("  ", " ");
						if (!mapa.ContainsKey(nombreColumna)) mapa.Add(nombreColumna, c);
					}
				}
			}
			return mapa;
		}

		/// <summary>
		/// 3. EXTRACCIÓN DINÁMICA POR SINÓNIMOS
		/// </summary>
		/// <param name="fila"></param>
		/// <param name="mapa"></param>
		/// <param name="posiblesNombres"></param>
		/// <returns></returns>
		public static string ExtraerValor(DataRow fila, Dictionary<string, int> mapa, params string[] posiblesNombres)
		{
			foreach (var nombre in posiblesNombres)
			{
				foreach (var key in mapa.Keys)
				{
					if (key.Contains(nombre))
					{
						int indice = mapa[key];
						return fila[indice]?.ToString().Trim();
					}
				}
			}
			return string.Empty;
		}

		/// <summary>
		/// 4. RASTREO ESPECIALIZADO DE BIMESTRES
		/// </summary>
		/// <param name="fila"></param>
		/// <param name="mapa"></param>
		/// <returns></returns>
		public static string RastrearBimestres(DataRow fila, Dictionary<string, int> mapa)
		{
			List<string> bimestresDetectados = new List<string>();
			string[] columnasBimestrales = { "1", "2", "3", "4", "5", "6", "B1", "B2", "B3", "B4", "B5", "B6" };

			foreach (var col in columnasBimestrales)
			{
				if (mapa.ContainsKey(col))
				{
					int indice = mapa[col];
					string valorCelda = fila[indice]?.ToString().Trim().ToUpper();

					// Si pagó algo en esa celda
					if (!string.IsNullOrWhiteSpace(valorCelda) && valorCelda != "0" && valorCelda != "0.00")
					{
						bimestresDetectados.Add(col.Replace("B", ""));
					}
				}
			}

			if (bimestresDetectados.Count > 0) return string.Join(",", bimestresDetectados);

			// Fallback: Si no venían separados, buscamos una columna concentradora
			return ExtraerValor(fila, mapa, "BIMESTRE", "PERIODO", "MESES");
		}

		/// <summary>
		/// 5. ESTANDARIZADOR DE TABLAS (Utilizado por ProcesadorPagosUniversal)
		/// Toma una tabla con columnas crudas y las mapea hacia la estructura estándar de Staging
		/// </summary>
		public static DataTable EstandarizarTabla(DataTable tablaCruda, Dictionary<string, int> mapaColumnas, int indiceInicioDatos = 0)
		{
			DataTable tablaEstandar = new DataTable();

			// 1. Creamos la estructura idéntica a lo que espera LimpiadorDatos
			tablaEstandar.Columns.Add("ClaveMunicipio", typeof(string));
			tablaEstandar.Columns.Add("TipoPredio", typeof(string));
			tablaEstandar.Columns.Add("CuentaPredial", typeof(string));
			tablaEstandar.Columns.Add("ClasePago", typeof(string));
			tablaEstandar.Columns.Add("Bimestre", typeof(string));
			tablaEstandar.Columns.Add("ImpuestoDeterminado", typeof(string));
			tablaEstandar.Columns.Add("FechaVigencia", typeof(string));
			tablaEstandar.Columns.Add("FolioCarga", typeof(string));

			// 2. Iteramos sobre los datos crudos
			for (int i = indiceInicioDatos; i < tablaCruda.Rows.Count; i++)
			{
				var fila = tablaCruda.Rows[i];

				// Paro Seguro: Fila vacía o Totales
				string validacionVacia = string.Join("", fila.ItemArray).Trim().ToUpper();
				if (string.IsNullOrWhiteSpace(validacionVacia) || validacionVacia.Contains("TOTAL"))
					continue;

				// Extraemos la cuenta (Si no hay cuenta, no es un registro válido)
				string cuenta = ExtraerValor(fila, mapaColumnas, "CUENTA", "PREDIAL", "CTA", "CTA.", "CLAVE");
				if (string.IsNullOrWhiteSpace(cuenta) || cuenta.Equals("Cuenta", StringComparison.OrdinalIgnoreCase))
					continue;
				string anioPredial = MapeadorInteligente.ExtraerValor(fila, mapaColumnas, "AÑO", "EJERCICIO", "AÑO PREDIAL");
				// Si la columna de Año existe, pero el valor NO es 2026, ignoramos a esta persona
				if (!string.IsNullOrWhiteSpace(anioPredial) && !anioPredial.Contains("2026"))
				{
					continue;
				}

				// 3. Ensamblamos la fila limpia usando el extractor dinámico
				DataRow nuevaFila = tablaEstandar.NewRow();
				nuevaFila["ClaveMunicipio"] = ExtraerValor(fila, mapaColumnas, "CLAVE DEL MUNICIPIO", "MUNICIPIO", "CVEMUN");
				nuevaFila["TipoPredio"] = ExtraerValor(fila, mapaColumnas, "TIPO DE PREDIO", "PREDIO", "TIPO");
				nuevaFila["CuentaPredial"] = cuenta;
				nuevaFila["ClasePago"] = ExtraerValor(fila, mapaColumnas, "CLASE DE PAGO", "CLASE");
				nuevaFila["Bimestre"] = RastrearBimestres(fila, mapaColumnas);
				nuevaFila["ImpuestoDeterminado"] = ExtraerValor(fila, mapaColumnas, "SALDO", "TOTAL", "2026", "PAGO", "IMPUESTO", "IMPORTE", "MONTO");
				nuevaFila["FechaVigencia"] = ExtraerValor(fila, mapaColumnas, "FECHA", "VIGENCIA", "DIA", "DÍA");

				tablaEstandar.Rows.Add(nuevaFila);
			}

			return tablaEstandar;
		}
	}
}