using swCargaMasivaIngresos.Models;
using System;
using System.IO;
using System.Threading.Tasks;
using QRCoder;

namespace swCargaMasivaIngresos.Services.PDF
{
	public static class ServicioGeneradorHTMLtoPDF
	{
		public static async Task<byte[]> GenerarDocumentoCompletoAsync(DatosDocumentoUsuario datos)
		{
			// 1. LEER LA PLANTILLA HTML BASE
			// (Tendrías un archivo PlantillaAlta.html guardado en tu servidor en la carpeta /Plantillas/)
			string rutaPlantilla = System.Web.Hosting.HostingEnvironment.MapPath("~/Plantillas/PlantillaAlta.html");
			string htmlText = File.ReadAllText(rutaPlantilla);

			// 2. INYECTAR LAS IMÁGENES DESDE LA API
			// En lugar de que el PDF descargue la imagen, la descargamos en C# y la inyectamos como Base64.
			// Esto garantiza que el PDF siempre se genere rápido y no falle por bloqueos de red internos.
			string logoInstitucionalBase64 = await DescargarImagenBase64("http://172.21.20.34:8088/swImagenInstitucional/api/visual/logo_estado");
			string marcaAguaBase64 = await DescargarImagenBase64("http://172.21.20.34:8088/swImagenInstitucional/api/visual/marca_agua");

			htmlText = htmlText.Replace("{{LogoBase64}}", logoInstitucionalBase64);
			htmlText = htmlText.Replace("{{MarcaAguaBase64}}", marcaAguaBase64);

			// 3. GENERAR EL CÓDIGO QR PARA LA CONTRASEÑA
			string qrBase64 = GenerarCodigoQR(datos.PasswordAcceso);
			htmlText = htmlText.Replace("{{QRPasswordBase64}}", qrBase64);

			// 4. REEMPLAZAR LOS DATOS DEL JSON EN EL TEXTO LEGAL
			htmlText = htmlText.Replace("{{NumActa}}", datos.NumActa);
			htmlText = htmlText.Replace("{{UsuarioLogin}}", datos.UsuarioLogin);
			htmlText = htmlText.Replace("{{NombreCompleto}}", datos.NombreCompleto);
			htmlText = htmlText.Replace("{{Curp}}", datos.CURP);
			htmlText = htmlText.Replace("{{AreaAdscripcion}}", datos.AreaAdscripcion);
			htmlText = htmlText.Replace("{{CargoUsuario}}", datos.CargoUsuario);
			htmlText = htmlText.Replace("{{FechaActual}}", DateTime.Now.ToString("dd 'de' MMMM 'de' yyyy"));
			// ... (Reemplazar el resto de las variables)

			// 5. CONVERTIR EL STRING HTML A PDF
			// Aquí llamas a tu librería conversora (Ej. DinkToPdf, SelectPdf, etc.)
			byte[] pdfBytes = ConvertirHtmlAPdf(htmlText);

			return pdfBytes;
		}

		// ===================================================================
		// MÉTODOS AUXILIARES
		// ===================================================================

		private static async Task<string> DescargarImagenBase64(string url)
		{
			try
			{
				using (var client = new System.Net.Http.HttpClient())
				{
					byte[] imageBytes = await client.GetByteArrayAsync(url);
					return "data:image/png;base64," + Convert.ToBase64String(imageBytes);
				}
			}
			catch
			{
				return ""; // Si falla, regresa vacío para no romper el HTML
			}
		}

		private static string GenerarCodigoQR(string textoSecreto)
		{
			using (QRCodeGenerator qrGenerator = new QRCodeGenerator())
			{
				QRCodeData qrCodeData = qrGenerator.CreateQrCode(textoSecreto, QRCodeGenerator.ECCLevel.Q);
				using (PngByteQRCode qrCode = new PngByteQRCode(qrCodeData))
				{
					byte[] qrBytes = qrCode.GetGraphic(20);
					return "data:image/png;base64," + Convert.ToBase64String(qrBytes);
				}
			}
		}

		private static byte[] ConvertirHtmlAPdf(string html)
		{
			// TODO: Integrar la librería PDF elegida. 
			// Ejemplo conceptual si usaras DinkToPdf:
			// var converter = new SynchronizedConverter(new PdfTools());
			// var doc = new HtmlToPdfDocument() { ... GlobalSettings ... Objects = { new ObjectSettings { HtmlContent = html } } };
			// return converter.Convert(doc);

			throw new NotImplementedException("Falta instalar la librería HTML a PDF");
		}
	}
}