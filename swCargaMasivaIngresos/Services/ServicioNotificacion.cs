using swCargaMasivaIngresos.Models;
using System;
using System.IO;
using System.Linq;
using System.Net.Mail;
using System.Text;
using System.Threading.Tasks;

namespace swCargaMasivaIngresos.Services
{
	/// <summary>
	/// Clase estática encargada de enviar correos electrónicos de notificación al usuario una vez que el proceso de carga masiva ha finalizado, resumiendo los resultados y detallando cualquier error encontrado en el archivo.
	/// </summary>
	public static class ServicioNotificacion
	{
		private static readonly string AppName = System.Configuration.ConfigurationManager.AppSettings["NombAplicacion"] ?? "APICargaMasivaIngresos";
		
		/// <summary>
		/// Sends an HTML notification email summarizing the results of a bulk-load process.
		/// </summary>
		/// <remarks>SMTP settings are read from application configuration. Builds an institutional HTML body, limits
		/// listed error details to 100 entries, and logs failures without aborting the load.</remarks>
		/// <param name="parametros">Parameters for the bulk load, including folio, office identifier, user and related configuration values.</param>
		/// <param name="resultado">Process result summary including counts of successful and failed records and an optional collection of error
		/// details.</param>
		/// <param name="correoUsuario">Recipient email address for the notification.</param>
		/// <returns>A task that represents the asynchronous operation.</returns>
		public static async Task EnviarCorreoNotificacion(ParametrosCarga parametros, ResultadoProceso resultado, string correoUsuario)
		{
			try
			{
				// Configuraciones SMTP leídas del Web.config
				string smtpHost = System.Configuration.ConfigurationManager.AppSettings["HostRemitente"];
				int smtpPort = Convert.ToInt32(System.Configuration.ConfigurationManager.AppSettings["SmtpPort"] ?? "587");
				string remitente = System.Configuration.ConfigurationManager.AppSettings["CorreoRemitente"];
				string smtpPass = System.Configuration.ConfigurationManager.AppSettings["PwdRemitente"];

				using (SmtpClient clienteSmtp = new SmtpClient(smtpHost, smtpPort))
				{
					clienteSmtp.Credentials = new System.Net.NetworkCredential(remitente, smtpPass);
					clienteSmtp.EnableSsl = true;

					using (MailMessage mensaje = new MailMessage())
					{
						// 1. Remitente y Destinatario PRINCIPAL (El usuario que capturó en pantalla)
						mensaje.From = new MailAddress(remitente, "Sistema de Ingresos Puebla");

						// 🚀 CORRECCIÓN: Ahora sí usamos el correo ingresado en el formulario
						if (!string.IsNullOrWhiteSpace(correoUsuario))
						{
							//mensaje.To.Add(new MailAddress(correoUsuario));
							mensaje.To.Add(correoUsuario); // ¡Así de simple!
						}

						// 2. Copias de Monitoreo Institucional (Solo en Producción)
						string ambiente = System.Configuration.ConfigurationManager.AppSettings["Ambiente"] ?? "Test";
						if (ambiente.Equals("Prod", StringComparison.OrdinalIgnoreCase))
						{
							// Leemos los correos de los encargados desde el Web.config
							string correoAdmin = System.Configuration.ConfigurationManager.AppSettings["CorreoDestinatario"];
							string correoJefa = System.Configuration.ConfigurationManager.AppSettings["CorreoDestinatarioC"];

							// Agregamos con Copia Oculta (BCC) para no saturar al usuario
							if (!string.IsNullOrWhiteSpace(correoAdmin))
								mensaje.Bcc.Add(new MailAddress(correoAdmin));

							if (!string.IsNullOrWhiteSpace(correoJefa))
								mensaje.Bcc.Add(new MailAddress(correoJefa));
						}
						else
						{
							// En ambientes de prueba, enviamos copia al desarrollador para monitoreo
							string correoDesarrollo = System.Configuration.ConfigurationManager.AppSettings["CorreoDestinatario"];
							if (!string.IsNullOrWhiteSpace(correoDesarrollo))
								mensaje.Bcc.Add(new MailAddress(correoDesarrollo));
						}
						// 3. Asunto y cuerpo
						mensaje.Subject = $"Resultados de Carga Masiva - Folio: {parametros.FolioCarga}";
						mensaje.IsBodyHtml = true;

						StringBuilder sbBody = new StringBuilder();
						sbBody.AppendLine("<h2>Reporte de Carga de Archivo</h2>");
						sbBody.AppendLine($"<p>Estimado usuario de la oficina <b>{parametros.OficinaId}</b>,</p>");
						sbBody.AppendLine($"<p>Le notificamos que el proceso de su archivo (Folio: <b>{parametros.FolioCarga}</b>) ha finalizado.</p>");

						sbBody.AppendLine("<h3>Resumen:</h3>");
						sbBody.AppendLine("<ul>");
						sbBody.AppendLine($"<li><b>Registros insertados exitosamente:</b> <span style='color:green'>{resultado.RegistrosExitosos}</span></li>");
						sbBody.AppendLine($"<li><b>Registros rechazados (Error de formato):</b> <span style='color:red'>{resultado.RegistrosFallidos}</span></li>");
						sbBody.AppendLine("</ul>");

						// Si hubo errores, los enlistamos (máximo 100 para no saturar el correo)
						// Si hubo errores, generamos un archivo adjunto para Excel (CSV)
						if (resultado.RegistrosFallidos > 0 && resultado.ErroresDetalle != null && resultado.ErroresDetalle.Count > 0)
						{
							sbBody.AppendLine("<h3>Detalle de Errores Encontrados:</h3>");
							sbBody.AppendLine("<p>Debido a la cantidad de observaciones, se ha adjuntado un archivo a este correo con el detalle completo para su revisión en Excel.</p>");

							// 1. Construimos el contenido del Excel (CSV)
							StringBuilder csvContent = new StringBuilder();
							// Encabezados de las columnas en Excel
							csvContent.AppendLine("No. Error,Detalle del Error");

							int contador = 1;
							foreach (var error in resultado.ErroresDetalle)
							{
								// Limpiamos las comillas por seguridad para no romper las columnas de Excel
								string errorLimpio = error.Replace("\"", "\"\"");

								// Agregamos la fila (Separamos las columnas con coma)
								csvContent.AppendLine($"{contador},\"{errorLimpio}\"");
								contador++;
							}

							// 2. Convertimos el texto en un archivo virtual en la memoria RAM
							// Usamos UTF8 con BOM para que el Excel en español reconozca los acentos perfectamente
							byte[] buffer = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csvContent.ToString())).ToArray();
							MemoryStream ms = new MemoryStream(buffer);

							// 3. Adjuntamos el archivo al correo
							string nombreArchivoAdjunto = $"Errores_Carga_{parametros.FolioCarga}.csv";
							mensaje.Attachments.Add(new Attachment(ms, nombreArchivoAdjunto, "text/csv"));
						}
						//if (resultado.RegistrosFallidos > 0 && resultado.ErroresDetalle != null)
						//{
						//	sbBody.AppendLine("<h3>Detalle de Errores Encontrados:</h3>");
						//	sbBody.AppendLine("<ul>");

						//	int contador = 0;
						//	foreach (var error in resultado.ErroresDetalle)
						//	{
						//		if (contador >= 100)
						//		{
						//			sbBody.AppendLine("<li><i>... y otros errores más. Por favor revise su archivo.</i></li>");
						//			break;
						//		}
						//		sbBody.AppendLine($"<li>{error}</li>");
						//		contador++;
						//	}
						//	sbBody.AppendLine("</ul>");
						//}

						sbBody.AppendLine("<hr/>");
						string appName = System.Configuration.ConfigurationManager.AppSettings["NombAplicacion"] ?? "Cargas Masivas";
						sbBody.AppendLine($"<p><small>Este es un mensaje automático generado por el sistema {appName} del Gobierno del Estado de Puebla. No responda a esta dirección.</small></p>");

						mensaje.Body = sbBody.ToString();

						// Disparamos el correo
						await clienteSmtp.SendMailAsync(mensaje);
					}
				}
			}
			catch (Exception ex)
			{
				// Si el correo falla, no cancelamos la inserción en BD, pero lo registramos en tu Log
				await LogService.WriteLogAsync(AppName, "ERROR", parametros.UsuarioLogin, "ServicioNotificacion", $"[Folio: {parametros.FolioCarga}] ERROR NOTIFICACIÓN: Falló el envío de correo a {correoUsuario}. Detalle: {ex.Message}");
			}
		}
	}
}