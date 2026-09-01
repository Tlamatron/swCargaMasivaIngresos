using Newtonsoft.Json;
using QRCoder;
using swCargaMasivaIngresos.Models;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Web.Hosting;

namespace swCargaMasivaIngresos.Services.PDF
{
	/// <summary>
	/// Servicio encargado de generar documentos PDF a partir de plantillas HTML. Este servicio permite inyectar datos dinámicos, imágenes institucionales y códigos QR en las plantillas antes de convertirlas a PDF. Utiliza la API de imágenes institucionales para obtener los recursos gráficos necesarios y asegura que los documentos generados sean consistentes y profesionales.
	/// </summary>
	public static class ServicioGeneradorHTMLtoPDF
	{
		
		/// <summary>
		/// Genera el paquete consolidado o un documento individual leyendo la plantilla HTML requerida.
		/// </summary>
		public static async Task<byte[]> GenerarDocumentoAsync(DatosDocumentoUsuario datos, string nombrePlantilla)
		{
			// 1. Cargar Plantilla HTML
			string rutaPlantilla = HostingEnvironment.MapPath($"~/Plantillas/{nombrePlantilla}.html");
			if (!File.Exists(rutaPlantilla))
				throw new FileNotFoundException($"No se encontró la plantilla {nombrePlantilla}.html en la carpeta /Plantillas/");

			string html = File.ReadAllText(rutaPlantilla, Encoding.UTF8);

			// 2. Determinar URL base según ambiente desde Web.config
			string ambiente = ConfigurationManager.AppSettings["Ambiente"] ?? "Test";
			bool esProduccion = ambiente.Equals("Prod", StringComparison.OrdinalIgnoreCase) ||
								ambiente.Equals("Produccion", StringComparison.OrdinalIgnoreCase);

			string baseUrl = esProduccion
				? ConfigurationManager.AppSettings["ApiImagenUrlProd"]
				: ConfigurationManager.AppSettings["ApiImagenUrlTest"];

			// 1. Envías las claves EXACTAS que tu catálogo JSON reconoce (Ajusta los nombres según corresponda)
			var imagenesRequeridas = new List<string> { "LogoInstitucional", "MarcaAgua", "Footer" };

			// 2. Obtienes el diccionario ya convertido a Base64
			var dictImagenes = await ObtenerImagenesALaCartaAsync(baseUrl, imagenesRequeridas);

			// 3. Extraes con TryGetValue para inyectar en el HTML
			dictImagenes.TryGetValue("LogoInstitucional", out string logoBase64);
			dictImagenes.TryGetValue("MarcaAgua", out string marcaAguaBase64);
			dictImagenes.TryGetValue("Footer", out string piePaginaBase64);

			// 4. Reemplazar Variables de Imágenes en HTML
			html = html.Replace("{{LogoBase64}}", logoBase64 ?? string.Empty)
					   .Replace("{{MarcaAguaBase64}}", marcaAguaBase64 ?? string.Empty)
					   .Replace("{{PiePaginaBase64}}", piePaginaBase64 ?? string.Empty);

			// 5. Generar Código QR (si aplica)
			if (!string.IsNullOrEmpty(datos.PasswordAcceso))
			{
				string qrBase64 = GenerarCodigoQR(datos.PasswordAcceso);
				html = html.Replace("{{QRPasswordBase64}}", qrBase64);
			}

			// 6. Reemplazar Variables de Datos
			html = MapearDatosEnHtml(html, datos);

			// 7. Convertir a PDF
			return ConvertirHtmlAPdf(html);
		}


		private static async Task<Dictionary<string, string>> ObtenerImagenesALaCartaAsync(string baseUrl, List<string> claves)
		{
			var resultadoBase64 = new Dictionary<string, string>();

			try
			{
				// Limpiamos la URL base para evitar dobles diagonales al concatenar
				baseUrl = baseUrl.TrimEnd('/');
				string endpointConfig = $"{baseUrl}/api/visual/config/custom";

				using (var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) })
				{
					// 1. Preparamos el payload exacto que espera tu controlador
					var payload = new PeticionVisual { ImagenesRequeridas = claves };
					var jsonContent = JsonConvert.SerializeObject(payload);
					var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

					// 2. Consultamos el endpoint config/custom
					var response = await client.PostAsync(endpointConfig, content);

					if (response.IsSuccessStatusCode)
					{
						var responseString = await response.Content.ReadAsStringAsync();
						var configData = JsonConvert.DeserializeObject<RespuestaVisual>(responseString);

						// 3. Si hay assets, iteramos sobre ellos para descargar sus bytes y convertirlos a Base64
						if (configData?.assetsRequeridos != null)
						{
							// Extraer solo el esquema y el host de tu baseUrl (ej. http://172.21.20.34:8088)
							// porque Url.Content usualmente devuelve rutas desde la raíz (ej. /swImagenInstitucional/Content/...)
							var uri = new Uri(baseUrl);
							string hostUrl = $"{uri.Scheme}://{uri.Authority}";

							foreach (var asset in configData.assetsRequeridos)
							{
								string clave = asset.Key;
								string rutaRelativa = asset.Value; // Ej: /swImagenInstitucional/Content/Logos/Actual/logo_color.svg

								// Armamos la URL de descarga absoluta
								string urlDescarga = $"{hostUrl}{rutaRelativa}";

								// Descargamos los bytes de la imagen
								byte[] imageBytes = await client.GetByteArrayAsync(urlDescarga);

								// Determinamos el MIME type correcto
								string mimeType = rutaRelativa.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)
												  ? "image/svg+xml"
												  : "image/png";

								// Guardamos el Base64 listo para el HTML
								resultadoBase64[clave] = $"data:{mimeType};base64,{Convert.ToBase64String(imageBytes)}";
							}
						}
					}
				}
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"Error al consultar y descargar imágenes de swImagenInstitucional: {ex.Message}");
			}

			return resultadoBase64;
		}

		private static string MapearDatosEnHtml(string html, DatosDocumentoUsuario d)
		{
			return html
				.Replace("{{NumActa}}", d.NumActa ?? string.Empty)
				.Replace("{{UsuarioLogin}}", d.UsuarioLogin ?? string.Empty)
				.Replace("{{CURP}}", d.CURP ?? string.Empty)
				.Replace("{{NombreCompleto}}", d.NombreCompleto ?? string.Empty)
				.Replace("{{NombreSistema}}", d.NombreSistema ?? string.Empty)
				.Replace("{{UrlSistema}}", d.UrlSistema ?? string.Empty)
				.Replace("{{CargoUsuario}}", d.CargoUsuario ?? string.Empty)
				.Replace("{{AreaAdscripcion}}", d.AreaAdscripcion ?? string.Empty)
				.Replace("{{CorreoElectronico}}", d.CorreoElectronico ?? string.Empty)
				.Replace("{{DomicilioLaboral}}", d.DomicilioLaboral ?? string.Empty)
				.Replace("{{IdentificacionTipo}}", d.IdentificacionTipo ?? string.Empty)
				.Replace("{{IdentificacionFolio}}", d.IdentificacionFolio ?? string.Empty)
				.Replace("{{OficioSolicitud}}", d.OficioSolicitud ?? string.Empty)
				.Replace("{{NombreDirectorIngresos}}", d.NombreDirectorIngresos ?? string.Empty)
				.Replace("{{Municipio}}", d.Municipio ?? string.Empty)
				.Replace("{{Rol}}", d.Rol ?? string.Empty)
				.Replace("{{FechaEmision}}", d.FechaEmision.ToString("dd 'de' MMMM 'de' yyyy"))
				.Replace("{{HoraEmision}}", d.FechaEmision.ToString("HH:mm"));
		}

		private static async Task<string> DescargarImagenBase64Async(string url)
		{
			try
			{
				using (var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) })
				{
					byte[] imageBytes = await client.GetByteArrayAsync(url);
					return "data:image/png;base64," + Convert.ToBase64String(imageBytes);
				}
			}
			catch
			{
				return string.Empty; // Retorna cadena vacía si falla la conexión a la API para evitar detener la emisión
			}
		}

		private static string GenerarCodigoQR(string contenido)
		{
			using (var qrGenerator = new QRCodeGenerator())
			{
				QRCodeData qrCodeData = qrGenerator.CreateQrCode(contenido, QRCodeGenerator.ECCLevel.Q);
				using (var qrCode = new PngByteQRCode(qrCodeData))
				{
					byte[] qrBytes = qrCode.GetGraphic(20);
					return "data:image/png;base64," + Convert.ToBase64String(qrBytes);
				}
			}
		}

		private static byte[] ConvertirHtmlAPdf(string html)
		{
			var converter = new DinkToPdf.BasicConverter(new DinkToPdf.PdfTools());
			var doc = new DinkToPdf.HtmlToPdfDocument()
			{
				GlobalSettings = {
				ColorMode = DinkToPdf.ColorMode.Color,
				Orientation = DinkToPdf.Orientation.Portrait,
				PaperSize = DinkToPdf.PaperKind.Letter,
				},
				Objects = {
					new DinkToPdf.ObjectSettings() {
						PagesCount = true,
						HtmlContent = html,
						WebSettings = { DefaultEncoding = "utf-8" }
					}
				}
			};
			return converter.Convert(doc);
		}
	}
}