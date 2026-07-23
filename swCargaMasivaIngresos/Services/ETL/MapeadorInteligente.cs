using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;

namespace swCargaMasivaIngresos.Services
{
	/// <summary>
	/// Mapeador inteligente que analiza un DataTable en memoria y genera un mapeo de columnas basado en encabezados encontrados, incluyendo la detección de bimestres sueltos y metadatos compuestos. Este mapeador permite extraer datos de manera flexible y robusta, incluso cuando los encabezados están distribuidos en varias filas o contienen sinónimos.
	/// </summary>
	public static class MapeadorInteligente
	{
		/// <summary>
		/// Contiene el mapeo oficial de columnas y bimestres sueltos, donde las claves son los nombres oficiales y los valores son los índices de columna correspondientes en el DataTable. Este objeto se utiliza para extraer datos de manera consistente y evitar conflictos entre sinónimos o encabezados similares.
		/// </summary>
		public class MapaOficial
		{
			/// <summary>
			/// Contiene el mapeo de columnas oficiales, donde la clave es el nombre oficial de la columna (ej. "CuentaPredial", "Anio", "ImpuestoDeterminado") y el valor es el índice de columna correspondiente en el DataTable. Este diccionario permite acceder a los datos de manera consistente y evita errores al referirse a columnas inexistentes.
			/// </summary>
			public Dictionary<string, int> Columnas { get; set; } = new Dictionary<string, int>();
			/// <summary>
			/// Contiene el mapeo de bimestres sueltos, donde la clave es el nombre del bimestre (ej. "1", "2", "3", "B1", "B2") y el valor es el índice de columna correspondiente en el DataTable. Este diccionario permite rastrear los bimestres detectados en los datos y facilita la extracción de información relacionada con pagos bimestrales.
			/// </summary>
			public Dictionary<string, int> BimestresSueltos { get; set; } = new Dictionary<string, int>();
		}


		/// <summary>
		/// Extrae un mapeo de columnas basado en encabezados encontrados en un DataTable, utilizando un enfoque de extracción vertical por regiones. 
		/// Utiliza el concepto de "Caja Delimitadora" (Bounding Box) para encontrar el Límite Superior (Zona A) y el Límite Inferior (Zona B).
		/// </summary>
		public static Dictionary<string, int> ObtenerMapaPorRegiones(DataTable tabla, out int filaInicioDatos)
		{
			filaInicioDatos = -1;
			int filaEncabezado = -1;

			// 🚀 HACK: Disfrazamos el Trace de WARN para que LogService lo imprima sí o sí
			LogService.WriteLogAsync("WARN", "SISTEMA_DEBUG", "Mapeador", $"[TRACE] === ESCANEANDO PESTAÑA: {tabla.TableName} | Filas Totales: {tabla.Rows.Count} ===").Wait();

			// ==========================================================================================
			// 1. ZONA A: Búsqueda Heurística por Ponderación Estructural (El Límite Superior)
			// Buscamos dónde empieza realmente la tabla de datos, ignorando membretes genéricos.
			// ==========================================================================================
			int mejorPuntaje = -1;
			int filaCandidata = -1;

			for (int i = 0; i < Math.Min(50, tabla.Rows.Count); i++)
			{
				var celdas = tabla.Rows[i].ItemArray.Select(x => x?.ToString().Trim() ?? "").ToList();
				int celdasLlenas = celdas.Count(x => !string.IsNullOrWhiteSpace(x));

				// Si la fila está vacía, no tiene caso analizarla
				if (celdasLlenas == 0) continue;

				string textoFila = string.Join(" ", celdas).ToUpper();
				int puntajeFila = 0;

				// 📊 CRITERIO 1: Densidad Horizontal
				puntajeFila += celdasLlenas;

				// 📊 CRITERIO 2: Palabras Clave Core (Identificadores únicos de negocio)
				if (textoFila.Contains("CUENTA") || textoFila.Contains("CTA")) puntajeFila += 10;
				if (textoFila.Contains("PREDIO") || textoFila.Contains("PREDIAL")) puntajeFila += 10;
				if (textoFila.Contains("BIMESTRE")) puntajeFila += 10;
				if (textoFila.Contains("CLASE")) puntajeFila += 10;
				if (textoFila.Contains("IMPUESTO") || textoFila.Contains("REDUCCION") || textoFila.Contains("IMPORTE")) puntajeFila += 10;

				// 📊 CRITERIO 3: Palabras Clave Suaves
				if (textoFila.Contains("MUNICIPIO")) puntajeFila += 3;
				if (textoFila.Contains("FECHA")) puntajeFila += 3;
				if (textoFila.Contains("NOMBRE")) puntajeFila += 3;

				// 📊 CRITERIO 4: FILTRO DEFENSIVO (Discriminador de Datos vs. Texto)
				int numerosAqui = celdas.Count(c => decimal.TryParse(c, out _));
				if (celdasLlenas > 0 && numerosAqui > (celdasLlenas / 2.0))
				{
					puntajeFila -= 100; // Descalificación inmediata, estos son datos, no un título.
				}

				// 📊 CRITERIO 5: Escaneo Profundo para encabezados agrupados (Ecosistema Inferior)
				bool encontroDatosAbajo = false;
				for (int step = 1; step <= 2; step++) // Buscamos hasta 2 filas abajo para salvar las celdas fusionadas
				{
					if (i + step < tabla.Rows.Count)
					{
						var celdasAbajo = tabla.Rows[i + step].ItemArray.Select(x => x?.ToString().Trim() ?? "").ToList();
						int celdasLlenasAbajo = celdasAbajo.Count(x => !string.IsNullOrWhiteSpace(x));
						int numerosAbajo = celdasAbajo.Count(c => decimal.TryParse(c, out _));

						// Si abajo hay puros números, esta fila actual es el título contenedor.
						if (celdasLlenasAbajo > 0 && numerosAbajo > (celdasLlenasAbajo / 2.0))
						{
							puntajeFila += 15;
							encontroDatosAbajo = true;
							break;
						}
					}
				}

				// 🏆 EVALUACIÓN DEL GANADOR
				if (puntajeFila > mejorPuntaje && puntajeFila >= 15)
				{
					mejorPuntaje = puntajeFila;
					filaCandidata = i;
				}
			}

			// DETERMINACIÓN FINAL DEL LÍMITE SUPERIOR
			if (filaCandidata != -1)
			{
				filaEncabezado = filaCandidata;
				LogService.WriteLogAsync("WARN", "SISTEMA_DEBUG", "Mapeador", $"[TRACE] -> ¡ANCLADO! Ganador indiscutible Fila {filaEncabezado} con {mejorPuntaje} puntos.").Wait();
			}
			else
			{
				LogService.WriteLogAsync("WARN", "SISTEMA_DEBUG", "Mapeador", "[TRACE] -> FRACASO: Ninguna fila superó el umbral. Abortando pestaña.").Wait();
				return new Dictionary<string, int>();
			}

			// ==========================================================================================
			// 2. ZONA B: Encontrar inicio de datos (La "Caja Delimitadora" / Límite Inferior)
			// Aquí el código aprende a distinguir cuándo terminaron los títulos múltiples y empezó el padrón.
			// ==========================================================================================
			filaInicioDatos = filaEncabezado + 1;
			for (int i = filaEncabezado + 1; i < Math.Min(filaEncabezado + 10, tabla.Rows.Count); i++)
			{
				var celdas = tabla.Rows[i].ItemArray.Select(x => x?.ToString().Trim().ToUpper() ?? "").ToList();
				string textoUnido = string.Join("", celdas);

				if (string.IsNullOrWhiteSpace(textoUnido)) continue;

				// 🚀 1. REGLA LÉXICA: Palabras que obligan a empujar el límite hacia abajo (Siguen siendo títulos)
				if (textoUnido.Contains("TIPO DE PREDIO") || textoUnido.Contains("BIMESTRE") ||
					textoUnido.Contains("CLASE DE PAGO") || textoUnido.Contains("CLAVE DEL MUNICIPIO") ||
					textoUnido.Contains("=") || textoUnido.Contains("TOTAL") || textoUnido.Contains("SALDO"))
				{
					continue;
				}

				// 🚀 2. REGLA NUMÉRICA: Discriminador inteligente de números de título vs. números de dinero
				var numeros = celdas.Where(c => decimal.TryParse(c.Replace("$", "").Replace(",", ""), out _)).ToList();

				if (numeros.Count >= 3)
				{
					// Validamos si es el sub-encabezado de bimestres (1, 2, 3...) y lo perdonamos
					bool esFilaBimestres = numeros.All(n => n == "1" || n == "2" || n == "3" || n == "4" || n == "5" || n == "6" || n == "1.0" || n == "2.0" || n == "3.0");

					bool esFilaAnios = numeros.All(n => {
						if (int.TryParse(n.Replace(".0", "").Trim(), out int num))
						{
							return num >= 1990 && num <= 2050; // Si todos los números son años, es un título
						}
						return false;
					});

					if (esFilaBimestres || esFilaAnios)
					{
						LogService.WriteLogAsync("WARN", "SISTEMA_DEBUG", "Mapeador", $"[TRACE] Fila {i} salvada. Es el sub-encabezado Bimestral/Anual.").Wait();
						continue;
					}

					// Si son números mezclados (ej. montos, cuentas largas), ES LA TABLA.
					filaInicioDatos = i;
					LogService.WriteLogAsync("WARN", "SISTEMA_DEBUG", "Mapeador", $"[TRACE] -> Inicio de datos fijado en Fila {i}").Wait();
					break;
				}

				// 🚀 3. REGLA DE SEGURIDAD EXTREMA: El Símbolo de Dinero
				if (celdas.Any(c => c.Contains("$")))
				{
					filaInicioDatos = i;
					LogService.WriteLogAsync("WARN", "SISTEMA_DEBUG", "Mapeador", $"[TRACE] -> Inicio de datos fijado en Fila {i} (Símbolo $ detectado)").Wait();
					break;
				}
			}

			// ==========================================================================================
			// 3. ZONA C: Extracción Vertical (Fusión de la Caja)
			// Ya que sabemos dónde empieza (Zona A) y dónde termina (Zona B), fusionamos los textos de arriba hacia abajo.
			// ==========================================================================================
			var mapaCrudo = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
			for (int c = 0; c < tabla.Columns.Count; c++)
			{
				List<string> partes = new List<string>();
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

			LogService.WriteLogAsync("WARN", "SISTEMA_DEBUG", "Mapeador", $"[TRACE] Mapa extraído: {string.Join(", ", mapaCrudo.Keys)}").Wait();
			return mapaCrudo;
		}

		/// <summary>
		/// Extrae un mapeo de columnas basado en encabezados encontrados en un DataTable, utilizando un enfoque de extracción vertical por regiones. Este método identifica la fila de encabezado y la fila de inicio de datos, y luego construye un diccionario que asocia los nombres de columna con sus índices correspondientes.
		/// </summary>
		/// <param name="tabla"></param>
		/// <param name="filaInicioDatos"></param>
		/// <returns></returns>
		public static Dictionary<string, int> ObtenerMapaPorRegiones_v01(DataTable tabla, out int filaInicioDatos)
		{
			filaInicioDatos = -1;
			int filaEncabezado = -1;

			// 🚀 HACK: Disfrazamos el Trace de WARN para que LogService lo imprima sí o sí
			LogService.WriteLogAsync("WARN", "SISTEMA_DEBUG", "Mapeador", $"[TRACE] === ESCANEANDO PESTAÑA: {tabla.TableName} | Filas Totales: {tabla.Rows.Count} ===").Wait();

			//// 1. ZONA A: Encontrar dónde empiezan los títulos reales
			//for (int i = 0; i < Math.Min(50, tabla.Rows.Count); i++)
			//{
			//	var celdas = tabla.Rows[i].ItemArray.Select(x => x?.ToString().Trim() ?? "").Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
			//	string texto = string.Join(" ", celdas).ToUpper();

			//	bool tieneContexto = texto.Contains("CLAVE") || texto.Contains("MUNICIPIO") || texto.Contains("CUENTA") || texto.Contains("PREDIAL") || texto.Contains("FECHA");

			//	// Imprimimos todo lo que tenga al menos 1 celda llena
			//	if (celdas.Count > 0)
			//	{
			//		LogService.WriteLogAsync("WARN", "SISTEMA_DEBUG", "Mapeador", $"[TRACE] Fila {i} | Celdas Llenas: {celdas.Count} | Contexto: {tieneContexto} | Texto: {texto}").Wait();
			//	}

			//	if (celdas.Count >= 2 && tieneContexto)
			//	{
			//		filaEncabezado = i;
			//		LogService.WriteLogAsync("WARN", "SISTEMA_DEBUG", "Mapeador", $"[TRACE] -> ¡ANCLADO! Encabezado inicia en Fila {i}").Wait();
			//		break;
			//	}
			//}

			//if (filaEncabezado == -1)
			//{
			//	LogService.WriteLogAsync("WARN", "SISTEMA_DEBUG", "Mapeador", "[TRACE] -> FRACASO: No se encontró la fila de encabezados. Abortando pestaña.").Wait();
			//	return new Dictionary<string, int>();
			//}

			// 1. ZONA A: Búsqueda Heurística por Ponderación Estructural (El "Escáner")
			int mejorPuntaje = -1;
			int filaCandidata = -1;

			LogService.WriteLogAsync("WARN", "SISTEMA_DEBUG", "Mapeador", $"[TRACE] === ESCANEANDO PESTAÑA: {tabla.TableName} | Filas Totales: {tabla.Rows.Count} ===").Wait();
			// 1. ZONA A: Búsqueda Heurística por Ponderación Estructural (El "Escáner")
			
			for (int i = 0; i < Math.Min(50, tabla.Rows.Count); i++)
			{
				var celdas = tabla.Rows[i].ItemArray.Select(x => x?.ToString().Trim() ?? "").ToList();
				int celdasLlenas = celdas.Count(x => !string.IsNullOrWhiteSpace(x));

				// Si la fila está vacía, no tiene caso analizarla
				if (celdasLlenas == 0) continue;

				string textoFila = string.Join(" ", celdas).ToUpper();
				int puntajeFila = 0;

				// 📊 CRITERIO 1: Densidad Horizontal (Aporta balance según el ancho de la tabla)
				puntajeFila += celdasLlenas;

				// 📊 CRITERIO 2: Palabras Clave Core (Peso pesado - Identificadores únicos de negocio)
				if (textoFila.Contains("CUENTA") || textoFila.Contains("CTA")) puntajeFila += 10;
				if (textoFila.Contains("PREDIO") || textoFila.Contains("PREDIAL")) puntajeFila += 10;
				if (textoFila.Contains("BIMESTRE")) puntajeFila += 10;
				if (textoFila.Contains("CLASE")) puntajeFila += 10;
				if (textoFila.Contains("IMPUESTO") || textoFila.Contains("REDUCCION") || textoFila.Contains("IMPORTE")) puntajeFila += 10;

				// 📊 CRITERIO 3: Palabras Clave Suaves (Peso medio - Contexto general)
				if (textoFila.Contains("MUNICIPIO")) puntajeFila += 3;
				if (textoFila.Contains("FECHA")) puntajeFila += 3;
				if (textoFila.Contains("NOMBRE")) puntajeFila += 3;

				// 📊 CRITERIO 4: FILTRO DEFENSIVO DE CONTENIDO (Discriminador de Datos vs. Texto)
				// Contamos cuántas celdas son numéricas. Si representan más del 50% de las celdas llenas,
				// significa que el renglón esminentemente numérico (un registro de cobro/padrón) y no un encabezado.
				int numerosAqui = celdas.Count(c => decimal.TryParse(c, out _));
				if (celdasLlenas > 0 && numerosAqui > (celdasLlenas / 2.0))
				{
					puntajeFila -= 100; // Descalificación inmediata
				}

				// 📊 CRITERIO 5: Validación del Ecosistema Inferior (Escaneo profundo para encabezados agrupados)
				bool encontroDatosAbajo = false;
				for (int step = 1; step <= 2; step++) // Buscamos en la fila inmediatamente abajo, o en la siguiente
				{
					if (i + step < tabla.Rows.Count)
					{
						var celdasAbajo = tabla.Rows[i + step].ItemArray.Select(x => x?.ToString().Trim() ?? "").ToList();
						int celdasLlenasAbajo = celdasAbajo.Count(x => !string.IsNullOrWhiteSpace(x));
						int numerosAbajo = celdasAbajo.Count(c => decimal.TryParse(c, out _));

						// Si la fila de abajo está compuesta en su mayoría por números (>50%), es el inicio de datos.
						if (celdasLlenasAbajo > 0 && numerosAbajo > (celdasLlenasAbajo / 2.0))
						{
							puntajeFila += 15;
							encontroDatosAbajo = true;
							break; // Si ya encontró los datos, otorgamos los puntos y dejamos de buscar
						}
					}
				}
				//if (i + 1 < tabla.Rows.Count)
				//{
				//	var celdasAbajo = tabla.Rows[i + 1].ItemArray.Select(x => x?.ToString().Trim() ?? "").ToList();
				//	int numerosAbajo = celdasAbajo.Count(c => decimal.TryParse(c, out _));

				//	// Si la fila de abajo está compuesta en su mayoría por números (>50%), 
				//	// incrementa drásticamente la probabilidad de que la fila actual sea el encabezado contenedor.
				//	if (celdasAbajo.Count > 0 && numerosAbajo > (celdasAbajo.Count(x => !string.IsNullOrWhiteSpace(x)) / 2.0))
				//	{
				//		puntajeFila += 15;
				//	}
				//}

				LogService.WriteLogAsync("WARN", "SISTEMA_DEBUG", "Mapeador", $"[TRACE] Fila {i} | Llenas: {celdasLlenas} | Números Aquí: {numerosAqui} | Puntaje: {puntajeFila} | Texto: {textoFila}").Wait();

				// 🏆 EVALUACIÓN DEL GANADOR
				// Mantenemos el umbral mínimo en 15 puntos para garantizar calidad semántica básica
				if (puntajeFila > mejorPuntaje && puntajeFila >= 15)
				{
					mejorPuntaje = puntajeFila;
					filaCandidata = i;
				}
			}

			// DETERMINACIÓN FINAL
			if (filaCandidata != -1)
			{
				filaEncabezado = filaCandidata;
				LogService.WriteLogAsync("WARN", "SISTEMA_DEBUG", "Mapeador", $"[TRACE] -> ¡ANCLADO! Ganador indiscutible Fila {filaEncabezado} con {mejorPuntaje} puntos.").Wait();
			}
			else
			{
				LogService.WriteLogAsync("WARN", "SISTEMA_DEBUG", "Mapeador", "[TRACE] -> FRACASO: Ninguna fila superó el umbral de ponderación. Abortando pestaña.").Wait();
				return new Dictionary<string, int>();
			}
			//for (int i = 0; i < Math.Min(50, tabla.Rows.Count); i++)
			//{
			//	var celdas = tabla.Rows[i].ItemArray.Select(x => x?.ToString().Trim() ?? "").ToList();
			//	int celdasLlenas = celdas.Count(x => !string.IsNullOrWhiteSpace(x));

			//	// Si la fila está vacía, la ignoramos completamente
			//	if (celdasLlenas == 0) continue;

			//	string textoFila = string.Join(" ", celdas).ToUpper();
			//	int puntajeFila = 0;

			//	// 📊 CRITERIO 1: Densidad Horizontal (Peso ligero)
			//	// Entre más columnas tenga, más probable es que sea la tabla principal y no un subtítulo.
			//	puntajeFila += celdasLlenas;

			//	// 📊 CRITERIO 2: Palabras Clave Core (Peso pesado)
			//	// Estas palabras gritan "¡Soy el encabezado de los datos!"
			//	if (textoFila.Contains("CUENTA") || textoFila.Contains("CTA")) puntajeFila += 10;
			//	if (textoFila.Contains("PREDIO")) puntajeFila += 10;
			//	if (textoFila.Contains("BIMESTRE")) puntajeFila += 10;
			//	if (textoFila.Contains("CLASE")) puntajeFila += 10;
			//	if (textoFila.Contains("IMPUESTO") || textoFila.Contains("REDUCCION")) puntajeFila += 10;

			//	// 📊 CRITERIO 3: Palabras Clave Suaves (Peso medio)
			//	// Pueden estar en el título o en la tabla.
			//	if (textoFila.Contains("MUNICIPIO")) puntajeFila += 3;
			//	if (textoFila.Contains("FECHA")) puntajeFila += 3;
			//	if (textoFila.Contains("NOMBRE")) puntajeFila += 3;

			//	// 📊 CRITERIO 4: Análisis de Contexto Vecino (Lo que tú sugeriste)
			//	// Miramos la fila inmediatamente inferior. Si abajo hay números puros (como montos, cuentas, claves), 
			//	// es casi seguro que nosotros somos el encabezado.
			//	if (i + 1 < tabla.Rows.Count)
			//	{
			//		var celdasAbajo = tabla.Rows[i + 1].ItemArray.Select(x => x?.ToString().Trim() ?? "").ToList();
			//		int numerosAbajo = celdasAbajo.Count(c => decimal.TryParse(c, out _));

			//		// Cada número en la fila de abajo nos da muchísima confianza
			//		puntajeFila += (numerosAbajo * 5);
			//	}

			//	LogService.WriteLogAsync("WARN", "SISTEMA_DEBUG", "Mapeador", $"[TRACE] Fila {i} | Llenas: {celdasLlenas} | Puntaje Calculado: {puntajeFila} | Texto: {textoFila}").Wait();

			//	// 🏆 CORONACIÓN DEL CANDIDATO
			//	// Exigimos un mínimo de 15 puntos para evitar anclarnos en basura con números al azar
			//	if (puntajeFila > mejorPuntaje && puntajeFila >= 15)
			//	{
			//		mejorPuntaje = puntajeFila;
			//		filaCandidata = i;
			//	}
			//}


			//if (filaCandidata != -1)
			//{
			//	filaEncabezado = filaCandidata;
			//	LogService.WriteLogAsync("WARN", "SISTEMA_DEBUG", "Mapeador", $"[TRACE] -> ¡ANCLADO! Ganador indiscutible Fila {filaEncabezado} con {mejorPuntaje} puntos.").Wait();
			//}
			//else
			//{
			//	LogService.WriteLogAsync("WARN", "SISTEMA_DEBUG", "Mapeador", "[TRACE] -> FRACASO: Ninguna fila superó el umbral de ponderación. Abortando pestaña.").Wait();
			//	return new Dictionary<string, int>();
			//}

			// 2. ZONA B: Encontrar inicio de datos (Análisis Estructural Híbrido)
			filaInicioDatos = filaEncabezado + 1;
			for (int i = filaEncabezado + 1; i < Math.Min(filaEncabezado + 10, tabla.Rows.Count); i++)
			{
				var celdas = tabla.Rows[i].ItemArray.Select(x => x?.ToString().Trim() ?? "").ToList();
				string textoUnido = string.Join("", celdas).ToUpper();

				if (string.IsNullOrWhiteSpace(textoUnido)) continue;

				// 🚀 1. EVALUACIÓN ESTRUCTURAL
				int cantidadNumeros = celdas.Count(c => decimal.TryParse(c, out _));

				if (cantidadNumeros >= 3)
				{
					// 🛠️ FIX AYOTOXCO: Salvar los números de bimestres horizontales (1 al 6)
					var numValidos = new HashSet<string> { "1", "2", "3", "4", "5", "6", "1.0", "2.0", "3.0", "4.0", "5.0", "6.0" };
					int conteoBimestres = celdas.Count(c => numValidos.Contains(c.Replace(".00", "").Replace(".0", "").Trim()));

					if (conteoBimestres >= 3)
					{
						LogService.WriteLogAsync("WARN", "SISTEMA_DEBUG", "Mapeador", $"[TRACE] -> Fila {i} salvada. Es la secuencia bimestral.").Wait();
						continue; // Es un encabezado bimestral, lo fusionamos.
					}

					filaInicioDatos = i; // Si llegó aquí, son datos reales.
					break;
				}

				// 🚀 2. EVALUACIÓN LÉXICA ENRIQUECIDA
				if (textoUnido.Contains("TIPO DE PREDIO") || textoUnido.Contains("CLASE DE PAGO") ||
					textoUnido.Contains("BIMESTRE") || // <--- ESTO SALVA LA FILA QUE DICE "BIMESTRES"
					textoUnido.Contains("IMPUESTO DETERMINADO") || textoUnido.Contains("CLAVE DEL MUNICIPIO") ||
					textoUnido.Contains("="))
				{
					continue;
				}

				filaInicioDatos = i;
				break;
			}

			// 3. ZONA C: Extracción Vertical
			var mapaCrudo = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
			for (int c = 0; c < tabla.Columns.Count; c++)
			{
				List<string> partes = new List<string>();
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

			LogService.WriteLogAsync("WARN", "SISTEMA_DEBUG", "Mapeador", $"[TRACE] Mapa extraído: {string.Join(", ", mapaCrudo.Keys)}").Wait();
			return mapaCrudo;
		}

		//public static Dictionary<string, int> ObtenerMapaPorRegiones_v01(DataTable tabla, out int filaInicioDatos)
		//{
		//	filaInicioDatos = -1;
		//	int filaEncabezado = -1;

		//	// 1. ZONA A: Encontrar dónde empiezan los títulos reales (Atrapará la Fila 4 de Amixtlán)
		//	for (int i = 0; i < Math.Min(50, tabla.Rows.Count); i++)
		//	{
		//		var celdas = tabla.Rows[i].ItemArray.Select(x => x?.ToString().Trim() ?? "").Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
		//		string texto = string.Join(" ", celdas).ToUpper();

		//		// Con que tenga Clave, Municipio o Fecha, sabemos que es el inicio del encabezado
		//		bool tieneContexto = texto.Contains("CLAVE") || texto.Contains("MUNICIPIO") || texto.Contains("CUENTA") || texto.Contains("PREDIAL") || texto.Contains("FECHA");

		//		if (celdas.Count >= 2 && tieneContexto)
		//		{
		//			filaEncabezado = i;
		//			break;
		//		}
		//	}

		//	if (filaEncabezado == -1) return new Dictionary<string, int>();

		//	// 2. ZONA B: Encontrar inicio de datos (Saltará hasta la Fila 7)
		//	filaInicioDatos = filaEncabezado + 1;
		//	for (int i = filaEncabezado + 1; i < Math.Min(filaEncabezado + 10, tabla.Rows.Count); i++)
		//	{
		//		var celdas = tabla.Rows[i].ItemArray.Select(x => x?.ToString().Trim() ?? "").ToList();
		//		string textoUnido = string.Join("", celdas).ToUpper();

		//		if (string.IsNullOrWhiteSpace(textoUnido)) continue;

		//		// Si la fila tiene cualquiera de estos textos, sigue siendo parte del encabezado
		//		if (textoUnido.Contains("BIMESTRAL") || textoUnido.Contains("RUSTICO") || textoUnido.Contains("URBANO") ||
		//			textoUnido.Contains("ANUAL") || textoUnido.Contains("BIMESTRE") || textoUnido.Contains("SUBURBANO") ||
		//			textoUnido.Contains("PREDIO") || textoUnido.Contains("CUENTA") || textoUnido.Contains("CLASE"))
		//		{
		//			continue;
		//		}

		//		filaInicioDatos = i;
		//		break;
		//	}

		//	// 3. ZONA C: Extracción Vertical (Fusión de títulos)
		//	var mapaCrudo = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
		//	for (int c = 0; c < tabla.Columns.Count; c++)
		//	{
		//		List<string> partes = new List<string>();
		//		for (int r = filaEncabezado; r < filaInicioDatos; r++)
		//		{
		//			string val = tabla.Rows[r][c]?.ToString().Trim().ToUpper();
		//			if (!string.IsNullOrWhiteSpace(val)) partes.Add(val);
		//		}

		//		if (partes.Count > 0)
		//		{
		//			string colName = string.Join(" ", partes).Replace("\r", " ").Replace("\n", " ").Replace("  ", " ");
		//			if (!mapaCrudo.ContainsKey(colName)) mapaCrudo[colName] = c;
		//		}
		//	}

		//	return mapaCrudo;
		//}

		/// <summary>
		/// Procesa un diccionario de encabezados crudos y genera un mapeo oficial de columnas y bimestres sueltos, asegurando que no haya conflictos entre sinónimos y que las columnas importantes sean reclamadas primero. Este método utiliza una memoria interna para bloquear columnas ya asignadas y evitar que se asignen múltiples sinónimos a la misma columna.
		/// </summary>
		/// <param name="mapaCrudo"></param>
		/// <returns></returns>
		public static MapaOficial ProcesarEncabezadosConMemoria(Dictionary<string, int> mapaCrudo)
		{
			var oficial = new MapaOficial();
			var columnasUsadas = new HashSet<int>();

			// 🚀 0. PRIMERO RESCATAMOS LOS BIMESTRES SUELTOS (Evita que el consolidado se robe el Bimestre 1)
			string[] columnasBimestrales = { "1", "2", "3", "4", "5", "6", "B1", "B2", "B3", "B4", "B5", "B6" };
			foreach (var col in columnasBimestrales)
			{
				foreach (var kvp in mapaCrudo)
				{
					if (!columnasUsadas.Contains(kvp.Value))
					{
						// Match exacto ("1") o sufijo de celdas agrupadas ("BIMESTRES 1", "BIM 2")
						if (kvp.Key == col || kvp.Key.EndsWith("BIMESTRES " + col) || kvp.Key.EndsWith("BIMESTRE " + col) || kvp.Key.EndsWith("BIM " + col))
						{
							oficial.BimestresSueltos[col] = kvp.Value;
							columnasUsadas.Add(kvp.Value);
							break;
						}
					}
				}
			}


			void Asignar(string nombreOficial, params string[] sinonimos)
			{
				foreach (var sin in sinonimos)
				{
					foreach (var kvp in mapaCrudo)
					{
						// Buscamos coincidencia ignorando espacios y mayúsculas
						if (kvp.Key.Replace(" ", "").Contains(sin.Replace(" ", "")) && !columnasUsadas.Contains(kvp.Value))
						{
							oficial.Columnas[nombreOficial] = kvp.Value;
							columnasUsadas.Add(kvp.Value); // 🔒 Se bloquea la columna
							return;
						}
					}
				}
			}

			// 🚀 1. PRIMERO ASEGURAMOS LOS METADATOS COMPUESTOS (El blindaje)
			Asignar("ClasePago", "CLASE DE PAGO", "CLASE");
			Asignar("BimestreConsolidado", "BIMESTRE PAGADO", "BIMESTRE", "PERIODO", "MESES");
			Asignar("ClaveMunicipio", "CLAVE DEL MUNICIPIO", "MUNICIPIO", "CVEMUN", "MPIO");
			Asignar("TipoPredio", "TIPO DE PREDIO", "PREDIO", "TIPO", "DESC_PRED","T/P");

			// 🚀 2. LAS COLUMNAS OBLIGATORIAS Y PRINCIPALES
			Asignar("CuentaPredial", "CUENTA PREDIAL","NUMERO DE CUENTA", "NO. CUENTA", "CUENTA", "CTA", "CTA.", "CLAVE");
			Asignar("Anio", "AÑO", "EJERCICIO", "EJERCICIO FISCAL");
			//Asignar("ImpuestoDeterminado", "SALDO", "TOTAL", "PAGO", "IMPUESTO", "IMPORTE", "TOTAL.*BRUTO", "IMPUESTO.*TOTAL");
			// Obtenemos el año en curso para blindarlo a futuro
			string anioActual = DateTime.Now.Year.ToString();

			Asignar("ImpuestoDeterminado",
				// 🎯 1. FRANCOTIRADORES DE MÁXIMA PRIORIDAD (Año actual)
				$"{anioActual}(BRUTO)",      // Atrapa a Cañada Morelos: "2026 (BRUTO)"
				$"{anioActual}BRUTO",
				$"TOTAL{anioActual}",        // Ej: "TOTAL 2026"
				$"IMPUESTO{anioActual}",     // Ej: "IMPUESTO 2026"
				anioActual.ToString(),       // 🛠️ FIX ZIHUATEUTLA: El puro año "2026" es el monto a cobrar
											 // 🎯 2. FRANCOTIRADORES SEMÁNTICOS (Palabras contundentes)
				"ACTUAL",                    // Ej: "IMPUESTO ACTUAL", "COBRO ACTUAL"
				"NETO",                      // Ej: "IMPORTE COBRADO (NETO)"
				"IMPUESTODETERMINADO",       // Nombre oficial perfecto

				// 🎯 3. PALABRAS ESTÁNDAR (El 90% de los municipios)
				"IMPUESTO",
				"IMPORTE",
				"PAGO",

				// 🎯 4. ÚLTIMO RECURSO (Genéricos peligrosos)
				"SALDO",
				"TOTAL"
			);

			Asignar("FechaVigencia", "FECHA", "VIGENCIA");
			Asignar("BaseGravable", "BASE GRAVABLE", "BASE", "VALOR CATASTRAL", "VALOR");

			// 🚀 3. EL NUEVO ENTRENAMIENTO: TODA LA DEMOGRAFÍA OPCIONAL DEL PADRÓN
			Asignar("FolioUnico", "FOLIO UNICO", "FOLIO ÚNICO", "FOLIO", "CONTROL", "NÚM. DE CONTROL", "NUM. DE CONTROL");
			Asignar("Localidad", "LOCALIDAD", "POBLACION", "POBLACIÓN", "CIUDAD");
			Asignar("Calle", "CALLE", "AVENIDA", "DIRECCION", "DIRECCIÓN", "DOMICILIO");
			Asignar("NumExterior", "NÚM. EXT", "NUM. EXT", "NO. EXT", "EXTERIOR", "EXT");
			Asignar("NumInterior", "NÚM. INT", "NUM. INT", "NO. INT", "INTERIOR", "INT");
			Asignar("Letra", "LETRA", "LOTE", "MANZANA", "MZA");
			Asignar("Colonia", "COLONIA", "BARRIO", "FRACCIONAMIENTO", "SECCION");
			Asignar("CP", "CP", "C.P.", "CÓDIGO POSTAL", "CODIGO POSTAL");

			Asignar("Nombre", "NOMBRE", "PROPIETARIO", "CONTRIBUYENTE", "RAZON SOCIAL", "RAZÓN SOCIAL");
			Asignar("PrimerApellido", "PRIMER APELLIDO", "APELLIDO PATERNO", "PATERNO", "APELLIDO 1");
			Asignar("SegundoApellido", "SEGUNDO APELLIDO", "APELLIDO MATERNO", "MATERNO", "APELLIDO 2");

			Asignar("TipoPersona", "TIPO PERSONA", "TIPO DE PERSONA", "FISICA/MORAL", "FISICA MORAL");
			Asignar("RFC", "RFC", "R.F.C.", "REGISTRO FEDERAL", "REGISTRO FEDERAL DEL CONTRIBUYENTE");
			Asignar("ClaveRegimenSAT", "REGIMEN SAT", "RÉGIMEN SAT", "REGIMEN FISCAL", "CLAVE REGIMEN");
			Asignar("ClaveUsoSAT", "USO SAT", "USO CFDI", "CLAVE USO");
			Asignar("CPFiscalSAT", "CP FISCAL", "C.P. FISCAL", "CODIGO POSTAL FISCAL");

			//Para el archivo de los descuentos.
			Asignar("TipoReduccion", "TIPO DE REDUCCION", "TIPO DE REDUCCIÓN", "REDUCCION", "REDUCCIÓN", "DESCUENTO", "REDUCCI");

			// Bimestres Sueltos
			//string[] columnasBimestrales = { "1", "2", "3", "4", "5", "6", "B1", "B2", "B3", "B4", "B5", "B6" };
			//foreach (var col in columnasBimestrales)
			//{
			//	if (mapaCrudo.ContainsKey(col) && !columnasUsadas.Contains(mapaCrudo[col]))
			//	{
			//		oficial.BimestresSueltos[col] = mapaCrudo[col];
			//		columnasUsadas.Add(mapaCrudo[col]);
			//	}
			//}
			
			return oficial;
		}

		/// <summary>
		/// Permite extraer el valor de una columna específica de un DataRow utilizando un mapeo oficial de columnas. Si la columna no existe en el mapeo, devuelve una cadena vacía. Este método asegura que los datos se extraigan de manera consistente y evita errores al acceder a columnas inexistentes.
		/// </summary>
		/// <param name="fila"></param>
		/// <param name="mapa"></param>
		/// <param name="columna"></param>
		/// <returns></returns>
		public static string Extraer(DataRow fila, MapaOficial mapa, string columna)
		{
			if (mapa.Columnas.ContainsKey(columna))
			{
				return fila[mapa.Columnas[columna]]?.ToString().Trim() ?? string.Empty;
			}
			return string.Empty;
		}

		/// <summary>
		/// Permite rastrear los bimestres sueltos en un DataRow utilizando un mapeo oficial de columnas. Este método busca los valores de las columnas correspondientes a los bimestres y devuelve una cadena con los bimestres detectados, separados por comas. Si no se detectan bimestres sueltos, intenta extraer el valor del bimestre consolidado.
		/// </summary>
		/// <param name="fila"></param>
		/// <param name="mapa"></param>
		/// <returns></returns>
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
		/// <summary>
		/// Método que permite obtener un mapeo de columnas a partir de un DataTable, considerando múltiples filas de encabezado. Este método analiza las filas especificadas y construye un diccionario que asocia los nombres de columna con sus índices correspondientes, permitiendo extraer datos de manera flexible incluso cuando los encabezados están distribuidos en varias filas.
		/// </summary>
		/// <param name="tabla"></param>
		/// <param name="indiceFilaInicio"></param>
		/// <param name="numFilas"></param>
		/// <returns></returns>
		public static Dictionary<string, int> ObtenerMapaColumnasMultiFila(DataTable tabla, int indiceFilaInicio, int numFilas) { return new Dictionary<string, int>(); /* Omitido por brevedad, no lo borres de tu código si lo tienes */ }

		/// <summary>
		/// Método que permite estandarizar un DataTable crudo utilizando un mapeo de columnas previamente obtenido. Este método crea un nuevo DataTable con las columnas oficiales y copia los datos correspondientes desde el DataTable crudo, asegurando que los datos se alineen correctamente con el mapeo oficial y que se mantenga la integridad de la información.
		/// </summary>
		/// <param name="tablaCruda"></param>
		/// <param name="mapaCrudo"></param>
		/// <param name="indiceInicioDatos"></param>
		/// <returns></returns>
		public static DataTable EstandarizarTabla(DataTable tablaCruda, Dictionary<string, int> mapaCrudo, int indiceInicioDatos = 0) { return new DataTable(); /* Omitido por brevedad, no lo borres */ }


		/// <summary>
		/// Extrae todos los bimestres pagados en un layout horizontal (estilo Ayotoxco) junto con el monto pagado en cada uno.
		/// </summary>
		public static Dictionary<string, decimal> ExtraerBimestresMultiplesConMonto(DataRow fila, MapaOficial mapa)
		{
			var resultado = new Dictionary<string, decimal>();
			foreach (var kvp in mapa.BimestresSueltos)
			{
				string valor = fila[kvp.Value]?.ToString().Trim().ToUpper() ?? "";

				// Ayotoxco usa guiones "-" para celdas vacías contables. Los ignoramos.
				if (!string.IsNullOrWhiteSpace(valor) && valor != "0" && valor != "0.00" && valor != "-")
				{
					// Limpiamos formato moneda
					string valorLimpio = valor.Replace("$", "").Replace(",", "").Trim();
					if (decimal.TryParse(valorLimpio, out decimal monto))
					{
						resultado[kvp.Key.Replace("B", "")] = monto;
					}
				}
			}
			return resultado;
		}
	}
}