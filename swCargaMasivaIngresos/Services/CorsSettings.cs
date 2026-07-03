using System;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Web.Hosting;
using System.Xml.Linq;

namespace swCargaMasivaIngresos.Services
{
	public static class CorsSettings
	{
		private static readonly string corsPath = HostingEnvironment.MapPath("~/Content/seguridad/Cors.config");

		public static string[] GetCorsOrigins(string entorno)
		{
			string key = entorno == "Prod" ? "CorsOrigins_Prod" : "CorsOrigins_Test";
			string value = GetValue(key);
			return value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
		}

		public static string[] GetAppsPermitidas(string entorno)
		{
			string key = entorno == "Prod" ? "AppsPermitidasProduccion" : "AppsPermitidasPruebas";
			string value = GetValue(key);
			return value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
		}

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
