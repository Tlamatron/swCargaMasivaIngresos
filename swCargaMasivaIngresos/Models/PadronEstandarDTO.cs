using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace swCargaMasivaIngresos.Models
{
	/// <summary>
	/// No importa como le llame el municipio a sus variables, nosotros siempre convertiremos sus datos a esta clase antes de insertarlos a la base de datos. Esto nos da una capa de abstracción que nos permite manejar cualquier formato de Excel que el municipio nos envíe, siempre y cuando podamos mapearlo a esta clase.
	/// </summary>
	public class PadronEstandarDTO
	{
		public short ClaveMunicipio { get; set; }
		public byte TipoPredio { get; set; }
		public string CuentaPredial { get; set; }
		public decimal BaseGravable { get; set; }
		public decimal ImpuestoDeterminado { get; set; }
		// ... aquí puedes agregar todos los campos que necesites guardar ...
	}
}