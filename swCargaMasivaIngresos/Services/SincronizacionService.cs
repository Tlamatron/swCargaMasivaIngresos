using swCargaMasivaIngresos.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web;

namespace swCargaMasivaIngresos.Services
{
	/// <summary>
	/// Este servicio es el que une todas las piezas que diseñamos anteriormente: los DTOs, la Fábrica y los Adaptadores.
	/// </summary>
	public static class SincronizacionService
	{
		private static readonly string AppName = System.Configuration.ConfigurationManager.AppSettings["NombAplicacion"] ?? "swCargaMasivaIngresos";

		public static async Task<ResultadoProceso> EjecutarSincronizacionAsync(int oficinaId, string usuarioLogin)
		{
			var resultado = new ResultadoProceso { ErroresDetalle = new List<string>() };

			try
			{
				// 1. Obtener la "Receta" (URL y Credenciales) de la BD
				var config = ConfiguracionWSService.ObtenerConfiguracion(oficinaId);
				if (config == null)
				{
					throw new Exception($"No hay configuración de Web Service activa para la Oficina {oficinaId}.");
				}

				await LogService.WriteLogAsync("INFO", usuarioLogin, "SincronizacionService", $"Iniciando sincronización para Oficina {oficinaId}. Endpoint: {config.EndpointUrl}");

				// 2. Pedirle a la Fábrica el traductor correcto
				IMunicipioAdapter adaptador = FabricaAdaptadoresWS.ObtenerAdaptador(oficinaId);

				// 3. Ejecutar la magia: Ir a internet, descargar el JSON raro y mapearlo a tu estándar
				List<PadronEstandarDTO> datosListos = await adaptador.ObtenerYMapearDatosAsync(config);

				resultado.RegistrosExitosos = datosListos.Count;

				// ================================================================
				// 4. AQUÍ SE INSERTAN EN TU BASE DE DATOS
				// Como los datos ya están mapeados a tu estándar, el siguiente paso 
				// natural es convertirlos a un DataTable y enviarlos con tu método
				// de SqlBulkCopy, idéntico a como lo haces con los TXT y Excel.
				// ================================================================

				await LogService.WriteLogAsync("INFO", usuarioLogin, "SincronizacionService", $"Sincronización exitosa. Se obtuvieron y tradujeron {datosListos.Count} registros del municipio.");
			}
			catch (Exception ex)
			{
				resultado.RegistrosFallidos = 1;
				resultado.ErroresDetalle.Add(ex.Message);
				await LogService.WriteLogAsync("ERROR", usuarioLogin, "SincronizacionService", $"Fallo grave en sincronización de Oficina {oficinaId}: {ex.Message}");
			}

			return resultado;
		}
	}
}