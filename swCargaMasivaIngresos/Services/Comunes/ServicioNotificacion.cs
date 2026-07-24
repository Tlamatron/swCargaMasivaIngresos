using swCargaMasivaIngresos.Models;
using System;
using System.Data;
using System.IO;
using System.Linq;
using System.Net.Mail;
using System.Text;
using System.Threading.Tasks;

namespace swCargaMasivaIngresos.Services
{
	/// <summary>
	/// Clase de servicio responsable de enviar notificaciones por correo electrónico a los usuarios después de procesar una carga masiva. Se encarga de construir el contenido del correo, adjuntar archivos CSV con errores si es necesario y manejar reintentos en caso de fallos en el envío SMTP. Además, notifica a los administradores si el correo hacia el usuario final falla tras varios intentos.
	/// </summary>
	public static class ServicioNotificacion
	{
		private static readonly string AppName = System.Configuration.ConfigurationManager.AppSettings["NombAplicacion"] ?? "APICargaMasivaIngresos";

		/// <summary>
		/// Envía un correo de notificación al usuario después de procesar una carga masiva, incluyendo detalles del resultado y adjuntando un archivo CSV con los registros rechazados si es necesario. Implementa reintentos en caso de fallos en el envío SMTP y notifica a los administradores si el correo hacia el usuario final falla tras varios intentos.
		/// </summary>
		/// <param name="parametros"></param>
		/// <param name="resultado"></param>
		/// <param name="correoUsuario"></param>
		/// <returns></returns>
		public static async Task EnviarCorreoNotificacion(ParametrosCarga parametros, ResultadoProceso resultado, string correoUsuario)
		{
			try
			{
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
						mensaje.From = new MailAddress(remitente, "Sistema de Ingresos Puebla");

						if (!string.IsNullOrWhiteSpace(correoUsuario))
						{
							mensaje.To.Add(correoUsuario);
						}

						string ambiente = System.Configuration.ConfigurationManager.AppSettings["Ambiente"] ?? "Test";
						if (ambiente.Equals("Prod", StringComparison.OrdinalIgnoreCase))
						{
							string correoAdmin = System.Configuration.ConfigurationManager.AppSettings["CorreoDestinatario"];
							string correoJefa = System.Configuration.ConfigurationManager.AppSettings["CorreoDestinatarioC"];

							if (!string.IsNullOrWhiteSpace(correoAdmin)) mensaje.Bcc.Add(new MailAddress(correoAdmin));
							if (!string.IsNullOrWhiteSpace(correoJefa)) mensaje.Bcc.Add(new MailAddress(correoJefa));
						}
						else
						{
							string correoDesarrollo = System.Configuration.ConfigurationManager.AppSettings["CorreoDestinatario"];
							if (!string.IsNullOrWhiteSpace(correoDesarrollo)) mensaje.Bcc.Add(new MailAddress(correoDesarrollo));
						}

						// 🚀 Agregamos el identificador del Municipio al ASUNTO (Como lo platicamos antes)
						string tituloMpio = parametros.ClaveMunicipioDestino > 0 ? $" [Mpio: {parametros.ClaveMunicipioDestino}]" : "";
						mensaje.Subject = $"Resultados de Carga Masiva - Folio: {parametros.FolioCarga}{tituloMpio}";
						mensaje.IsBodyHtml = true;

						// 🚀 Determinar el Tipo de Archivo procesado
						string nombreTipoCarga = "Desconocido";
						switch (parametros.TipoCargaId)
						{
							case 1: nombreTipoCarga = "Padrón Catastral (Alta de Predios)"; break;
							case 2: nombreTipoCarga = "Pagos Locales (Etiquetado)"; break;
							case 3: nombreTipoCarga = "Reducciones y Descuentos"; break;
						}

						StringBuilder sbBody = new StringBuilder();
						sbBody.AppendLine("<h2>Reporte de Carga de Archivo</h2>");
						sbBody.AppendLine($"<p>Estimado usuario de la oficina <b>{parametros.OficinaId}</b>,</p>");

						sbBody.AppendLine($"<p><b>Tipo de Carga procesada:</b> {nombreTipoCarga}</p>");

						// 🚀 Agregamos el Municipio al CUERPO del correo
						if (parametros.ClaveMunicipioDestino > 0)
						{
							sbBody.AppendLine($"<p><b>Municipio destino procesado:</b> Clave {parametros.ClaveMunicipioDestino}</p>");
						}

						sbBody.AppendLine($"<p>Le notificamos que el proceso de su archivo (Folio: <b>{parametros.FolioCarga}</b>) ha finalizado.</p>");

						// =========================================================================
						// DETECCIÓN VISUAL DE ERROR FATAL
						// =========================================================================
						// 🚀 FIX: Un error es fatal SOLO si no hubo NI exitosos NI fallidos de negocio reportados. 
						// Si hay fallidos contados matemáticamente (ej. 1317), entonces fue un escaneo exitoso que rebotó en reglas de negocio.

						//bool esErrorFatal = resultado.RegistrosExitosos == 0 && resultado.TablaRechazados == null && resultado.ErroresDetalle != null && resultado.ErroresDetalle.Count > 0;

						bool esErrorFatal = resultado.RegistrosExitosos == 0
										 && resultado.RegistrosFallidos == 0
										 && resultado.ErroresDetalle != null
										 && resultado.ErroresDetalle.Count > 0;

						if (esErrorFatal)
						{
							sbBody.AppendLine($"<div style='background-color:#ffe6e6; border-left: 6px solid #cc0000; padding: 15px; margin: 20px 0;'>");
							sbBody.AppendLine($"<h3 style='color:#cc0000; margin-top: 0;'>⚠️ No se pudo procesar el archivo</h3>");
							sbBody.AppendLine($"<p>El sistema abortó la lectura del archivo porque no contiene el formato esperado, está vacío o le faltan los encabezados obligatorios (Cuenta Predial, Bimestre, etc.).</p>");
							sbBody.AppendLine($"<p><b>Detalle técnico:</b> <i>{resultado.ErroresDetalle.FirstOrDefault()}</i></p>");
							sbBody.AppendLine($"</div>");
							sbBody.AppendLine($"<p>Por favor, corrija el archivo fuente e intente cargarlo nuevamente.</p>");
						}
						else
						{
							// EL FLUJO NORMAL
							sbBody.AppendLine("<h3>Resumen:</h3>");
							sbBody.AppendLine("<ul>");
							sbBody.AppendLine($"<li><b>Registros insertados exitosamente:</b> <span style='color:green'>{resultado.RegistrosExitosos}</span></li>");
							sbBody.AppendLine($"<li><b>Registros rechazados (Error de formato):</b> <span style='color:red'>{resultado.RegistrosFallidos}</span></li>");
							sbBody.AppendLine("</ul>");

							// LÓGICA DE EXPORTACIÓN A CSV
							if (resultado.RegistrosFallidos > 0)
							{
								sbBody.AppendLine("<h3>Detalle de Errores Encontrados:</h3>");
								sbBody.AppendLine("<p>Se ha adjuntado un archivo CSV a este correo con el detalle completo de las filas rechazadas para su corrección.</p>");

								StringBuilder csvContent = new StringBuilder();

								if (resultado.TablaRechazados != null && resultado.TablaRechazados.Rows.Count > 0)
								{
									var nombresColumnas = resultado.TablaRechazados.Columns.Cast<DataColumn>().Select(c => LimpiarParaCsv(c.ColumnName));
									csvContent.AppendLine(string.Join(",", nombresColumnas));

									foreach (DataRow fila in resultado.TablaRechazados.Rows)
									{
										var valoresFila = fila.ItemArray.Select(v => LimpiarParaCsv(v?.ToString()));
										csvContent.AppendLine(string.Join(",", valoresFila));
									}
								}
								else if (resultado.ErroresDetalle != null && resultado.ErroresDetalle.Count > 0)
								{
									csvContent.AppendLine("No. Error,Detalle del Error");
									int contador = 1;
									foreach (var error in resultado.ErroresDetalle)
									{
										csvContent.AppendLine($"{contador},{LimpiarParaCsv(error)}");
										contador++;
									}
								}

								if (csvContent.Length > 0)
								{
									byte[] buffer = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csvContent.ToString())).ToArray();
									MemoryStream ms = new MemoryStream(buffer);
									mensaje.Attachments.Add(new Attachment(ms, $"Errores_Carga_{parametros.FolioCarga}.csv", "text/csv"));
								}
							}
						}

						sbBody.AppendLine("<hr/>");
						string appNameWebConfig = System.Configuration.ConfigurationManager.AppSettings["NombAplicacion"] ?? "Cargas Masivas";
						sbBody.AppendLine($"<p><small>Este es un mensaje automático generado por el sistema {appNameWebConfig} del Gobierno del Estado de Puebla. No responda a esta dirección.</small></p>");

						mensaje.Body = sbBody.ToString();

						// =========================================================================
						// 🚀 NUEVO: LÓGICA DE REINTENTOS (EXPONENTIAL BACKOFF)
						// =========================================================================
						int maxIntentos = 3;
						int intentoActual = 0;
						bool enviado = false;

						while (intentoActual < maxIntentos && !enviado)
						{
							try
							{
								intentoActual++;
								await clienteSmtp.SendMailAsync(mensaje);
								enviado = true; // Si no lanza excepción, se envió correctamente

								if (intentoActual > 1)
								{
									await LogService.WriteLogAsync("INFO", parametros.UsuarioLogin, "ServicioNotificacion", $"[Folio: {parametros.FolioCarga}] El correo se envió exitosamente en el intento {intentoActual}.");
								}
							}
							catch (Exception exSmtp)
							{
								await LogService.WriteLogAsync("WARN", parametros.UsuarioLogin, "ServicioNotificacion", $"[Folio: {parametros.FolioCarga}] Fallo en intento {intentoActual} al enviar correo SMTP. Detalle: {exSmtp.Message}");

								if (intentoActual >= maxIntentos)
								{
									// Se agotaron los intentos, lanzamos la alerta a Soporte
									await LogService.WriteLogAsync("ERROR", parametros.UsuarioLogin, "ServicioNotificacion", $"[Folio: {parametros.FolioCarga}] ERROR CRÍTICO: No se pudo enviar el correo a {correoUsuario} tras {maxIntentos} intentos.");
									await NotificarFalloCriticoASoporte(parametros, correoUsuario, exSmtp.Message);
								}
								else
								{
									// Esperamos de forma asíncrona antes del siguiente intento (3s, 6s)
									await Task.Delay(3000 * intentoActual);
								}
							}
						}
					}
				}
			}
			catch (Exception ex)
			{
				// Atrapa cualquier otro error antes de intentar enviar (ej. Fallo armando el CSV)
				await LogService.WriteLogAsync("ERROR", parametros.UsuarioLogin, "ServicioNotificacion", $"[Folio: {parametros.FolioCarga}] ERROR INTERNO DE NOTIFICACIÓN: {ex.Message}");
			}
		}

		/// <summary>
		/// 🚀 NUEVO MÉTODO: Envía una alerta a los administradores si el correo hacia el usuario final falla por problemas de red/SMTP.
		/// </summary>
		private static async Task NotificarFalloCriticoASoporte(ParametrosCarga parametros, string correoDestinoFallido, string errorSmtp)
		{
			try
			{
				string smtpHost = System.Configuration.ConfigurationManager.AppSettings["HostRemitente"];
				int smtpPort = Convert.ToInt32(System.Configuration.ConfigurationManager.AppSettings["SmtpPort"] ?? "587");
				string remitente = System.Configuration.ConfigurationManager.AppSettings["CorreoRemitente"];
				string smtpPass = System.Configuration.ConfigurationManager.AppSettings["PwdRemitente"];
				string ambiente = System.Configuration.ConfigurationManager.AppSettings["Ambiente"] ?? "Test";

				using (SmtpClient smtp = new SmtpClient(smtpHost, smtpPort))
				{
					smtp.Credentials = new System.Net.NetworkCredential(remitente, smtpPass);
					smtp.EnableSsl = true;

					using (MailMessage alerta = new MailMessage())
					{
						alerta.From = new MailAddress(remitente, "Alerta Sistema Predial");

						// Regla de destinatarios (Estado vs Desarrollo)
						if (ambiente.Equals("Prod", StringComparison.OrdinalIgnoreCase))
						{
							alerta.To.Add("isabel.rugerio@puebla.gob.mx"); // Jefa
							alerta.CC.Add("tlamatini.ortiz@puebla.gob.mx"); // Tú
						}
						else
						{
							alerta.To.Add("tlamatini.ortiz@puebla.gob.mx"); // Tú en ambiente Test
						}

						alerta.Subject = $"🚨 ALERTA URGENTE: Falla en Notificación al Usuario - Folio {parametros.FolioCarga}";
						alerta.IsBodyHtml = true;

						StringBuilder body = new StringBuilder();
						body.AppendLine("<h2>Alerta de Sistema: Fallo en entrega de correo SMTP</h2>");
						body.AppendLine($"<p>El sistema procesó correctamente el archivo <b>Folio {parametros.FolioCarga}</b> del usuario <b>{parametros.UsuarioLogin}</b>.</p>");
						body.AppendLine($"<p>Sin embargo, el servidor de correos <b>NO pudo enviar el correo de confirmación</b> al destinatario final ({correoDestinoFallido}) después de varios reintentos.</p>");
						body.AppendLine($"<p><b>Detalle técnico del error SMTP:</b> {errorSmtp}</p>");
						body.AppendLine("<p>Por favor, revise la tabla de <i>ControlCargas</i> y notifique al usuario manualmente si es necesario.</p>");

						alerta.Body = body.ToString();

						await smtp.SendMailAsync(alerta);
					}
				}
			}
			catch (Exception exSoporte)
			{
				// Si incluso el correo de alerta falla (el internet del servidor se cayó por completo)
				await LogService.WriteLogAsync("ERROR", parametros.UsuarioLogin, "ServicioNotificacion", $"[Folio: {parametros.FolioCarga}] Fallo catastrófico al intentar notificar a Soporte sobre la caída de SMTP. Detalle: {exSoporte.Message}");
			}
		}

		private static string LimpiarParaCsv(string valor)
		{
			if (string.IsNullOrEmpty(valor)) return "";

			if (valor.Contains(",") || valor.Contains("\"") || valor.Contains("\r") || valor.Contains("\n"))
			{
				return $"\"{valor.Replace("\"", "\"\"")}\"";
			}

			return valor;
		}
	}
}