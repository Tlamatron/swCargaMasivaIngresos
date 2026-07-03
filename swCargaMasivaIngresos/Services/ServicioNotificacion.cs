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

						sbBody.AppendLine("<h3>Resumen:</h3>");
						sbBody.AppendLine("<ul>");
						sbBody.AppendLine($"<li><b>Registros insertados exitosamente:</b> <span style='color:green'>{resultado.RegistrosExitosos}</span></li>");
						sbBody.AppendLine($"<li><b>Registros rechazados (Error de formato):</b> <span style='color:red'>{resultado.RegistrosFallidos}</span></li>");
						sbBody.AppendLine("</ul>");

						// =========================================================================
						// 🚀 NUEVA LÓGICA DE EXPORTACIÓN A CSV (Soporta Tabla Completa o Lista Simple)
						// =========================================================================
						if (resultado.RegistrosFallidos > 0)
						{
							sbBody.AppendLine("<h3>Detalle de Errores Encontrados:</h3>");
							sbBody.AppendLine("<p>Se ha adjuntado un archivo CSV a este correo con el detalle completo de las filas rechazadas para su corrección.</p>");

							StringBuilder csvContent = new StringBuilder();

							// ESCENARIO A: El nuevo motor devolvió la tabla completa de rechazados
							if (resultado.TablaRechazados != null && resultado.TablaRechazados.Rows.Count > 0)
							{
								// 1. Imprimir Encabezados
								var nombresColumnas = resultado.TablaRechazados.Columns.Cast<DataColumn>().Select(c => LimpiarParaCsv(c.ColumnName));
								csvContent.AppendLine(string.Join(",", nombresColumnas));

								// 2. Imprimir Filas
								foreach (DataRow fila in resultado.TablaRechazados.Rows)
								{
									var valoresFila = fila.ItemArray.Select(v => LimpiarParaCsv(v?.ToString()));
									csvContent.AppendLine(string.Join(",", valoresFila));
								}
							}
							// ESCENARIO B: Compatibilidad con los procesadores antiguos (Padrón y Reducciones)
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
				await LogService.WriteLogAsync(AppName, "ERROR", parametros.UsuarioLogin, "ServicioNotificacion", $"[Folio: {parametros.FolioCarga}] ERROR NOTIFICACIÓN: Falló el envío de correo a {correoUsuario}. Detalle: {ex.Message}");
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