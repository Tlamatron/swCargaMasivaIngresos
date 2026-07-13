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
	/// Gestor de documentos PDF para los usuarios. Permite descargar Actas de Entrega, Cartas de Confidencialidad y Credenciales de Acceso.
	/// </summary>
	[RoutePrefix("api/documentos")]
	public class DocumentosController : ApiController
	{
		/// <summary>
		/// Descarga el Acta de Entrega en formato PDF para un usuario específico.
		/// </summary>
		/// <param name="idUsuario"></param>
		/// <returns></returns>
		[HttpGet]
		[Route("ActaEntrega/{idUsuario}")]
		public async Task<IHttpActionResult> DescargarActaEntrega(int idUsuario)
		{
			try
			{
				var datos = ObtenerDatosUsuarioMock(idUsuario);
				byte[] pdfBytes = ServicioGeneradorDocumentos.GenerarActaEntrega(datos);

				return ResponseMessage(CrearRespuestaPdf(pdfBytes, $"ActaEntrega_{datos.NombreCompleto.Replace(" ", "_")}.pdf"));
			}
			catch (Exception ex)
			{
				// Aquí podrías usar tu LogService
				return InternalServerError(ex);
			}
		}

		/// <summary>
		/// Descarga la Carta de Confidencialidad en formato PDF para un usuario específico.
		/// </summary>
		/// <param name="idUsuario"></param>
		/// <returns></returns>
		[HttpGet]
		[Route("CartaConfidencialidad/{idUsuario}")]
		public async Task<IHttpActionResult> DescargarCartaConfidencialidad(int idUsuario)
		{
			try
			{
				var datos = ObtenerDatosUsuarioMock(idUsuario);
				byte[] pdfBytes = ServicioGeneradorDocumentos.GenerarCartaConfidencialidad(datos);

				return ResponseMessage(CrearRespuestaPdf(pdfBytes, $"Confidencialidad_{datos.NombreCompleto.Replace(" ", "_")}.pdf"));
			}
			catch (Exception ex)
			{
				return InternalServerError(ex);
			}
		}

		/// <summary>
		/// Descarga la Credencial de Acceso en formato PDF para un usuario específico.
		/// </summary>
		/// <param name="idUsuario"></param>
		/// <returns></returns>
		[HttpGet]
		[Route("Credencial/{idUsuario}")]
		public async Task<IHttpActionResult> DescargarCredencial(int idUsuario)
		{
			try
			{
				var datos = ObtenerDatosUsuarioMock(idUsuario);
				byte[] pdfBytes = ServicioGeneradorDocumentos.GenerarCredencialAcceso(datos);

				return ResponseMessage(CrearRespuestaPdf(pdfBytes, $"Credencial_{datos.NombreCompleto.Replace(" ", "_")}.pdf"));
			}
			catch (Exception ex)
			{
				return InternalServerError(ex);
			}
		}

		// ====================================================================
		// MÉTODOS AUXILIARES
		// ====================================================================

		/// <summary>
		/// Crea el formato HTTP correcto para que el navegador descargue un PDF en Web API 2.
		/// </summary>
		private HttpResponseMessage CrearRespuestaPdf(byte[] archivoBytes, string nombreArchivo)
		{
			var response = new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = new ByteArrayContent(archivoBytes)
			};

			// Avisamos que es un PDF
			response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
			// Avisamos que es un archivo adjunto para descargar
			response.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment")
			{
				FileName = nombreArchivo
			};

			return response;
		}

		/// <summary>
		/// TODO: Reemplazar esto con una consulta real a tu BD (Ej. SeguridadService.ObtenerUsuarioPorId(idUsuario))
		/// </summary>
		private DatosDocumentoUsuario ObtenerDatosUsuarioMock(int idUsuario)
		{
			return new DatosDocumentoUsuario
			{
				idUsuario = idUsuario,
				NombreCompleto = "Tlamatini Ortiz",
				ClaveMunicipio = "115",
				Rol = "Administrador Municipal"
			};
		}
	}
}
