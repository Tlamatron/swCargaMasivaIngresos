using swCargaMasivaIngresos.Models;
using swCargaMasivaIngresos.Services;
using System;
using System.Data;
using System.Data.SqlClient;
using System.Web.Http;

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
		[HttpGet]
		[Route("Dashboard")]
		public IHttpActionResult ObtenerDashboard(int? oficinaId = null, byte? tipoCargaId = null)
		{
			try
			{
				DashboardDTO reporte = new DashboardDTO();
				
				if (reporte.Kpis == null) reporte.Kpis = new KpiGenerales();
				if (reporte.Grafica == null) reporte.Grafica = new System.Collections.Generic.List<GraficaEstatus>();
				if (reporte.FocosRojos == null) reporte.FocosRojos = new System.Collections.Generic.List<FocoRojo>();

				using (SqlConnection conn = new SqlConnection(CadenaConexion))
				using (SqlCommand cmd = new SqlCommand("pred_Operacion.sp_ObtenerEstadisticasDashboard", conn))
				{
					cmd.CommandType = CommandType.StoredProcedure;

					// 🚀 Le pasamos el parámetro al SP (Si es null, el SP devolverá todo el Estado)
					cmd.Parameters.AddWithValue("@OficinaId", (object)oficinaId ?? DBNull.Value);
					cmd.Parameters.AddWithValue("@TipoCargaId", (object)tipoCargaId ?? DBNull.Value);

					conn.Open();

					using (SqlDataReader reader = cmd.ExecuteReader())
					{
						// 1. Leer Primera Tabla (KPIs Generales)
						if (reader.Read())
						{
							reporte.Kpis.CargasTotalesHoy = Convert.ToInt32(reader["CargasTotalesHoy"]);
							reporte.Kpis.RegistrosInsertadosHoy = Convert.ToInt32(reader["RegistrosInsertadosHoy"]);
							reporte.Kpis.RegistrosFallidosHoy = Convert.ToInt32(reader["RegistrosFallidosHoy"]);
							reporte.Kpis.CargasInterrumpidas = Convert.ToInt32(reader["CargasInterrumpidas"]);
						}

						// 2. Saltar a la Segunda Tabla (Gráfica de Pastel)
						if (reader.NextResult())
						{
							while (reader.Read())
							{
								reporte.Grafica.Add(new GraficaEstatus
								{
									Estatus = reader["Estatus"].ToString(),
									Cantidad = Convert.ToInt32(reader["Cantidad"])
								});
							}
						}

						// 3. Saltar a la Tercera Tabla (Top 5 Focos Rojos)
						if (reader.NextResult())
						{
							while (reader.Read())
							{
								reporte.FocosRojos.Add(new FocoRojo
								{
									NombreOficina = reader["NombreOficina"].ToString(),
									TotalErrores = Convert.ToInt32(reader["TotalErrores"])
								});
							}
						}
					}
				}

				return Ok(reporte);
			}
			catch (Exception ex)
			{
				LogService.WriteLogAsync(System.Configuration.ConfigurationManager.AppSettings["NombAplicacion"] ?? "APICargaMasivaIngresos", "ERROR", "", "EstadísticasController", ex.Message);
				return InternalServerError(ex);
			}
		}
	}
}