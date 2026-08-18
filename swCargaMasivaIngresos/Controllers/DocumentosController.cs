using swCargaMasivaIngresos.Models;
using swCargaMasivaIngresos.Services.PDF;
using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.Web.Http;

namespace swCargaMasivaIngresos.Controllers
{
	[RoutePrefix("api/documentos")]
	public class DocumentosController : ApiController
	{
		/// <summary>
		/// Genera el paquete completo (Acta, Carta y Credencial) en un solo PDF.
		/// Se consume vía POST desde Postman enviando un JSON.
		/// </summary>
		[HttpPost]
		[Route("GenerarPaqueteAlta")]
		public async Task<IHttpActionResult> GenerarPaqueteAlta([FromBody] DatosDocumentoUsuario solicitud)
		{
			if (solicitud == null) return BadRequest("El JSON de solicitud está vacío.");

			try
			{
				// Delegamos la magia al servicio de PDF
				byte[] pdfBytes = await ServicioGeneradorHTMLtoPDF.GenerarDocumentoCompletoAsync(solicitud);

				var response = new HttpResponseMessage(HttpStatusCode.OK)
				{
					Content = new ByteArrayContent(pdfBytes)
				};

				response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
				response.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment")
				{
					FileName = $"PaqueteAcceso_{solicitud.UsuarioLogin}.pdf"
				};

				return ResponseMessage(response);
			}
			catch (Exception ex)
			{
				return InternalServerError(ex);
			}
		}
	}
}