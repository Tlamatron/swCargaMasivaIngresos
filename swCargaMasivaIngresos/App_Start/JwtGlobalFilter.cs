using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http.Controllers;
using System.Web.Http.Filters;

namespace swCargaMasivaIngresos
{
	public class JwtGlobalFilter : ActionFilterAttribute
	{
		public override async Task OnActionExecutingAsync(HttpActionContext actionContext, CancellationToken cancellationToken)
		{
			// Verificamos si la petición trae un token en el header "Authorization"
			if (actionContext.Request.Headers.Authorization != null &&
				actionContext.Request.Headers.Authorization.Scheme == "Bearer")
			{
				string tokenStr = actionContext.Request.Headers.Authorization.Parameter;

				try
				{
					var handler = new JwtSecurityTokenHandler();

					// Aseguramos que el token tiene un formato válido antes de leerlo
					if (handler.CanReadToken(tokenStr))
					{
						var jwtToken = handler.ReadJwtToken(tokenStr);

						// Extraemos los "Claims" (Datos del usuario)
						string usuario = jwtToken.Claims.FirstOrDefault(c => c.Type == "usuario")?.Value;
						string aplicacion = jwtToken.Claims.FirstOrDefault(c => c.Type == "aplicacion")?.Value;
						string rolId = jwtToken.Claims.FirstOrDefault(c => c.Type == "rolId")?.Value;
						string oficinaId = jwtToken.Claims.FirstOrDefault(c => c.Type == "oficinaId")?.Value;

						// Los guardamos en el diccionario de la petición actual
						// Este diccionario "vive" durante todo lo que dura la llamada a la API
						actionContext.Request.Properties["UsuarioActual"] = usuario ?? "Anonimo";
						actionContext.Request.Properties["AplicacionActual"] = aplicacion ?? "Desconocida";
						actionContext.Request.Properties["RolIdActual"] = rolId ?? "0";
						actionContext.Request.Properties["OficinaIdActual"] = oficinaId ?? "0";
					}
				}
				catch
				{
					// Si el token está mal formado, lo ignoramos y dejamos que 
					// la seguridad estándar de tu API (o [Authorize]) se encargue.
				}
			}

			await base.OnActionExecutingAsync(actionContext, cancellationToken);
		}
	}
}