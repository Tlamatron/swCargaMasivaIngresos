using swCargaMasivaIngresos.Services;
using System.Threading.Tasks;
using System.Web.Http;

namespace swCargaMasivaIngresos.Controllers
{
	/// <summary>
	/// Controlador encargado de gestionar las peticiones de sincronización con Web Services externos de los Municipios.
	/// </summary>
	[RoutePrefix("api/Sincronizacion")]
	public class SincronizacionController : ApiController
	{
		[HttpPost]
		[Route("Ejecutar/{oficinaId:int}")]
		public async Task<IHttpActionResult> EjecutarSincronizacion(int oficinaId, [FromBody] string usuarioLogin)
		{
			if (oficinaId <= 0) return BadRequest("Identificador de oficina inválido.");

			// Si el front-end no mandó usuario, ponemos uno por defecto para los logs
			if (string.IsNullOrWhiteSpace(usuarioLogin)) usuarioLogin = "SupervisorEstatal";

			// Disparamos el orquestador
			ResultadoProceso resultado = await SincronizacionService.EjecutarSincronizacionAsync(oficinaId, usuarioLogin);

			// Evaluamos el resultado
			if (resultado.RegistrosFallidos > 0)
			{
				return Content(System.Net.HttpStatusCode.InternalServerError, new
				{
					Exito = false,
					Mensaje = "No se pudo completar la sincronización. Verifique que el servicio del municipio esté activo.",
					DetallesErrores = resultado.ErroresDetalle
				});
			}

			return Ok(new
			{
				Exito = true,
				Mensaje = $"Sincronización exitosa con la Oficina {oficinaId}.",
				TotalRegistrosDescargados = resultado.RegistrosExitosos
			});
		}
	}
}