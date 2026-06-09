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

		public static bool TryParseFecha(string valor, out DateTime fecha)
		{
			// Soporta múltiples formatos por si los municipios varían un poco
			string[] formatos = { "dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy" };
			return DateTime.TryParseExact(valor.Trim(), formatos, _cultura, DateTimeStyles.None, out fecha);
		}

		public static bool TryParseMoneda(string valor, out decimal monto)
		{
			// Permite símbolos de moneda, comas de miles y decimales
			return decimal.TryParse(valor.Trim(), NumberStyles.Currency, _cultura, out monto);
		}

		public static bool TryParseEntero(string valor, out int numero)
		{
			return int.TryParse(valor.Trim(), out numero);
		}

		public static string LimpiarCadena(string valor, int maxLongitud)
		{
			if (string.IsNullOrWhiteSpace(valor)) return string.Empty;
			string limpio = valor.Trim();
			return limpio.Length > maxLongitud ? limpio.Substring(0, maxLongitud) : limpio;
		}
	}
}