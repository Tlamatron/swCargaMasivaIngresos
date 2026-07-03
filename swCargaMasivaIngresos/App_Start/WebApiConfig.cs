using swCargaMasivaIngresos.Areas.HelpPage;
using swCargaMasivaIngresos.Services;
using System;
using System.Configuration;
using System.Web.Http;
using System.Web.Http.Cors;
using System.Web.Http.ExceptionHandling;

namespace swCargaMasivaIngresos
{
	public static class WebApiConfig
	{
		public static void Register(HttpConfiguration config)
		{
			// 1. Configuración de CORS Dinámico desde Cors.config
			string ambiente = ConfigurationManager.AppSettings["Ambiente"] ?? "Test";
			string[] originsArray = CorsSettings.GetCorsOrigins(ambiente);
			string origins = string.Join(",", originsArray);


			// Forzar origen seguro si por alguna razón la llave no se encuentra
			if (string.IsNullOrEmpty(origins)) origins = "https://wscargamasivaingresos.puebla.gob.mx";

			var cors = new EnableCorsAttribute(
				origins,
				"Content-Type,Authorization,Token,Accept,Origin",   // Headers permitidos
				"GET,POST,PUT,DELETE,OPTIONS"                         // Métodos permitidos
			);
			config.EnableCors(cors);

			// 2. Control de flujo y seguridad perimetral (Handlers y Filtros)
			config.MessageHandlers.Add(new RateLimitHandler());
			config.Filters.Add(new ValidateJsonContentFilter());
			config.Services.Replace(typeof(IExceptionHandler), new GlobalExceptionHandler());

			// 3. Habilitar rutas por atributos (Atribute Routing)
			config.MapHttpAttributeRoutes();

			// 4. Ruta por defecto de la API
			config.Routes.MapHttpRoute(
				name: "DefaultApi",
				routeTemplate: "api/{controller}/{id}",
				defaults: new { id = RouteParameter.Optional }
			);

			// 5. Conexión dinámica con la Documentación XML del HelpPage (/help)
			var xmlPath = AppDomain.CurrentDomain.BaseDirectory + @"bin\swCargaMasivaIngresos.xml";
			if (System.IO.File.Exists(xmlPath))
			{
				config.SetDocumentationProvider(new XmlDocumentationProvider(xmlPath));
			}
		}
	}
}