using System.Collections.Generic;

namespace swCargaMasivaIngresos.Models
{
	/// <summary>
	/// DTO utilizado para representar la estructura de un menú en la aplicación. Contiene propiedades para el identificador del menú, el identificador del menú padre (en caso de ser un sub-menú), el nombre que se mostrará en la interfaz, la ruta URL a la que apunta el menú, el icono asociado y el orden de aparición. Además, incluye una lista de sub-menús para permitir una estructura jerárquica en caso de que se requieran niveles adicionales de navegación en el futuro.
	/// </summary>
	public class MenuDTO
	{
		/// <summary>
		/// Método que obtiene o establece el identificador único del menú. Este valor es utilizado para diferenciar cada menú dentro de la aplicación y puede ser referenciado en otras partes del sistema para realizar operaciones relacionadas con el menú.
		/// </summary>
		public int MenuId { get; set; }

		/// <summary>
		/// Método que obtiene o establece el identificador del menú padre. Este valor es opcional y se utiliza para establecer relaciones jerárquicas entre menús, permitiendo la creación de sub-menús. Si el menú no tiene un padre, este valor puede ser nulo.
		/// </summary>
		public int? MenuPadreId { get; set; }

		/// <summary>
		/// Método que obtiene o establece el nombre del menú. Este valor es utilizado para mostrar el texto correspondiente en la interfaz de usuario, permitiendo a los usuarios identificar y seleccionar el menú deseado.
		/// </summary>
		public string Nombre { get; set; }

		/// <summary>
		/// Método que obtiene o establece la ruta URL asociada al menú. Este valor define la dirección a la que se redirigirá al usuario cuando seleccione el menú, permitiendo la navegación dentro de la aplicación.
		/// </summary>
		public string RutaUrl { get; set; }

		/// <summary>
		/// Método que obtiene o establece el icono asociado al menú. Este valor puede ser utilizado para mostrar un ícono visual junto al nombre del menú en la interfaz de usuario, mejorando la experiencia de navegación y facilitando la identificación rápida de las opciones disponibles.
		/// </summary>
		public string Icono { get; set; }

		/// <summary>
		/// Método que obtiene o establece el orden de aparición del menú. Este valor determina la posición en la que el menú se mostrará dentro de la lista de menús, permitiendo organizar las opciones de manera lógica y coherente para los usuarios.
		/// </summary>
		public int Orden { get; set; }

		/// <summary>
		/// Sección que obtiene o establece la lista de sub-menús asociados al menú actual. Esta propiedad permite crear una estructura jerárquica de menús, donde cada menú puede contener múltiples sub-menús, facilitando la organización y navegación dentro de la aplicación.
		/// </summary>
		public List<MenuDTO> SubMenus { get; set; } = new List<MenuDTO>();
	}
}