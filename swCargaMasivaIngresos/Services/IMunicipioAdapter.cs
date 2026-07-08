using swCargaMasivaIngresos.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace swCargaMasivaIngresos.Services
{
	/// <summary>
	/// Interfaz que define el contrato para los adaptadores de municipios. Cada adaptador debe implementar este método para obtener y mapear los datos del padrón del municipio al formato estándar utilizado por la aplicación.
	/// </summary>
	public interface IMunicipioAdapter
	{
		/// <summary>
		/// Este método va a internet, trae los datos y los traduce a nuestro estándar
		/// </summary>
		/// <param name="config"></param>
		/// <returns></returns>
		Task<List<PadronEstandarDTO>> ObtenerYMapearDatosAsync(ConfiguracionWSDTO config);
	}
}
