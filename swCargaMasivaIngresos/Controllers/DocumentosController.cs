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
	/// <summary>
	/// Controlador API encargado de la generación de documentos PDF relacionados con usuarios, como el Paquete de Alta, Acta de Entrega, Carta de Confidencialidad y Credencial de Acceso. Este controlador recibe solicitudes con los datos del usuario y utiliza el servicio de generación de PDF para crear los documentos correspondientes, devolviéndolos como archivos adjuntos en la respuesta HTTP.
	/// </summary>
	[RoutePrefix("api/documentos")]
	public class DocumentosController : ApiController
	{
		/// <summary>
		/// Genera un paquete de alta en formato PDF para un usuario específico. Este método recibe un objeto JSON con los datos del usuario y utiliza una plantilla HTML para generar el documento PDF correspondiente. El PDF generado se devuelve como un archivo adjunto en la respuesta HTTP.
		/// </summary>
		/// <param name="solicitud"></param>
		/// <returns></returns>
		[HttpPost]
		[Route("GenerarPaqueteAlta")]
		public async Task<IHttpActionResult> GenerarPaqueteAlta([FromBody] DatosDocumentoUsuario solicitud)
		{
			if (solicitud == null) return BadRequest("El JSON de solicitud está vacío.");

			try
			{
				byte[] pdfBytes = await ServicioGeneradorHTMLtoPDF.GenerarDocumentoAsync(solicitud, "PlantillaPaqueteAlta");
				return CrearRespuestaPdf(pdfBytes, $"PaqueteAcceso_{solicitud.UsuarioLogin}.pdf");
			}
			catch (Exception ex)
			{
				return InternalServerError(ex);
			}
		}

		/// <summary>
		/// Genera un acta de entrega en formato PDF para un usuario específico. Este método recibe un objeto JSON con los datos del usuario y utiliza una plantilla HTML para generar el documento PDF correspondiente. El PDF generado se devuelve como un archivo adjunto en la respuesta HTTP.
		/// </summary>
		/// <param name="solicitud"></param>
		/// <returns></returns>
		[HttpPost]
		[Route("GenerarActaEntrega")]
		public async Task<IHttpActionResult> GenerarActaEntrega([FromBody] DatosDocumentoUsuario solicitud)
		{
			if (solicitud == null) return BadRequest("El JSON de solicitud está vacío.");

			try
			{
				byte[] pdfBytes = await ServicioGeneradorHTMLtoPDF.GenerarDocumentoAsync(solicitud, "ActaEntrega");
				return CrearRespuestaPdf(pdfBytes, $"ActaEntrega_{solicitud.UsuarioLogin}.pdf");
			}
			catch (Exception ex)
			{
				return InternalServerError(ex);
			}
		}

		/// <summary>
		/// Genera una carta de confidencialidad en formato PDF para un usuario específico. Este método recibe un objeto JSON con los datos del usuario y utiliza una plantilla HTML para generar el documento PDF correspondiente. El PDF generado se devuelve como un archivo adjunto en la respuesta HTTP.
		/// </summary>
		/// <param name="solicitud"></param>
		/// <returns></returns>
		[HttpPost]
		[Route("GenerarCartaConfidencialidad")]
		public async Task<IHttpActionResult> GenerarCartaConfidencialidad([FromBody] DatosDocumentoUsuario solicitud)
		{
			if (solicitud == null) return BadRequest("El JSON de solicitud está vacío.");

			try
			{
				byte[] pdfBytes = await ServicioGeneradorHTMLtoPDF.GenerarDocumentoAsync(solicitud, "CartaConfidencialidad");
				return CrearRespuestaPdf(pdfBytes, $"CartaConfidencialidad_{solicitud.UsuarioLogin}.pdf");
			}
			catch (Exception ex)
			{
				return InternalServerError(ex);
			}
		}

		/// <summary>
		/// Genera una credencial de acceso en formato PDF para un usuario específico. Este método recibe un objeto JSON con los datos del usuario y utiliza una plantilla HTML para generar el documento PDF correspondiente. El PDF generado se devuelve como un archivo adjunto en la respuesta HTTP.
		/// </summary>
		/// <param name="solicitud"></param>
		/// <returns></returns>
		[HttpPost]
		[Route("GenerarCredencial")]
		public async Task<IHttpActionResult> GenerarCredencial([FromBody] DatosDocumentoUsuario solicitud)
		{
			if (solicitud == null) return BadRequest("El JSON de solicitud está vacío.");

			try
			{
				byte[] pdfBytes = await ServicioGeneradorHTMLtoPDF.GenerarDocumentoAsync(solicitud, "CredencialAcceso");
				return CrearRespuestaPdf(pdfBytes, $"Credencial_{solicitud.UsuarioLogin}.pdf");
			}
			catch (Exception ex)
			{
				return InternalServerError(ex);
			}
		}

		/// <summary>
		/// Crea una respuesta HTTP que contiene un archivo PDF como contenido adjunto. Este método configura los encabezados de la respuesta para indicar que el contenido es un archivo PDF y establece el nombre del archivo que se descargará. Se utiliza internamente en los métodos de generación de documentos para devolver el PDF generado al cliente.
		/// </summary>
		/// <param name="pdfBytes"></param>
		/// <param name="nombreArchivo"></param>
		/// <returns></returns>
		private IHttpActionResult CrearRespuestaPdf(byte[] pdfBytes, string nombreArchivo)
		{
			var response = new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = new ByteArrayContent(pdfBytes)
			};
			response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
			response.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment")
			{
				FileName = nombreArchivo
			};
			return ResponseMessage(response);
		}
	}
}