using System;

namespace swCargaMasivaIngresos.Models
{
	/// <summary>
	/// Clase estándar utilizada para unificar las respuestas HTTP de la API hacia el cliente web (Front-end).
	/// </summary>
	public class RespuestaCarga
	{
		/// <summary>
		/// Indica si la operación actual (recepción de fragmento o ensamble) se realizó con éxito.
		/// </summary>
		public bool Exito { get; set; }

		/// <summary>
		/// Mensaje descriptivo del estatus actual del proceso para su visualización en el cliente.
		/// </summary>
		public string Mensaje { get; set; }

		/// <summary>
		/// Folio único de la carga (GUID). El cliente debe capturarlo en la respuesta del primer fragmento y reenviarlo en los posteriores.
		/// </summary>
		public int FolioCarga { get; set; }

		/// <summary>
		/// El número del fragmento (Chunk) que acaba de ser procesado por el servidor web.
		/// </summary>
		public int FragmentoActual { get; set; }

		/// <summary>
		/// Indica si el servidor ha recibido el último fragmento y ha procedido a ensamblar y encolar el archivo final.
		/// </summary>
		public bool EnsambladoCompleto { get; set; }
	}
}