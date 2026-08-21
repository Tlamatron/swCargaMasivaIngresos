using swCargaMasivaIngresos.Utils;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Web;

namespace swCargaMasivaIngresos.Services
{
	public static class ConfiguracionApp
	{
		/// <summary>
		/// Obtiene la cadena de conexión dinámicamente basándose en la llave 'Ambiente' del Web.config.
		/// </summary>
		public static string ObtenerCadenaConexion_ante()
		{
			// 1. Leemos el ambiente actual. Si por alguna razón no existe, por seguridad asumimos 'Local'.
			string ambiente = ConfigurationManager.AppSettings["Ambiente"] ?? "Local";

			// 2. Construimos el nombre exacto de la cadena
			string nombreCadena = $"ConexionSQL_{ambiente}";

			// 3. Devolvemos la conexión correspondiente
			return ConfigurationManager.ConnectionStrings[nombreCadena].ConnectionString;
		}

		/// <summary>
		/// Obtiene la cadena de conexión dinámicamente según el ambiente y la desencripta en memoria.
		/// </summary>
		public static string ObtenerCadenaConexion()
		{
			// 1. Leemos el ambiente actual. Si por alguna razón no existe, asumimos 'Local'.
			string ambiente = ConfigurationManager.AppSettings["Ambiente"] ?? "Local";

			// 2. Construimos el nombre exacto de la cadena
			string nombreCadena = $"ConexionSQL_{ambiente}";

			// 3. Obtenemos el texto encriptado directamente del Web.config
			string cadenaEncriptada = ConfigurationManager.ConnectionStrings[nombreCadena].ConnectionString;

			// 4. 🚀 EXCEPCIÓN PARA DESARROLLO (Opcional pero recomendada)
			// Si estás en Local, a veces es más cómodo dejarla en texto plano.
			if (ambiente.Equals("Local", System.StringComparison.OrdinalIgnoreCase))
			{
				// Si tu cadena local NO está encriptada, devuélvela directa:
				// return cadenaEncriptada;
			}

			// 5. Desencriptamos en la memoria RAM y devolvemos la cadena real lista para SQL
			return CryptoHelper.Desencriptar(cadenaEncriptada);
		}
	}
}