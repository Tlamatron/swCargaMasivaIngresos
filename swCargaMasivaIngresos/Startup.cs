using Microsoft.Owin;
using Owin;
using Hangfire;

// 🚀 ¡ESTA ES LA LÍNEA MÁGICA QUE EL SERVIDOR ESTÁ BUSCANDO!
[assembly: OwinStartup(typeof(swCargaMasivaIngresos.Startup))]

namespace swCargaMasivaIngresos
{
	public class Startup
	{
		public void Configuration(IAppBuilder app)
		{
			// 1. Configurar la base de datos que Hangfire usará para guardar las tareas pendientes
			// Nota: "ConexionSQL" debe coincidir exactamente con el nombre de tu cadena en el Web.config
			GlobalConfiguration.Configuration
				.UseSqlServerStorage("ConexionSQL");

			// 2. Levantar el "Worker" (el motor que procesará el MotorPrincipalCarga en 2do plano)
			app.UseHangfireServer();

			// 3. (Recomendado) Levantar el panel de control visual para ver tus tareas en tiempo real
			app.UseHangfireDashboard();
		}
	}
}