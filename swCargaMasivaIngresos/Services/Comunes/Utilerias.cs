using System;
using System.Globalization;

namespace swCargaMasivaIngresos.Services
{
	/// <summary>
	/// Clase estática que contiene métodos de utilidad para validar y limpiar datos extraídos de los archivos, como fechas, montos y cadenas de texto. Esto ayuda a centralizar la lógica de validación y limpieza, asegurando consistencia en todo el proceso de carga masiva.
	/// </summary>
	public static class Utilerias
	{
		// Forzamos cultura invariante para evitar problemas si el servidor web está en inglés o español
		private static readonly CultureInfo _cultura = CultureInfo.InvariantCulture;

		/// <summary>
		/// Método que intenta convertir una cadena de texto en un objeto DateTime, soportando múltiples formatos de fecha. Devuelve true si la conversión es exitosa y false si falla, evitando excepciones y permitiendo manejar errores de manera controlada.
		/// </summary>
		/// <param name="valor"></param>
		/// <param name="fecha"></param>
		/// <returns></returns>
		public static bool TryParseFecha(string valor, out DateTime fecha)
		{
			// Soporta múltiples formatos por si los municipios varían un poco
			string[] formatos = { "dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy" };
			return DateTime.TryParseExact(valor.Trim(), formatos, _cultura, DateTimeStyles.None, out fecha);
		}

		/// <summary>
		/// Método que intenta convertir una cadena de texto en un valor decimal, soportando símbolos de moneda, comas de miles y decimales. Devuelve true si la conversión es exitosa y false si falla, evitando excepciones y permitiendo manejar errores de manera controlada.
		/// </summary>
		/// <param name="valor"></param>
		/// <param name="monto"></param>
		/// <returns></returns>
		public static bool TryParseMoneda(string valor, out decimal monto)
		{
			// Permite símbolos de moneda, comas de miles y decimales
			return decimal.TryParse(valor.Trim(), NumberStyles.Currency, _cultura, out monto);
		}

		/// <summary>
		/// Método que intenta convertir una cadena de texto en un valor entero. Devuelve true si la conversión es exitosa y false si falla, evitando excepciones y permitiendo manejar errores de manera controlada.
		/// </summary>
		/// <param name="valor"></param>
		/// <param name="numero"></param>
		/// <returns></returns>
		public static bool TryParseEntero(string valor, out int numero)
		{
			return int.TryParse(valor.Trim(), out numero);
		}

		/// <summary>
		/// Método que limpia una cadena de texto eliminando espacios en blanco al inicio y al final, y truncándola a una longitud máxima especificada. Esto ayuda a evitar errores de validación por exceso de caracteres o espacios innecesarios.
		/// </summary>
		/// <param name="valor"></param>
		/// <param name="maxLongitud"></param>
		/// <returns></returns>
		public static string LimpiarCadena(string valor, int maxLongitud)
		{
			if (string.IsNullOrWhiteSpace(valor)) return string.Empty;
			string limpio = valor.Trim();
			return limpio.Length > maxLongitud ? limpio.Substring(0, maxLongitud) : limpio;
		}
	}
}