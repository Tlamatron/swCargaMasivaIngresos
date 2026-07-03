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
	public class RateLimitHandler : DelegatingHandler
	{
		// 📌 Cache en memoria con expiración automática
		private static readonly MemoryCache _cache = MemoryCache.Default;

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
		private static object GetLockObject(string key)
		{
			var lockKey = key + "_lock";
			var existing = _cache.Get(lockKey);
			if (existing != null) return existing;

			var newLock = new object();
			_cache.Set(lockKey, newLock, new CacheItemPolicy { AbsoluteExpiration = DateTimeOffset.UtcNow.AddMinutes(5) });
			return newLock;
		}


		private class RequestCounter
		{
			public int Count { get; set; } = 0;
		}
	}
}
