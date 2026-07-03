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
		public static string ObtenerCadenaConexion()
		{
			// 1. Leemos el ambiente actual. Si por alguna razón no existe, por seguridad asumimos 'Local'.
			string ambiente = ConfigurationManager.AppSettings["Ambiente"] ?? "Local";

			// 2. Construimos el nombre exacto de la cadena
			string nombreCadena = $"ConexionSQL_{ambiente}";

			// 3. Devolvemos la conexión correspondiente
			return ConfigurationManager.ConnectionStrings[nombreCadena].ConnectionString;
		}
	}
}