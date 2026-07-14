using swCargaMasivaIngresos.Services.Mpios;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace swCargaMasivaIngresos.Services
{
	/// <summary>
	/// Clase fábrica que devuelve el adaptador de web service correspondiente según el municipio (oficinaId). Permite desacoplar la lógica de negocio de la implementación específica de cada municipio, facilitando la extensión y mantenimiento del código.
	/// </summary>
	public static class FabricaAdaptadoresWS
	{
		/// <summary>
		/// Método que devuelve el adaptador de web service correspondiente según el municipio (oficinaId). Si no existe un adaptador configurado para el municipio, lanza una excepción NotSupportedException.
		/// </summary>
		/// <param name="oficinaId"></param>
		/// <returns></returns>
		/// <exception cref="NotSupportedException"></exception>
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