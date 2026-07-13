using iTextSharp.text;
using iTextSharp.text.pdf;
using swCargaMasivaIngresos.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web;

namespace swCargaMasivaIngresos.Services.PDF
{
	/// <summary>
	/// Servicio encargado de generar documentos PDF relacionados con los usuarios, como el Acta de Entrega, la Carta de Confidencialidad y la Credencial de Acceso. Utiliza la biblioteca iTextSharp para crear y formatear los documentos PDF de manera programática.
	/// </summary>
	public static class ServicioGeneradorDocumentos
	{
		/// <summary>
		/// Método que genera un documento PDF representando el Acta de Entrega de Credenciales de Acceso para un usuario específico. El documento incluye información relevante del usuario, como su nombre completo, ID, municipio y fecha de emisión.
		/// </summary>
		/// <param name="datos"></param>
		/// <returns></returns>
		public static byte[] GenerarActaEntrega(DatosDocumentoUsuario datos)
		{
			using (var ms = new MemoryStream())
			{
				var doc = new Document(PageSize.LETTER, 50, 50, 50, 50);
				PdfWriter.GetInstance(doc, ms);
				doc.Open();

				// TODO: Aquí diseñarás el Acta de Entrega final
				doc.Add(new Paragraph("ACTA DE ENTREGA DE CREDENCIALES DE ACCESO"));
				doc.Add(new Paragraph($"Usuario: {datos.NombreCompleto} (ID: {datos.idUsuario})"));
				doc.Add(new Paragraph($"Municipio: {datos.ClaveMunicipio}"));
				doc.Add(new Paragraph($"Fecha: {datos.FechaEmision:dd/MM/yyyy}"));

				doc.Close();
				return ms.ToArray();
			}
		}

		/// <summary>
		/// Método que genera un documento PDF representando la Carta de Confidencialidad para un usuario específico. El documento incluye una declaración de compromiso de confidencialidad por parte del usuario, junto con su nombre completo y otros detalles relevantes.
		/// </summary>
		/// <param name="datos"></param>
		/// <returns></returns>
		public static byte[] GenerarCartaConfidencialidad(DatosDocumentoUsuario datos)
		{
			using (var ms = new MemoryStream())
			{
				var doc = new Document(PageSize.LETTER, 50, 50, 50, 50);
				PdfWriter.GetInstance(doc, ms);
				doc.Open();

				// TODO: Aquí diseñarás la Carta de Confidencialidad
				doc.Add(new Paragraph("CARTA DE CONFIDENCIALIDAD"));
				doc.Add(new Paragraph($"Yo, {datos.NombreCompleto}, me comprometo a mantener la confidencialidad..."));

				doc.Close();
				return ms.ToArray();
			}
		}

		/// <summary>
		/// Método que genera un documento PDF representando la Credencial de Acceso para un usuario específico. El documento incluye información relevante del usuario, como su nombre completo, rol y otros detalles necesarios para la identificación y acceso.
		/// </summary>
		/// <param name="datos"></param>
		/// <returns></returns>
		public static byte[] GenerarCredencialAcceso(DatosDocumentoUsuario datos)
		{
			using (var ms = new MemoryStream())
			{
				// Para una credencial, podrías usar un tamaño personalizado en lugar de LETTER
				var doc = new Document(new Rectangle(240f, 153f), 10, 10, 10, 10); // Tamaño Gafete aprox
				PdfWriter.GetInstance(doc, ms);
				doc.Open();

				// TODO: Aquí diseñarás la Credencial (Foto, Código QR, etc.)
				doc.Add(new Paragraph("CREDENCIAL DE ACCESO"));
				doc.Add(new Paragraph(datos.NombreCompleto));
				doc.Add(new Paragraph(datos.Rol));

				doc.Close();
				return ms.ToArray();
			}
		}
	}
}