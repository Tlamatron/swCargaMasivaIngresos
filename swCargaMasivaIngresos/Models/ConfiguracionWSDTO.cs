using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace swCargaMasivaIngresos.Models
{
	/// <summary>
	/// Lee la configuración de la base de datos.
	/// </summary>
	public class ConfiguracionWSDTO
	{
		public int OficinaId { get; set; }
		public string EndpointUrl { get; set; }
		public byte TipoAutenticacion { get; set; }
		public string Credencial1 { get; set; }
		public string Credencial2 { get; set; }
	}
}