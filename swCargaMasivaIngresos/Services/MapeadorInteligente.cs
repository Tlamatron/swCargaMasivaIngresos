using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;

namespace swCargaMasivaIngresos.Services
{
	public static class MapeadorInteligente
	{
		public class MapaOficial
		{
			public Dictionary<string, int> Columnas { get; set; } = new Dictionary<string, int>();
			public Dictionary<string, int> BimestresSueltos { get; set; } = new Dictionary<string, int>();
		}

		// =========================================================================================
		// 🚀 LA IDEA DEL USUARIO: EXTRACCIÓN VERTICAL POR REGIONES (Bounding Box)
		// =========================================================================================
		public static Dictionary<string, int> ObtenerMapaPorRegiones(DataTable tabla, out int filaInicioDatos)
		{
			filaInicioDatos = -1;
			int filaEncabezado = -1;

			// 1. ZONA A: Encontrar dónde empiezan los títulos reales (saltando logos)
			for (int i = 0; i < Math.Min(50, tabla.Rows.Count); i++)
			{
				var celdas = tabla.Rows[i].ItemArray.Select(x => x?.ToString().Trim() ?? "").Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
				string texto = string.Join(" ", celdas).ToUpper();

				bool tieneIdentificador = texto.Contains("CUENTA") || texto.Contains("PREDIAL") || texto.Contains("CTA");
				bool tieneContexto = texto.Contains("MUNICIPIO") || texto.Contains("TIPO") || texto.Contains("PAGO") || texto.Contains("IMPORTE") || texto.Contains("TOTAL") || texto.Contains("FECHA") || texto.Contains("NOMBRE") || texto.Contains("CLAVE");

				if (celdas.Count >= 3 && tieneIdentificador && tieneContexto)
				{
					filaEncabezado = i;
					break;
				}
			}

			if (filaEncabezado == -1) return new Dictionary<string, int>();

			// 2. ZONA B: Encontrar dónde terminan los títulos y empiezan los datos de los ciudadanos
			filaInicioDatos = filaEncabezado + 1;
			for (int i = filaEncabezado + 1; i < Math.Min(filaEncabezado + 10, tabla.Rows.Count); i++)
			{
				var celdas = tabla.Rows[i].ItemArray.Select(x => x?.ToString().Trim() ?? "").ToList();
				string textoUnido = string.Join("", celdas).ToUpper();

				if (string.IsNullOrWhiteSpace(textoUnido)) continue; // Saltamos filas vacías de decoración

				// Si la fila tiene textos clásicos de sub-encabezados, seguimos bajando
				if (textoUnido.Contains("BIMESTRAL") || textoUnido.Contains("RUSTICO") || textoUnido.Contains("URBANO") || textoUnido.Contains("ANUAL") || textoUnido.Contains("BIMESTRE") || textoUnido.Contains("SUBURBANO") || textoUnido.Contains("PREDIO"))
				{
					continue;
				}

				// Si no tiene textos de títulos y llegamos a una fila llena, ¡Aquí inician los datos!
				filaInicioDatos = i;
				break;
			}

			// 3. ZONA C: Extracción Vertical (Como en el diagrama). Desagrupamos y fusionamos hacia abajo.
			var mapaCrudo = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
			for (int c = 0; c < tabla.Columns.Count; c++)
			{
				List<string> partes = new List<string>();
				// Leemos desde el primer título HASTA antes de que empiecen los datos
				for (int r = filaEncabezado; r < filaInicioDatos; r++)
				{
					string val = tabla.Rows[r][c]?.ToString().Trim().ToUpper();
					if (!string.IsNullOrWhiteSpace(val)) partes.Add(val);
				}

				if (partes.Count > 0)
				{
					string colName = string.Join(" ", partes).Replace("\r", " ").Replace("\n", " ").Replace("  ", " ");
					if (!mapaCrudo.ContainsKey(colName)) mapaCrudo[colName] = c;
				}
			}

			return mapaCrudo;
		}

		public static MapaOficial ProcesarEncabezadosConMemoria(Dictionary<string, int> mapaCrudo)
		{
			var oficial = new MapaOficial();
			var columnasUsadas = new HashSet<int>();

			void Asignar(string nombreOficial, params string[] sinonimos)
			{
				foreach (var sin in sinonimos)
				{
					foreach (var kvp in mapaCrudo)
					{
						if (kvp.Key.Contains(sin) && !columnasUsadas.Contains(kvp.Value))
						{
							oficial.Columnas[nombreOficial] = kvp.Value;
							columnasUsadas.Add(kvp.Value);
							return;
						}
					}
				}
			}

			Asignar("CuentaPredial", "CUENTA", "PREDIAL", "CTA", "CTA.", "CLAVE");
			Asignar("Anio", "AÑO", "EJERCICIO");
			Asignar("ImpuestoDeterminado", "SALDO", "TOTAL", "PAGO", "IMPUESTO", "IMPORTE");
			Asignar("FechaVigencia", "FECHA", "VIGENCIA");
			Asignar("ClaveMunicipio", "CLAVE DEL MUNICIPIO", "MUNICIPIO", "CVEMUN");
			Asignar("TipoPredio", "TIPO DE PREDIO", "PREDIO", "TIPO");
			Asignar("ClasePago", "CLASE DE PAGO", "CLASE");

			Asignar("NombrePropietario", "NOMBRE", "PROPIETARIO", "CONTRIBUYENTE");
			Asignar("BaseGravable", "BASE GRAVABLE", "BASE", "VALOR CATASTRAL");
			Asignar("BimestreConsolidado", "BIMESTRE", "PERIODO", "MESES");

			string[] columnasBimestrales = { "1", "2", "3", "4", "5", "6", "B1", "B2", "B3", "B4", "B5", "B6" };
			foreach (var col in columnasBimestrales)
			{
				if (mapaCrudo.ContainsKey(col) && !columnasUsadas.Contains(mapaCrudo[col]))
				{
					oficial.BimestresSueltos[col] = mapaCrudo[col];
					columnasUsadas.Add(mapaCrudo[col]);
				}
			}

			return oficial;
		}

		public static string Extraer(DataRow fila, MapaOficial mapa, string columna)
		{
			if (mapa.Columnas.ContainsKey(columna))
			{
				return fila[mapa.Columnas[columna]]?.ToString().Trim() ?? string.Empty;
			}
			return string.Empty;
		}

		public static string RastrearBimestres(DataRow fila, MapaOficial mapa)
		{
			List<string> detectados = new List<string>();
			foreach (var kvp in mapa.BimestresSueltos)
			{
				string valor = fila[kvp.Value]?.ToString().Trim().ToUpper() ?? "";
				if (!string.IsNullOrWhiteSpace(valor) && valor != "0" && valor != "0.00")
				{
					detectados.Add(kvp.Key.Replace("B", ""));
				}
			}

			if (detectados.Count > 0) return string.Join(",", detectados);
			return Extraer(fila, mapa, "BimestreConsolidado");
		}

		// Mantenemos estos dos para que no se rompa tu procesador TXT (ProcesadorPagosUniversal)
		public static Dictionary<string, int> ObtenerMapaColumnasMultiFila(DataTable tabla, int indiceFilaInicio, int numFilas) { return new Dictionary<string, int>(); /* Omitido por brevedad, no lo borres de tu código si lo tienes */ }
		public static DataTable EstandarizarTabla(DataTable tablaCruda, Dictionary<string, int> mapaCrudo, int indiceInicioDatos = 0) { return new DataTable(); /* Omitido por brevedad, no lo borres */ }
	}
}