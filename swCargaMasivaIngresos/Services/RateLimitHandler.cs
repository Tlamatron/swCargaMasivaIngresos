using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace swCargaMasivaIngresos.Services
{
	/// <summary>
	/// Manejador global (Handler) para limitar la cantidad de peticiones por cliente (Rate Limiting) 
	/// y prevenir sobrecargas en el servidor web.
	/// </summary>
	public class RateLimitHandler : DelegatingHandler
	{
		// Diccionario en memoria ultra rápido y seguro para hilos múltiples
		private static readonly ConcurrentDictionary<string, RequestCounter> _counters = new ConcurrentDictionary<string, RequestCounter>();

		private const int LIMIT = 50; // Máximo de peticiones permitidas
		private static readonly TimeSpan WINDOW = TimeSpan.FromMinutes(1); // En una ventana de 1 minuto

		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			// Usamos el Token JWT o Authorization como clave principal. Si no existe, usamos la IP.
			string clientKey = request.Headers.Contains("Authorization")
				? request.Headers.GetValues("Authorization").FirstOrDefault()
				: (request.Headers.Contains("Token")
					? request.Headers.GetValues("Token").FirstOrDefault()
					: GetClientIp(request) ?? "anonimo");

			var counter = _counters.GetOrAdd(clientKey, _ => new RequestCounter());

			lock (counter)
			{
				// Si ya pasó el minuto, reiniciamos su contador
				if (DateTime.UtcNow - counter.WindowStart >= WINDOW)
				{
					counter.Count = 0;
					counter.WindowStart = DateTime.UtcNow;
				}

				counter.Count++;

				// Si excede el límite de 50 peticiones por minuto, cortamos la comunicación
				if (counter.Count > LIMIT)
				{
					var response = new HttpResponseMessage((HttpStatusCode)429) // 429 = Too Many Requests
					{
						Content = new StringContent($"El cliente ha excedido el límite seguro de {LIMIT} peticiones por minuto.")
					};

					// Le decimos al navegador cuántos segundos esperar antes de volver a intentar
					response.Headers.Add("Retry-After", ((int)WINDOW.TotalSeconds).ToString());
					return response;
				}
			}

			return await base.SendAsync(request, cancellationToken);
		}

		private string GetClientIp(HttpRequestMessage request)
		{
			if (request.Properties.ContainsKey("MS_HttpContext"))
			{
				return ((HttpContextWrapper)request.Properties["MS_HttpContext"]).Request.UserHostAddress;
			}
			return null;
		}
	}

	/// <summary>
	/// Clase de apoyo para el control de tiempo y conteo del Rate Limit.
	/// </summary>
	public class RequestCounter
	{
		public int Count { get; set; }
		public DateTime WindowStart { get; set; } = DateTime.UtcNow;
	}
}