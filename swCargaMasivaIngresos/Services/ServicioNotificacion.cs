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
	public static class ServicioNotificacion
	{
		private static readonly string AppName = System.Configuration.ConfigurationManager.AppSettings["NombAplicacion"] ?? "APICargaMasivaIngresos";

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

						mensaje.Subject = $"Resultados de Carga Masiva - Folio: {parametros.FolioCarga}";
						mensaje.IsBodyHtml = true;

						StringBuilder sbBody = new StringBuilder();
						sbBody.AppendLine("<h2>Reporte de Carga de Archivo</h2>");
						sbBody.AppendLine($"<p>Estimado usuario de la oficina <b>{parametros.OficinaId}</b>,</p>");
						sbBody.AppendLine($"<p>Le notificamos que el proceso de su archivo (Folio: <b>{parametros.FolioCarga}</b>) ha finalizado.</p>");

						// =========================================================================
						// 🚀 DETECCIÓN VISUAL DE ERROR FATAL
						// =========================================================================
						bool esErrorFatal = resultado.RegistrosExitosos == 0 && resultado.TablaRechazados == null && resultado.ErroresDetalle != null && resultado.ErroresDetalle.Count > 0;

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
							// EL FLUJO NORMAL (Si sí leyó el archivo)
							sbBody.AppendLine("<h3>Resumen:</h3>");
							sbBody.AppendLine("<ul>");
							sbBody.AppendLine($"<li><b>Registros insertados exitosamente:</b> <span style='color:green'>{resultado.RegistrosExitosos}</span></li>");
							sbBody.AppendLine($"<li><b>Registros rechazados (Error de formato):</b> <span style='color:red'>{resultado.RegistrosFallidos}</span></li>");
							sbBody.AppendLine("</ul>");

							// LÓGICA DE EXPORTACIÓN A CSV (Tu código original intacto)
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

						await clienteSmtp.SendMailAsync(mensaje);
					}
				}
			}
			catch (Exception ex)
			{
				await LogService.WriteLogAsync("ERROR", parametros.UsuarioLogin, "ServicioNotificacion", $"[Folio: {parametros.FolioCarga}] ERROR NOTIFICACIÓN: Falló el envío de correo a {correoUsuario}. Detalle: {ex.Message}");
			}
		}

		/// <summary>
		/// Escapa correctamente los textos para que las comas (,) o saltos de línea internos no rompan las columnas en Excel.
		/// </summary>
		private static string LimpiarParaCsv(string valor)
		{
			if (string.IsNullOrEmpty(valor)) return "";

			// Si el texto tiene comas, comillas dobles o saltos de línea, DEBE ir encerrado entre comillas dobles
			if (valor.Contains(",") || valor.Contains("\"") || valor.Contains("\r") || valor.Contains("\n"))
			{
				// Escapamos las comillas internas duplicándolas ("")
				return $"\"{valor.Replace("\"", "\"\"")}\"";
			}

			return valor;
		}
	}
}