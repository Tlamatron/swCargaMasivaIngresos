using swCargaMasivaIngresos.Services;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Threading.Tasks;
using System.Web.Http;
using System.Linq;

namespace swCargaMasivaIngresos.Controllers
{
	/// <summary>
	/// Controlador API encargado de proporcionar estadísticas y datos para el dashboard de monitoreo de cargas masivas. Expone un endpoint GET que devuelve un DTO con KPIs generales, datos para una gráfica de pastel y un listado de los top 5 focos rojos (oficinas con más errores). La información se obtiene a través de una consulta a la base de datos utilizando un procedimiento almacenado optimizado para este propósito.
	/// </summary>
	[RoutePrefix("api/Estadisticas")]
	public class EstadisticasController : ApiController
	{
		private readonly string CadenaConexion = ConfiguracionApp.ObtenerCadenaConexion();

		/// <summary>
		/// Endpoint GET que devuelve un DTO con las estadísticas necesarias para el dashboard. El método ejecuta un procedimiento almacenado que retorna tres conjuntos de resultados: KPIs generales, datos para la gráfica de pastel y el top 5 de focos rojos. La respuesta se estructura en un objeto DashboardDTO que se envía al cliente.
		/// </summary>
		/// <returns></returns>
		//[HttpGet]
		//[Route("Dashboard")]
		//public IHttpActionResult ObtenerDashboard(int? oficinaId = null, byte? tipoCargaId = null)
		//{
		//	try
		//	{
		//		DashboardDTO reporte = new DashboardDTO();
				
		//		if (reporte.Kpis == null) reporte.Kpis = new KpiGenerales();
		//		if (reporte.Grafica == null) reporte.Grafica = new System.Collections.Generic.List<GraficaEstatus>();
		//		if (reporte.FocosRojos == null) reporte.FocosRojos = new System.Collections.Generic.List<FocoRojo>();

		//		using (SqlConnection conn = new SqlConnection(CadenaConexion))
		//		using (SqlCommand cmd = new SqlCommand("pred_Operacion.sp_ObtenerEstadisticasDashboard", conn))
		//		{
		//			cmd.CommandType = CommandType.StoredProcedure;

		//			// 🚀 Le pasamos el parámetro al SP (Si es null, el SP devolverá todo el Estado)
		//			cmd.Parameters.AddWithValue("@OficinaId", (object)oficinaId ?? DBNull.Value);
		//			cmd.Parameters.AddWithValue("@TipoCargaId", (object)tipoCargaId ?? DBNull.Value);

		//			conn.Open();

		//			using (SqlDataReader reader = cmd.ExecuteReader())
		//			{
		//				// 1. Leer Primera Tabla (KPIs Generales)
		//				if (reader.Read())
		//				{
		//					reporte.Kpis.CargasTotalesHoy = Convert.ToInt32(reader["CargasTotalesHoy"]);
		//					reporte.Kpis.RegistrosInsertadosHoy = Convert.ToInt32(reader["RegistrosInsertadosHoy"]);
		//					reporte.Kpis.RegistrosFallidosHoy = Convert.ToInt32(reader["RegistrosFallidosHoy"]);
		//					reporte.Kpis.CargasInterrumpidas = Convert.ToInt32(reader["CargasInterrumpidas"]);
		//				}

		//				// 2. Saltar a la Segunda Tabla (Gráfica de Pastel)
		//				if (reader.NextResult())
		//				{
		//					while (reader.Read())
		//					{
		//						reporte.Grafica.Add(new GraficaEstatus
		//						{
		//							Estatus = reader["Estatus"].ToString(),
		//							Cantidad = Convert.ToInt32(reader["Cantidad"])
		//						});
		//					}
		//				}

		//				// 3. Saltar a la Tercera Tabla (Top 5 Focos Rojos)
		//				if (reader.NextResult())
		//				{
		//					while (reader.Read())
		//					{
		//						reporte.FocosRojos.Add(new FocoRojo
		//						{
		//							NombreOficina = reader["NombreOficina"].ToString(),
		//							TotalErrores = Convert.ToInt32(reader["TotalErrores"])
		//						});
		//					}
		//				}
		//			}
		//		}

		//		return Ok(reporte);
		//	}
		//	catch (Exception ex)
		//	{
		//		LogService.WriteLogAsync("ERROR", "", "EstadísticasController", ex.Message);
		//		return InternalServerError(ex);
		//	}
		//}


		/// <summary>
		/// Endpoint asíncrono que devuelve la Inteligencia de Negocio del sistema.
		/// Soporta modo "Dashboard General" y modo "Radiografía de Folio".
		/// </summary>
		[HttpGet]
		[Route("InteligenciaNegocio")]
		public async Task<IHttpActionResult> InteligenciaNegocio(
			int? oficinaId = null,
			byte? tipoCargaId = null,
			int? folioCarga = null,
			int? municipioInicio = null,
			int? municipioFin = null)
		{
			try
			{
				// Usamos un diccionario dinámico para no depender de DTOs rígidos
				var response = new Dictionary<string, object>();
				response["EsVistaFolio"] = folioCarga.HasValue;

				using (SqlConnection conn = new SqlConnection(CadenaConexion))
				{
					await conn.OpenAsync();
					using (SqlCommand cmd = new SqlCommand("pred_Operacion.sp_ObtenerEstadisticasDashboard", conn))
					{
						cmd.CommandType = CommandType.StoredProcedure;

						// Inyección segura de parámetros
						cmd.Parameters.AddWithValue("@OficinaId", (object)oficinaId ?? DBNull.Value);
						cmd.Parameters.AddWithValue("@TipoCargaId", (object)tipoCargaId ?? DBNull.Value);
						cmd.Parameters.AddWithValue("@FolioCarga", (object)folioCarga ?? DBNull.Value);
						cmd.Parameters.AddWithValue("@MunicipioInicio", (object)municipioInicio ?? DBNull.Value);
						cmd.Parameters.AddWithValue("@MunicipioFin", (object)municipioFin ?? DBNull.Value);

						using (var reader = await cmd.ExecuteReaderAsync())
						{
							if (folioCarga.HasValue)
							{
								// =======================================================
								// MODO 1: Vista de Folio (3 Result Sets)
								// =======================================================
								response["Cabecera"] = MapearDataReader(reader);
								if (await reader.NextResultAsync()) response["ImpactoFinanciero"] = MapearDataReader(reader);
								if (await reader.NextResultAsync()) response["FocosRojos"] = MapearDataReader(reader);
							}
							else
							{
								// =======================================================
								// MODO 2: Dashboard General (5 Result Sets)
								// =======================================================
								var productividad = MapearDataReader(reader);
								response["Productividad"] = productividad;

								if (await reader.NextResultAsync()) response["DesgloseAnios"] = MapearDataReader(reader);
								if (await reader.NextResultAsync()) response["DesglosePredio"] = MapearDataReader(reader);
								if (await reader.NextResultAsync()) response["DesglosePago"] = MapearDataReader(reader);
								if (await reader.NextResultAsync()) response["FocosRojos"] = MapearDataReader(reader);

								// 🚀 MAGIA EN C#: Reconstruimos los KPIs sumando la productividad
								// Esto evita que modifiquemos el SP nuevamente y le da al Javascript justo lo que pide
								response["KPIsOperativos"] = new
								{
									CargasTotalesHoy = productividad.Sum(x => Convert.ToInt32(x["ArchivosSubidos"])),
									RegistrosInsertadosHoy = productividad.Sum(x => Convert.ToInt32(x["RegistrosLeidosFormato"])),
									RegistrosFallidosHoy = productividad.Sum(x => Convert.ToInt32(x["RegistrosRechazadosFormato"]))
								};

								response["ImpactoFinanciero"] = new List<object>
								{
									new
									{
										Periodo = "Recaudación Consolidada (Año Actual)",
										MontoRecuperado = productividad.Sum(x => Convert.ToDecimal(x["MontoRecuperado"])),
										CuentasConsolidadas = productividad.Sum(x => Convert.ToInt32(x["CuentasConsolidadasBD"]))
									}
								};
							}
						}
					}
				}

				return Ok(response);
			}
			catch (Exception ex)
			{
				// Capturamos cualquier error en el log físico
				LogService.WriteLogAsync("ERROR", "", "EstadisticasController", ex.Message).Wait();
				return InternalServerError(ex);
			}
		}

		/// <summary>
		/// Función auxiliar que convierte cualquier tabla devuelta por SQL en una lista de diccionarios,
		/// permitiendo que .NET la serialice automáticamente a un JSON perfecto.
		/// </summary>
		private List<Dictionary<string, object>> MapearDataReader(SqlDataReader reader)
		{
			var lista = new List<Dictionary<string, object>>();
			while (reader.Read())
			{
				var fila = new Dictionary<string, object>();
				for (int i = 0; i < reader.FieldCount; i++)
				{
					// Si es nulo en BD, lo mandamos como nulo en JSON
					fila[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
				}
				lista.Add(fila);
			}
			return lista;
		}
	}
}