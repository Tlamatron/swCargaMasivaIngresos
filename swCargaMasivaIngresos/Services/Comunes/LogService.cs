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
		// Centralizamos el nombre de la aplicación leyendo la llave del Web.config
		private static readonly string AppNameWebConfig = ConfigurationManager.AppSettings["NombAplicacion"] ?? "APICargaMasivaIngresos";

		private static readonly HttpClient _httpClient = new HttpClient
		{
			Timeout = TimeSpan.FromSeconds(3)
		};

		/// <summary>
		/// Escribe un log de error o advertencia en segundo plano. Filtra eventos de información (INFO/OK).
		/// </summary>
		public static Task WriteLogAsync(string nivel, string usuario, string origen, string mensaje)
		{
			// 🚀 1. DETECTAR EL AMBIENTE DESDE WEB.CONFIG
			string ambiente = ConfigurationManager.AppSettings["Ambiente"] ?? "Test";
			bool esProduccion = ambiente.Equals("Prod", StringComparison.OrdinalIgnoreCase) ||
								ambiente.Equals("Produccion", StringComparison.OrdinalIgnoreCase);

			// 🚀 2. FILTRO INTELIGENTE: Solo bloqueamos los INFO/OK si estamos en Producción.
			// Si estamos en Dev/Test, dejamos pasar todo para depurar a gusto.
			if (esProduccion)
			{
				if (nivel.Equals("INFO", StringComparison.OrdinalIgnoreCase) ||
					nivel.Equals("OK", StringComparison.OrdinalIgnoreCase))
				{
					return Task.FromResult(0);
				}
			}

			// 3. RECUPERACIÓN AUTOMÁTICA DE IDENTIDAD (Fallback para solicitudes Web)
			if (string.IsNullOrWhiteSpace(usuario))
			{
				usuario = System.Web.HttpContext.Current?.User?.Identity?.Name ?? "SISTEMA_ETL";
			}

			// 4. ENCOLA EL LOG EN SEGUNDO PLANO (Fire-and-Forget)
			HostingEnvironment.QueueBackgroundWorkItem(async cancellationToken =>
			{
				try
				{
					string baseUrl = esProduccion
						? ConfigurationManager.AppSettings["LogServiceUrlProd"]
						: ConfigurationManager.AppSettings["LogServiceUrlTest"];

					string urlFinal = $"{baseUrl}/{AppNameWebConfig}";

					string logMessage = $"| {usuario} | {nivel.ToUpper()} | {origen} | {mensaje}";
					var content = new StringContent($"\"{logMessage}\"", Encoding.UTF8, "application/json");

					var response = await _httpClient.PostAsync(urlFinal, content, cancellationToken);
#if DEBUG
					string responseBody = await response.Content.ReadAsStringAsync();
					System.Diagnostics.Debug.WriteLine($"[LOG {nivel}] {response.StatusCode} - {responseBody}");
#endif
				}
				catch (Exception ex)
				{
					System.Diagnostics.Debug.WriteLine($"Fallo crítico al intentar guardar el log: {ex.Message}");
				}
			});

			return Task.FromResult(0);
		}
	}
}