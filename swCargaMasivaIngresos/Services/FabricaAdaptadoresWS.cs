using swCargaMasivaIngresos.Services.Mpios;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace swCargaMasivaIngresos.Services
{
	public static class FabricaAdaptadoresWS
	{
		public static IMunicipioAdapter ObtenerAdaptador(int oficinaId)
		{
			switch (oficinaId)
			{
				case 1:
					return new AdaptadorMunicipio1(); // Ejemplo: Puebla
				//case 2:
				//	return new AdaptadorMunicipio2(); // Ejemplo: Cholula
				default:
					throw new NotSupportedException($"El municipio con OficinaId {oficinaId} aún no tiene un adaptador web service configurado en la API.");
			}
		}
	}
}