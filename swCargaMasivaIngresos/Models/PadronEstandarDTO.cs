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
		/// <summary>
		/// Municipio al que pertenece el predio. Este valor debe ser un número entero entre 1 y 217, representando la clave oficial del municipio según la normativa vigente. Se utiliza para identificar geográficamente el predio dentro del sistema de catastro y para aplicar las reglas fiscales correspondientes.
		/// </summary>
		public short ClaveMunicipio { get; set; }

		/// <summary>
		/// Muestra el tipo de predio según la clasificación oficial: 1 para Urbano, 2 para Rústico y 3 para Suburbano. Este valor es crucial para determinar las tasas impositivas aplicables y las regulaciones específicas que afectan al predio en cuestión.
		/// </summary>
		public byte TipoPredio { get; set; }

		/// <summary>
		/// Muestra la cuenta predial del predio, que es un identificador único asignado por el municipio. Este valor es esencial para vincular los pagos y obligaciones fiscales con el predio correcto, y debe ser tratado con precisión para evitar errores en la gestión de impuestos.
		/// </summary>
		public string CuentaPredial { get; set; }

		/// <summary>
		/// Muestra la base gravable sobre la cual se calcula el impuesto determinado. Este valor es un número decimal que representa la cantidad de dinero sujeta a impuestos, y es fundamental para calcular correctamente el monto del impuesto que debe ser pagado por el contribuyente.
		/// </summary>
		public decimal BaseGravable { get; set; }

		/// <summary>
		/// Muestra el monto del impuesto determinado que el contribuyente debe pagar. Este valor es un número decimal calculado a partir de la base gravable y las tasas impositivas aplicables, y es esencial para la correcta recaudación de impuestos por parte del municipio.
		/// </summary>
		public decimal ImpuestoDeterminado { get; set; }

		/// <summary>
		/// Muestra la clase de pago asociada al predio, que puede influir en la forma en que se procesan los pagos y las obligaciones fiscales. Este valor es un número entero que representa diferentes categorías de pago según la normativa municipal, y es importante para garantizar que los pagos se registren y procesen correctamente.
		/// </summary>
		public int ClasePago { get; set; }

		/// <summary>
		/// Muestra el bimestre pagado por el contribuyente, que es un número entero entre 1 y 6 representando los seis bimestres del año fiscal. Este valor es crucial para determinar el período fiscal al que corresponde el pago realizado, y ayuda a mantener un registro preciso de las obligaciones fiscales del contribuyente a lo largo del año.
		/// </summary>
		public int BimestresPagados { get; set; }
	}
}