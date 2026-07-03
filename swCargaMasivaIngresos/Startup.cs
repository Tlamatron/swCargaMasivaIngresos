using Hangfire;
using Hangfire.SqlServer;
using Microsoft.Owin;
using Owin;
using swCargaMasivaIngresos.Services;

// 🚀 ¡ESTA ES LA LÍNEA MÁGICA QUE EL SERVIDOR ESTÁ BUSCANDO!
[assembly: OwinStartup(typeof(swCargaMasivaIngresos.Startup))]

namespace swCargaMasivaIngresos
{
	public class Startup
	{
		public void Configuration(IAppBuilder app)
		{
			// 1. Obtenemos la conexión dinámica (Local, Test o Prod)
			string cadenaConexionActiva = ConfiguracionApp.ObtenerCadenaConexion();

			// 2. Configurar la base de datos de Hangfire usando el esquema personalizado
			GlobalConfiguration.Configuration
				.UseSqlServerStorage(cadenaConexionActiva, new SqlServerStorageOptions
				{
					SchemaName = "pred_HangFire", // 🚀 AQUÍ ESTÁ LA MAGIA PARA LOS ESQUEMAS
					PrepareSchemaIfNecessary = true // Le permite a Hangfire crear sus tablas si no existen
				});

			// 3. Levantar el "Worker" 
			app.UseHangfireServer();

			// 4. Levantar el panel de control visual
			app.UseHangfireDashboard();
		}
	}
}