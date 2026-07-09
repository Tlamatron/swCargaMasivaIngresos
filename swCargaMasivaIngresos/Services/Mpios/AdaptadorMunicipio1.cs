using Newtonsoft.Json;
using swCargaMasivaIngresos.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.Web;

namespace swCargaMasivaIngresos.Services.Mpios
{
	/// <summary>
	/// Clase adaptadora para el municipio 1. Esta clase implementa la interfaz IMunicipioAdapter y se encarga de obtener y mapear los datos del padrón del municipio 1 al formato estándar utilizado por la aplicación.
	/// </summary>
	public class AdaptadorMunicipio1 : IMunicipioAdapter
	{
		/// <summary>
		/// Método que obtiene y mapea los datos del padrón del municipio 1 al formato estándar. Este método realiza una petición HTTP al endpoint del municipio, aplica la autenticación necesaria, deserializa la respuesta y mapea los datos al formato estándar definido por PadronEstandarDTO.
		/// </summary>
		/// <param name="config"></param>
		/// <returns></returns>
		/// <exception cref="Exception"></exception>
		public async Task<List<PadronEstandarDTO>> ObtenerYMapearDatosAsync(ConfiguracionWSDTO config)
		{
			var listaEstandar = new List<PadronEstandarDTO>();

			using (HttpClient client = new HttpClient())
			{
				// 1. APLICAR LA SEGURIDAD (Tipo 1 = Bearer Token)
				if (config.TipoAutenticacion == 1 && !string.IsNullOrEmpty(config.Credencial2))
				{
					client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", config.Credencial2);
				}

				// 2. HACER LA PETICIÓN AL SERVIDOR DEL MUNICIPIO
				HttpResponseMessage response = await client.GetAsync(config.EndpointUrl);

				if (!response.IsSuccessStatusCode)
				{
					throw new Exception($"El servidor del municipio respondió con error: {response.StatusCode}");
				}

				string jsonRespuesta = await response.Content.ReadAsStringAsync();

				// 3. DESERIALIZAR AL FORMATO "RARO" DEL MUNICIPIO
				// jsonplaceholder devuelve un arreglo con { userId, id, title, body }
				var datosCrudos = JsonConvert.DeserializeObject<List<dynamic>>(jsonRespuesta);

				// 4. EL MAPEO: Traducir del formato del municipio a NUESTRO formato estándar
				foreach (var item in datosCrudos)
				{
					var dto = new PadronEstandarDTO
					{
						ClaveMunicipio = 114, // Lo forzamos porque sabemos que es este adaptador
						TipoPredio = 1,

						// Aquí hacemos la traducción: Usamos su 'id' como si fuera nuestra 'CuentaPredial'
						CuentaPredial = Convert.ToString(item.id),

						// Simulamos montos 
						BaseGravable = 150000m,
						ImpuestoDeterminado = 1250.50m
					};

					listaEstandar.Add(dto);
				}
			}

			return listaEstandar;
		}
	}
}