using System;
using System.Data;
using System.Data.SqlClient;
using System.Runtime.Caching;
using System.Threading.Tasks;

namespace swCargaMasivaIngresos.Services.Comunes
{
	/// <summary>
	/// Servicio de seguridad para validar permisos de ejecución de SPs.
	/// </summary>
	public class SeguridadService
	{
		// Instancia global de caché en memoria de .NET Framework
		private static readonly MemoryCache _cache = MemoryCache.Default;

		/// <summary>
		/// Valida si un usuario tiene permiso para ejecutar un SP.
		/// Utiliza memoria caché por 10 minutos para no saturar la base de datos.
		/// </summary>
		public async Task<bool> TienePermisoEjecucionAsync(string usuarioLogin, string nombreSp, string cadenaConexion, int App)
		{
			// 1. Generamos una llave única para esta combinación Usuario-SP
			string cacheKey = $"Permiso_{App}_{usuarioLogin}_{nombreSp}";

			// 2. Revisamos si el resultado ya está en la memoria RAM
			if (_cache.Contains(cacheKey))
			{
				return (bool)_cache.Get(cacheKey);
			}

			// 3. Si no está en memoria, vamos a SQL Server
			bool tienePermiso = false;

			using (SqlConnection conn = new SqlConnection(cadenaConexion))
			{
				await conn.OpenAsync();

				using (SqlCommand cmd = new SqlCommand("pred_Seguridad.sp_ValidarPermisoEjecucion", conn))
				{
					cmd.CommandType = CommandType.StoredProcedure;
					cmd.Parameters.AddWithValue("@UsuarioLogin", usuarioLogin);
					cmd.Parameters.AddWithValue("@NombreSP", nombreSp);
					cmd.Parameters.AddWithValue("@AppId", App);

					// El SP devuelve 1 (Autorizado) o 0 (Denegado)
					object result = await cmd.ExecuteScalarAsync();
					if (result != null && result != DBNull.Value)
					{
						tienePermiso = Convert.ToInt32(result) == 1;
					}
				}
			}

			// 4. Guardar el resultado en caché por 10 minutos
			// NOTA: Guardamos tanto los true como los false. Así, si un atacante intenta
			// ejecutar un SP prohibido 1000 veces, SQL Server solo recibe 1 consulta.
			CacheItemPolicy policy = new CacheItemPolicy
			{
				AbsoluteExpiration = DateTimeOffset.Now.AddMinutes(10)
			};

			_cache.Set(cacheKey, tienePermiso, policy);

			return tienePermiso;
		}
	}
}