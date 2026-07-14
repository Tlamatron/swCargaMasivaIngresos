using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.Caching;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace swCargaMasivaIngresos.Services
{
	/// <summary>
	/// Middleware para limitar la cantidad de peticiones que un cliente puede hacer a la API en un período de tiempo determinado. Se aplican diferentes límites para rutas de autenticación y rutas generales, y se utiliza un cache en memoria para llevar el conteo de las solicitudes por cliente.
	/// </summary>
	public class RateLimitHandler : DelegatingHandler
	{
		// 📌 Cache en memoria con expiración automática
		private static readonly MemoryCache _cache = MemoryCache.Default;

		/// <summary>
		/// Maneja la lógica de limitación de peticiones. Identifica si la ruta es de login o general, obtiene la clave del cliente (ya sea por token o IP), y verifica si el cliente ha excedido el límite de peticiones permitido. Si se excede, devuelve un código 429 (Too Many Requests) con un mensaje y un encabezado "Retry-After".
		/// </summary>
		/// <param name="request"></param>
		/// <param name="cancellationToken"></param>
		/// <returns></returns>
		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			// 1. Identificar si es ruta de Login
			bool isLoginRoute = request.RequestUri.AbsolutePath.ToLower().Contains("/api/login") ||
								request.RequestUri.AbsolutePath.ToLower().Contains("/auth");

			// 2. Definir reglas
			int limit = isLoginRoute ? 3 : 200;
			TimeSpan window = isLoginRoute ? TimeSpan.FromSeconds(10) : TimeSpan.FromMinutes(1);

			// 3. Obtener clave del cliente
			string clientKey = GetClientKey(request);
			string cacheKey = $"RateLimit_{clientKey}_{(isLoginRoute ? "Auth" : "Gen")}";

			RequestCounter counter;

			// 4. Manejo concurrente por clave
			lock (GetLockObject(cacheKey))
			{
				if (_cache.Contains(cacheKey))
				{
					counter = (RequestCounter)_cache.Get(cacheKey);
				}
				else
				{
					counter = new RequestCounter();
					_cache.Set(cacheKey, counter, new CacheItemPolicy
					{
						AbsoluteExpiration = DateTimeOffset.UtcNow.Add(window)
					});
				}

				counter.Count++;

				// 5. Validar límite
				if (counter.Count > limit)
				{
					var response = new HttpResponseMessage((HttpStatusCode)429)
					{
						Content = new StringContent($"El cliente excedió el límite de {limit} peticiones en {window.TotalSeconds} segundos.")
					};
					response.Headers.Add("Retry-After", ((int)window.TotalSeconds).ToString());
					return response;
				}
			}

			return await base.SendAsync(request, cancellationToken);
		}

		/// <summary>
		/// Método privado que obtiene la clave del cliente a partir de la solicitud HTTP. Primero intenta obtenerla del encabezado de autorización estándar, luego de un encabezado personalizado "Token", y finalmente, si no se encuentra ninguna de las anteriores, utiliza la dirección IP del cliente como clave.
		/// </summary>
		/// <param name="request"></param>
		/// <returns></returns>
		private string GetClientKey(HttpRequestMessage request)
		{
			// Authorization estándar
			if (request.Headers.Authorization != null && !string.IsNullOrWhiteSpace(request.Headers.Authorization.Parameter))
			{
				return request.Headers.Authorization.Parameter;
			}

			// Token personalizado
			if (request.Headers.Contains("Token"))
			{
				return request.Headers.GetValues("Token").FirstOrDefault();
			}

			// Fallback a IP
			return GetClientIp(request) ?? "anon";
		}

		/// <summary>
		/// Método privado que obtiene la dirección IP del cliente a partir de la solicitud HTTP. Intenta obtenerla de los encabezados de proxy (X-Forwarded-For) y, si no está disponible, utiliza la dirección IP del host remoto.
		/// </summary>
		/// <param name="request"></param>
		/// <returns></returns>
		private string GetClientIp(HttpRequestMessage request)
		{
			if (request.Properties.ContainsKey("MS_HttpContext"))
			{
				var ctx = ((HttpContextWrapper)request.Properties["MS_HttpContext"]).Request;

				string forwardedIp = ctx.ServerVariables["HTTP_X_FORWARDED_FOR"];
				if (!string.IsNullOrEmpty(forwardedIp))
				{
					return forwardedIp.Split(',')[0].Trim();
				}

				return ctx.UserHostAddress;
			}
			return "unknown";
		}

		// 📌 Lock por clave para granularidad
		private static object GetLockObject_v01(string key)
		{
			return _cache.GetCacheItem(key + "_lock")?.Value ?? new object();
		}

		/// <summary>
		/// Método privado que obtiene un objeto de bloqueo único para una clave específica. Si no existe un objeto de bloqueo para esa clave, se crea uno nuevo y se almacena en la caché con una expiración de 5 minutos. Esto permite manejar la concurrencia de manera segura al actualizar el contador de solicitudes por cliente.
		/// </summary>
		/// <param name="key"></param>
		/// <returns></returns>
		private static object GetLockObject(string key)
		{
			var lockKey = key + "_lock";
			var existing = _cache.Get(lockKey);
			if (existing != null) return existing;

			var newLock = new object();
			_cache.Set(lockKey, newLock, new CacheItemPolicy { AbsoluteExpiration = DateTimeOffset.UtcNow.AddMinutes(5) });
			return newLock;
		}

		/// <summary>
		/// Clase privada que representa un contador de solicitudes para un cliente específico. Se utiliza para llevar un registro del número de solicitudes realizadas por el cliente dentro de un período de tiempo determinado.
		/// </summary>
		private class RequestCounter
		{
			/// <summary>
			/// Contador de solicitudes realizadas por el cliente. Se incrementa cada vez que el cliente realiza una solicitud y se utiliza para verificar si el cliente ha excedido el límite de solicitudes permitido.
			/// </summary>
			public int Count { get; set; } = 0;
		}
	}
}
