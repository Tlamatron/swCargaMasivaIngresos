using System;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Web.Hosting;
using System.Xml.Linq;

namespace swCargaMasivaIngresos.Services
{
	/// <summary>
	/// Clase de configuración para los ajustes de CORS (Cross-Origin Resource Sharing). Esta clase proporciona métodos para obtener los orígenes permitidos y las aplicaciones permitidas según el entorno (Producción o Pruebas) a partir de un archivo de configuración externo.
	/// </summary>
	public static class CorsSettings
	{
		private static readonly string corsPath = HostingEnvironment.MapPath("~/Content/seguridad/Cors.config");

		/// <summary>
		/// Retorna un arreglo de cadenas que representan los orígenes permitidos para CORS según el entorno especificado (Producción o Pruebas). El método lee la configuración desde un archivo externo y maneja tanto el formato completo de configuración como el formato XML simplificado.
		/// </summary>
		/// <param name="entorno"></param>
		/// <returns></returns>
		public static string[] GetCorsOrigins(string entorno)
		{
			string key = entorno == "Prod" ? "CorsOrigins_Prod" : "CorsOrigins_Test";
			string value = GetValue(key);
			return value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
		}

		/// <summary>
		/// Retorna un arreglo de cadenas que representan las aplicaciones permitidas según el entorno especificado (Producción o Pruebas). El método lee la configuración desde un archivo externo y maneja tanto el formato completo de configuración como el formato XML simplificado.
		/// </summary>
		/// <param name="entorno"></param>
		/// <returns></returns>
		public static string[] GetAppsPermitidas(string entorno)
		{
			string key = entorno == "Prod" ? "AppsPermitidasProduccion" : "AppsPermitidasPruebas";
			string value = GetValue(key);
			return value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
		}

		/// <summary>
		/// Retorna el valor de configuración correspondiente a la clave especificada desde el archivo de configuración externo. El método intenta primero leerlo como un archivo de configuración completo y, si falla, lo lee como un XML con raíz appSettings.
		/// </summary>
		/// <param name="key"></param>
		/// <returns></returns>
		private static string GetValue(string key)
		{
			if (!File.Exists(corsPath)) return string.Empty;

			try
			{
				// Intentar como archivo de configuración completo (<configuration>)
				var fileMap = new ExeConfigurationFileMap { ExeConfigFilename = corsPath };
				var config = ConfigurationManager.OpenMappedExeConfiguration(fileMap, ConfigurationUserLevel.None);
				var val = config.AppSettings.Settings[key]?.Value;
				if (!string.IsNullOrEmpty(val)) return val;
			}
			catch
			{
				// Si falla, intentamos leerlo como XML con raíz <appSettings>
				var doc = XDocument.Load(corsPath);
				var val = doc.Element("appSettings")?
							 .Elements("add")
							 .FirstOrDefault(x => (string)x.Attribute("key") == key)?
							 .Attribute("value")?.Value;
				if (!string.IsNullOrEmpty(val)) return val;
			}

			return string.Empty;
		}
	}
}
