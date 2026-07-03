using swCargaMasivaIngresos.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace swCargaMasivaIngresos.Services
{
	public interface IMunicipioAdapter
	{
		// Este método va a internet, trae los datos y los traduce a nuestro estándar
		Task<List<PadronEstandarDTO>> ObtenerYMapearDatosAsync(ConfiguracionWSDTO config);
	}
}
