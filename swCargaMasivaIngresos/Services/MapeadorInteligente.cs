using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;

namespace swCargaMasivaIngresos.Services
{
	public static class MapeadorInteligente
	{
		// 🚀 DICCIONARIO DE SINÓNIMOS
		// Llave: Cómo podría venir en el Excel/CSV. Valor: Tu nombre de columna estándar.
		private static readonly Dictionary<string, string> DiccionarioColumnas = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
		{
            // Obligatorios
            {"CLAVE DEL MUNICIPIO", "ClaveMunicipio"}, {"MUNICIPIO", "ClaveMunicipio"}, {"CVEMUN", "ClaveMunicipio"}, {"CVE_MUN", "ClaveMunicipio"}, {"CLAVE MUNICIPIO", "ClaveMunicipio"},
			{"TIPO DE PREDIO", "TipoPredio"}, {"PREDIO", "TipoPredio"}, {"TIPO", "TipoPredio"},
			{"NÚMERO DE CUENTA PREDIAL", "CuentaPredial"}, {"CUENTA PREDIAL", "CuentaPredial"}, {"CUENTA", "CuentaPredial"}, {"NO.CUENTA", "CuentaPredial"},
			{"CLASE DE PAGO", "ClasePago"}, {"CLASE", "ClasePago"},
			{"BIMESTRE PAGADO", "Bimestre"}, {"BIMESTRE", "Bimestre"},
			{"PAGO 2026", "ImpuestoDeterminado"}, {"PAGO", "ImpuestoDeterminado"}, {"IMPUESTO DETERMINADO", "ImpuestoDeterminado"}, {"IMPUESTO", "ImpuestoDeterminado"},
            
            // Opcionales / Padrón
            {"NÚMERO DE CONTROL O FOLIADOR ÚNICO", "FolioUnico"}, {"NO.CONTROL", "FolioUnico"}, {"FOLIO", "FolioUnico"}, {"CONTROL", "FolioUnico"},
			{"LOCALIDAD DEL PREDIO", "Localidad"}, {"LOCALIDAD", "Localidad"},
			{"CALLE DEL PREDIO", "Calle"}, {"CALLE", "Calle"},
			{"NÚMERO EXTERIOR DEL PREDIO", "NumExterior"}, {"NO.EXT", "NumExterior"}, {"EXTERIOR", "NumExterior"},
			{"NÚMERO INTERIOR DEL PREDIO", "NumInterior"}, {"NO.INT", "NumInterior"}, {"INTERIOR", "NumInterior"},
			{"LETRA DEL PREDIO", "Letra"}, {"LETRA", "Letra"},
			{"COLONIA DEL PREDIO", "Colonia"}, {"COLONIA", "Colonia"},
			{"CÓDIGO POSTAL DEL PREDIO", "CP"}, {"CP", "CP"}, {"C.P.", "CP"},
			{"NOMBRE", "Nombre"}, {"RAZON SOCIAL", "Nombre"}, {"CONTRIBUYENTE", "Nombre"},
			{"PRIMER APELLIDO", "PrimerApellido"}, {"APELLIDO PATERNO", "PrimerApellido"},
			{"SEGUNDO APELLIDO", "SegundoApellido"}, {"APELLIDO MATERNO", "SegundoApellido"},
			{"TIPO DE PERSONA", "TipoPersona"}, {"PERSONA", "TipoPersona"},
			{"RFC", "RFC"},
			{"CLAVE RÉGIMEN SAT", "ClaveRegimenSAT"}, {"REGIMEN", "ClaveRegimenSAT"},
			{"CLAVE USO SAT", "ClaveUsoSAT"}, {"USO SAT", "ClaveUsoSAT"},
			{"CÓDIGO POSTAL FISCAL SAT", "CPFiscalSAT"}, {"CP FISCAL", "CPFiscalSAT"},
			{"BASE GRAVABLE", "BaseGravable"}, {"BASE", "BaseGravable"},
			{"FECHA VIGENCIA", "FechaVigencia"}, {"FECHA", "FechaVigencia"},
            
            // Exclusivos Reducciones
            {"TIPO DE REDUCCIÓN", "TipoReduccion"}, {"REDUCCION", "TipoReduccion"}, {"DESCUENTO", "TipoReduccion"}
		};

		public static DataTable EstandarizarTabla(DataTable tablaCruda, out List<string> erroresMapeo)
		{
			erroresMapeo = new List<string>();
			DataTable tablaEstandar = CrearEstructuraBase();

			if (tablaCruda == null || tablaCruda.Rows.Count == 0)
			{
				erroresMapeo.Add("El archivo no contiene datos.");
				return tablaEstandar;
			}

			// 1. Encontrar la fila de encabezados reales (Ignorando títulos arriba)
			int indiceFilaEncabezados = EncontrarFilaEncabezados(tablaCruda);

			if (indiceFilaEncabezados == -1)
			{
				erroresMapeo.Add("No se pudieron identificar las columnas requeridas en el archivo (Ej. Falta Clave del Municipio o Cuenta Predial). Verifique el formato.");
				return tablaEstandar;
			}

			// 2. Mapear qué índice de columna cruda corresponde a qué columna estándar
			var mapaColumnas = MapearIndicesColumnas(tablaCruda.Rows[indiceFilaEncabezados]);

			// 3. Trasladar los datos (A partir de la fila siguiente a los encabezados)
			for (int i = indiceFilaEncabezados + 1; i < tablaCruda.Rows.Count; i++)
			{
				DataRow filaCruda = tablaCruda.Rows[i];

				// Si la fila está completamente vacía, la saltamos
				if (filaCruda.ItemArray.All(x => string.IsNullOrWhiteSpace(x?.ToString()))) continue;

				DataRow nuevaFila = tablaEstandar.NewRow();

				foreach (var mapa in mapaColumnas)
				{
					// mapa.Key = "ClaveMunicipio", mapa.Value = 2 (Índice en la tabla cruda)
					string valorCelda = filaCruda[mapa.Value]?.ToString().Trim();
					nuevaFila[mapa.Key] = valorCelda;
				}

				tablaEstandar.Rows.Add(nuevaFila);
			}

			return tablaEstandar;
		}

		/// <summary>
		/// Busca fila por fila hasta encontrar una que tenga al menos 2 palabras clave de nuestros diccionarios.
		/// Esto evita leer títulos y logos.
		/// </summary>
		private static int EncontrarFilaEncabezados(DataTable tablaCruda)
		{
			// Escaneamos solo las primeras 15 filas para no penalizar rendimiento
			int limite = Math.Min(15, tablaCruda.Rows.Count);

			for (int i = 0; i < limite; i++)
			{
				int coincidencias = 0;
				foreach (var item in tablaCruda.Rows[i].ItemArray)
				{
					string valorCelda = item?.ToString().Trim();
					if (!string.IsNullOrEmpty(valorCelda) && DiccionarioColumnas.ContainsKey(valorCelda))
					{
						coincidencias++;
					}
				}

				// Si encontramos al menos 2 columnas que nos suenan, asumimos que aquí empiezan los datos
				if (coincidencias >= 2) return i;
			}

			return -1;
		}

		/// <summary>
		/// Crea un diccionario que asocia nuestro nombre de columna estándar con la posición [int] en la tabla cruda.
		/// </summary>
		private static Dictionary<string, int> MapearIndicesColumnas(DataRow filaEncabezados)
		{
			var mapa = new Dictionary<string, int>();

			for (int i = 0; i < filaEncabezados.ItemArray.Length; i++)
			{
				string nombreCrudo = filaEncabezados[i]?.ToString().Trim();

				if (!string.IsNullOrEmpty(nombreCrudo) && DiccionarioColumnas.TryGetValue(nombreCrudo, out string nombreEstandar))
				{
					// Si encontramos una columna y aún no la hemos mapeado, la guardamos
					if (!mapa.ContainsKey(nombreEstandar))
					{
						mapa.Add(nombreEstandar, i);
					}
				}
			}

			return mapa;
		}

		private static DataTable CrearEstructuraBase()
		{
			var tabla = new DataTable();

			// Agregamos todas las posibles columnas. Lo que no venga en el archivo se quedará como string nulo o vacío.
			string[] columnas = {
				"ClaveMunicipio", "TipoPredio", "CuentaPredial", "FolioUnico", "Localidad", "Calle",
				"NumExterior", "NumInterior", "Letra", "Colonia", "CP", "Nombre", "PrimerApellido",
				"SegundoApellido", "TipoPersona", "RFC", "ClaveRegimenSAT", "ClaveUsoSAT", "CPFiscalSAT",
				"BaseGravable", "ClasePago", "Bimestre", "ImpuestoDeterminado", "FechaVigencia", "TipoReduccion"
			};

			foreach (var col in columnas) tabla.Columns.Add(col, typeof(string));

			return tabla;
		}
	}
}