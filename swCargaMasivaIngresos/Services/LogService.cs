using System;
using System.Configuration;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Web.Hosting;

namespace swCargaMasivaIngresos.Services
{
	/// <summary>
	/// Registra logs en servicio institucional de logs mediante patrón Fire-and-Forget.
	/// </summary>
	public static class LogService
	{
		/// <summary>
		/// Método para escribir un log en el servicio de logs. Utiliza patrón Fire-and-Forget para no bloquear el hilo principal.
		/// </summary>
		private static readonly HttpClient _httpClient = new HttpClient
		{
			// Reducimos el timeout a 3 segundos. Al ser en segundo plano, no importa mucho, 
			// pero libera el socket más rápido si hay problemas de red.
			Timeout = TimeSpan.FromSeconds(3)
		};

		/// <summary>
		/// Método para escribir en el servicio de logs. No devuelve nada al llamador, ya que se ejecuta en un hilo de fondo.
		/// </summary>
		/// <param name="appName"></param>
		/// <param name="nivel"></param>
		/// <param name="usuario"></param>
		/// <param name="origen"></param>
		/// <param name="mensaje"></param>
		/// <returns></returns>
		public static Task WriteLogAsync(string appName, string nivel, string usuario, string origen, string mensaje)
		{
			// 🚀 PATRÓN FIRE-AND-FORGET: 
			// Encolamos el trabajo en un hilo de fondo de IIS.
			// El usuario final NO espera a que este bloque de código termine.
			HostingEnvironment.QueueBackgroundWorkItem(async cancellationToken =>
			{
				try
				{
					string ambiente = ConfigurationManager.AppSettings["Ambiente"] ?? "Test";
					string baseUrl = ambiente.Equals("Prod", StringComparison.OrdinalIgnoreCase)
						? ConfigurationManager.AppSettings["LogServiceUrlProd"]
						: ConfigurationManager.AppSettings["LogServiceUrlTest"];

					string urlFinal = $"{baseUrl}/{appName}";

					string logMessage = $"| {usuario} | {nivel} | {origen} | {mensaje}";
					var content = new StringContent($"\"{logMessage}\"", Encoding.UTF8, "application/json");

					// 🚀 Esto viaja por la red en segundo plano.
					var response = await _httpClient.PostAsync(urlFinal, content, cancellationToken);

					// 📌 Solo leemos la respuesta si el programador está depurando. 
					// En Producción, evitamos gastar memoria leyendo el body de la respuesta.
#if DEBUG
					string responseBody = await response.Content.ReadAsStringAsync();
					System.Diagnostics.Debug.WriteLine($"[LOG {nivel}] {response.StatusCode} - {responseBody}");
#endif
				}
				catch (Exception ex)
				{
					System.Diagnostics.Debug.WriteLine($"[LOG ERROR EN BACKGROUND] {usuario} | {origen} | {mensaje} | {ex.Message}");
				}
			});

			// 🚀 Devolvemos el control INMEDIATAMENTE al AuthController / VisualController
			return Task.FromResult(0);
		}
	}
}