using Hangfire;
using swCargaMasivaIngresos.Models;
using swCargaMasivaIngresos.Services;
using System;
using System.Data;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web;
using System.Web.Http;

namespace swCargaMasivaIngresos.Controllers
{
	/// <summary>
	/// Controlador principal para gestionar la recepción de archivos masivos enviados en fragmentos (Chunks).
	/// </summary>
	[RoutePrefix("api/CargaMasiva")]
	public class CargaMasivaController : ApiController
	{
		// Leemos la ruta desde el Web.config para que en Producción lo puedas cambiar fácilmente.
		// Si no existe, usa la ruta por defecto.
		private readonly string RutaDirectorio = System.Configuration.ConfigurationManager.AppSettings["RutaTemporalCargas"] ?? @"C:\CargasIngresos\Temporales\";
		private readonly string AppName = System.Configuration.ConfigurationManager.AppSettings["NombAplicacion"] ?? "swCargaMasivaIngresos";

		/// <summary>
		/// Recibe un fragmento de archivo y lo almacena temporalmente. 
		/// Si es el último fragmento, ensambla el archivo completo y encola el procesamiento en segundo plano.
		/// </summary>
		/// <remarks>
		/// Se requiere que la petición sea de tipo multipart/form-data.
		/// Parámetros de formulario esperados: chunkNumber, totalChunks, fileName, usuarioLogin, oficinaId, tipoDocumento, periodoInicio, periodoFin, correoNotificacion y folioCarga (este último viaja vacío en el primer chunk).
		/// </remarks>
		/// <returns>Objeto RespuestaCarga con el estado del fragmento y el Folio de transacción asignado.</returns>
		[HttpPost]
		[Route("SubirFragmento")]
		public async Task<IHttpActionResult> SubirFragmento()
		{
			if (!Request.Content.IsMimeMultipartContent())
			{
				return Content(HttpStatusCode.UnsupportedMediaType, new RespuestaCarga { Exito = false, Mensaje = "Formato no soportado." });
			}

			try
			{
				if (!Directory.Exists(RutaDirectorio)) Directory.CreateDirectory(RutaDirectorio);

				var httpRequest = HttpContext.Current.Request;

				int chunkNumber = Convert.ToInt32(httpRequest.Form["chunkNumber"]);
				int totalChunks = Convert.ToInt32(httpRequest.Form["totalChunks"]);
				string nombreArchivoOriginal = httpRequest.Form["fileName"];
				string usuarioLogin = httpRequest.Form["usuarioLogin"];
				int tipoCargaId = Convert.ToInt32(httpRequest.Form["tipoDocumento"]);

				int folioCarga = 0;
				int.TryParse(httpRequest.Form["folioCarga"], out folioCarga);

				// Extraemos la oficina real y el nuevo municipio destino
				int oficinaIdReal = Convert.ToInt32(httpRequest.Form["oficinaId"]);
				int claveMunicipioDestino = 0;
				int.TryParse(httpRequest.Form["claveMunicipioDestino"], out claveMunicipioDestino);

				// Si el folio es 0, significa que es el primer chunk
				if (folioCarga == 0)
				{
					folioCarga = ControlCargasService.GenerarFolio(1);

					// 🚀 Pasamos ambos datos: quién sube (oficinaIdReal) y para quién es (claveMunicipioDestino)
					ControlCargasService.RegistrarInicio(folioCarga, oficinaIdReal, usuarioLogin, tipoCargaId, claveMunicipioDestino);

					await LogService.WriteLogAsync("INFO", usuarioLogin, "CargaMasivaController", $"Inicia recepción de archivo: {nombreArchivoOriginal}. Folio: {folioCarga}. TipoCarga: {tipoCargaId}");
				}

				if (httpRequest.Files.Count == 0) return BadRequest("No se encontró ningún archivo.");

				HttpPostedFile archivoChunk = httpRequest.Files[0];
				string rutaChunk = Path.Combine(RutaDirectorio, $"{folioCarga}_chunk_{chunkNumber}.tmp");
				archivoChunk.SaveAs(rutaChunk);

				bool cargaCompletada = (chunkNumber == totalChunks);

				if (cargaCompletada)
				{
					var parametros = new ParametrosCarga
					{
						FolioCarga = folioCarga, 
						UsuarioLogin = usuarioLogin,
						OficinaId = Convert.ToInt32(httpRequest.Form["oficinaId"]),
						TipoCargaId = tipoCargaId,
						CorreoNotificacion = httpRequest.Form["correoNotificacion"]
					};

					string rutaArchivoCompleto = ServicioEnsamblador.UnirFragmentos(folioCarga, nombreArchivoOriginal, totalChunks, RutaDirectorio);
					string extension = Path.GetExtension(nombreArchivoOriginal);

					BackgroundJob.Enqueue(() => MotorPrincipalCarga.EjecutarEnSegundoPlano(rutaArchivoCompleto, extension, parametros));

					await LogService.WriteLogAsync("INFO", usuarioLogin, "CargaMasivaController", $"Archivo ensamblado. Tarea encolada. Folio: {folioCarga}");
					ControlCargasService.ActualizarEstatus(folioCarga, "Encolado en Hangfire");
				}

				return Ok(new RespuestaCarga
				{
					Exito = true,
					Mensaje = cargaCompletada ? "Archivo encolado." : $"Fragmento {chunkNumber} recibido.",
					FolioCarga = folioCarga,
					FragmentoActual = chunkNumber,
					EnsambladoCompleto = cargaCompletada
				});
			}
			catch (Exception ex)
			{
				string usuarioFallo = HttpContext.Current.Request.Form["usuarioLogin"] ?? "Desconocido";
				string folioFallo = HttpContext.Current.Request.Form["folioCarga"] ?? "SinFolio";
				await LogService.WriteLogAsync("ERROR", usuarioFallo, "CargaMasivaController", $"[Folio: {folioFallo}] Error: {ex.Message}");
				return InternalServerError(ex);
			}
		}


		/// <summary>
		/// Consulta el estatus actual de una carga masiva mediante su folio.
		/// </summary>
		/// <param name="folioCarga">El folio único generado al iniciar la carga.</param>
		/// <returns>Un objeto JSON con el estatus y los contadores actuales.</returns>
		[HttpGet]
		[Route("Estatus/{folioCarga:int}")]
		public IHttpActionResult ConsultarEstatus(int folioCarga) // <-- Parámetro int
		{
			if (folioCarga <= 0) return BadRequest("El folio es inválido.");

			try
			{
				string cadenaConexion = ConfiguracionApp.ObtenerCadenaConexion();

				using (var conn = new System.Data.SqlClient.SqlConnection(cadenaConexion))
				using (var cmd = new System.Data.SqlClient.SqlCommand("pred_Operacion.sp_ConsultarEstatusCarga", conn))
				{
					cmd.CommandType = CommandType.StoredProcedure;
					cmd.Parameters.AddWithValue("@FolioCarga", folioCarga); // 🚀 Pasa como int
					conn.Open();

					using (var reader = cmd.ExecuteReader())
					{
						if (reader.Read())
						{
							return Ok(new
							{
								Folio = Convert.ToInt32(reader["FolioCarga"]), // 🚀 Lo leemos como int
								Estatus = reader["Estatus"].ToString(),
								Exitosos = Convert.ToInt32(reader["TotalExitosos"]),
								Fallidos = Convert.ToInt32(reader["TotalFallidos"]),
								Mensaje = reader["MensajeDetalle"]?.ToString(),
								Fecha = Convert.ToDateTime(reader["FechaRegistro"]).ToString("dd/MM/yyyy HH:mm")
							});
						}
					}
				}
				return NotFound();
			}
			catch (Exception ex)
			{
				return InternalServerError(ex);
			}
		}



	}
}