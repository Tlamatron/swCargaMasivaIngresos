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
			// 1. FILTRO DE AHORRO: Si es informativo, salimos inmediatamente para proteger el servidor de logs
			if (nivel.Equals("INFO", StringComparison.OrdinalIgnoreCase) ||
				nivel.Equals("OK", StringComparison.OrdinalIgnoreCase))
			{
				return Task.FromResult(0);
			}

			// 2. RECUPERACIÓN AUTOMÁTICA DE IDENTIDAD (Fallback para solicitudes Web)
			if (string.IsNullOrWhiteSpace(usuario))
			{
				usuario = System.Web.HttpContext.Current?.User?.Identity?.Name ?? "SISTEMA_ETL";
			}

			// 3. ENCOLA EL LOG EN SEGUNDO PLANO (Fire-and-Forget)
			HostingEnvironment.QueueBackgroundWorkItem(async cancellationToken =>
			{
				try
				{
					string ambiente = ConfigurationManager.AppSettings["Ambiente"] ?? "Test";
					string baseUrl = ambiente.Equals("Prod", StringComparison.OrdinalIgnoreCase)
						? ConfigurationManager.AppSettings["LogServiceUrlProd"]
						: ConfigurationManager.AppSettings["LogServiceUrlTest"];

					string urlFinal = $"{baseUrl}/{AppNameWebConfig}";

					string logMessage = $"| {usuario} | {nivel.ToUpper()} | {origen} | {mensaje}";
					var content = new StringContent($"\"{logMessage}\"", Encoding.UTF8, "application/json");

					await _httpClient.PostAsync(urlFinal, content, cancellationToken);
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

			return Task.FromResult(0);
		}
	}
}