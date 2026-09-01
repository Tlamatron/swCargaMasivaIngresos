using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace swCargaMasivaIngresos.Models
{
	/// <summary>
	/// Clase que representa la petición para obtener recursos visuales desde la API de imágenes institucionales. Contiene una lista de nombres de imágenes requeridas que el cliente solicita al servidor para su uso en la generación de documentos PDF o interfaces web.
	/// </summary>
	public class PeticionVisual
	{
		/// <summary>
		/// Lista de nombres de imágenes requeridas que el cliente solicita al servidor. Cada nombre corresponde a un recurso gráfico específico que se utilizará en la generación de documentos o interfaces visuales.
		/// </summary>
		public List<string> ImagenesRequeridas { get; set; }
	}

	/// <summary>
	/// Clase que representa la respuesta de la API de imágenes institucionales. Contiene la información sobre el tema visual, el modo responsivo y los recursos gráficos requeridos.
	/// </summary>
	public class RespuestaVisual
	{
		/// <summary>
		/// Nombre del tema visual.
		/// </summary>
		public string tema { get; set; }

		/// <summary>
		/// Indica si el modo responsivo está habilitado.
		/// </summary>
		public bool modoResponsivo { get; set; }

		/// <summary>
		/// Diccionario que contiene los recursos gráficos requeridos, donde la clave es el nombre de la imagen y el valor es su representación en base64.
		/// </summary>
		public Dictionary<string, string> assetsRequeridos { get; set; }
	}
}