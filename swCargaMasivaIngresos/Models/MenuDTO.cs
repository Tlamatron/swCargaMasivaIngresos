using System.Collections.Generic;

namespace swCargaMasivaIngresos.Models
{
	/// <summary>
	/// DTO utilizado para representar la estructura de un menú en la aplicación. Contiene propiedades para el identificador del menú, el identificador del menú padre (en caso de ser un sub-menú), el nombre que se mostrará en la interfaz, la ruta URL a la que apunta el menú, el icono asociado y el orden de aparición. Además, incluye una lista de sub-menús para permitir una estructura jerárquica en caso de que se requieran niveles adicionales de navegación en el futuro.
	/// </summary>
	public class MenuDTO
	{
		public int MenuId { get; set; }
		public int? MenuPadreId { get; set; }
		public string Nombre { get; set; }
		public string RutaUrl { get; set; }
		public string Icono { get; set; }
		public int Orden { get; set; }

		// Esta lista nos servirá en el Front-end si en el futuro tenemos sub-menús
		public List<MenuDTO> SubMenus { get; set; } = new List<MenuDTO>();
	}
}