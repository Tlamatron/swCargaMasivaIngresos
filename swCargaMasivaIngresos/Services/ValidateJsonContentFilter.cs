using System.Net;
using System.Net.Http;
using System.Web.Http.Controllers;
using System.Web.Http.Filters;

namespace swCargaMasivaIngresos.Services
{
	/// <summary>
	/// Filtro de seguridad perimetral que asegura que las peticiones POST y PUT tengan un Content-Type válido.
	/// Permite application/json para datos estándar y multipart/form-data para la carga de archivos.
	/// </summary>
	public class ValidateJsonContentFilter : ActionFilterAttribute
	{
		public override void OnActionExecuting(HttpActionContext actionContext)
		{
			var method = actionContext.Request.Method;

			// Solo validar si el método es POST o PUT (Los GET no llevan Body)
			if (method == HttpMethod.Post || method == HttpMethod.Put)
			{
				var contentType = actionContext.Request.Content.Headers.ContentType?.MediaType;

				// 🛡️ REGLA: Si NO es JSON y TAMPOCO es un envío de archivos (multipart), lo bloqueamos.
				if (contentType != "application/json" &&
					(contentType == null || !contentType.StartsWith("multipart/form-data")))
				{
					actionContext.Response = actionContext.Request.CreateResponse(
						HttpStatusCode.UnsupportedMediaType,
						"Formato no soportado. El Content-Type debe ser application/json o multipart/form-data."
					);
				}
			}
		}
	}
}