using System.Net;
using System.Net.Http;
using System.Web.Http.ExceptionHandling;
using System.Web.Http.Results;

namespace swCargaMasivaIngresos.Services
{
	public class GlobalExceptionHandler : ExceptionHandler
	{
		public override void Handle(ExceptionHandlerContext context)
		{
			// Respuesta institucional uniforme en caso de error no controlado catastrófico
			var response = context.Request.CreateResponse(
				HttpStatusCode.InternalServerError,
				new
				{
					error = true,
					mensaje = "Error interno en el servidor de Cargas Masivas. Contacte al área de soporte técnico."
				}
			);

			context.Result = new ResponseMessageResult(response);
		}
	}
}