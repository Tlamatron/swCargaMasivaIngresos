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
	/// Controlador para exponer los endpoints de Estadísticas e Inteligencia de Negocio del sistema.
	/// </summary>
	[RoutePrefix("api/Estadisticas")]
	public class EstadisticasController : ApiController
	{
		private readonly string CadenaConexion = ConfiguracionApp.ObtenerCadenaConexion();

		/// <summary>
		/// Endpoint asíncrono que devuelve la Inteligencia de Negocio del sistema.
		/// Soporta modo "Dashboard General" y modo "Radiografía de Folio" con filtrado por fechas.
		/// </summary>
		[HttpGet]
		[Route("InteligenciaNegocio")]
		public async Task<IHttpActionResult> InteligenciaNegocio(
			int? oficinaId = null,
			byte? tipoCargaId = null,
			int? folioCarga = null,
			int? municipioInicio = null,
			int? municipioFin = null,
			DateTime? fechaInicio = null,
			DateTime? fechaFin = null) 
		{
			try
			{
				var response = new Dictionary<string, object>();
				response["EsVistaFolio"] = folioCarga.HasValue;

				using (SqlConnection conn = new SqlConnection(CadenaConexion))
				{
					await conn.OpenAsync();
					using (SqlCommand cmd = new SqlCommand("pred.sp_ObtenerEstadisticasDashboard", conn))
					{
						cmd.CommandType = CommandType.StoredProcedure;

						// Inyección segura de parámetros
						cmd.Parameters.AddWithValue("@OficinaId", (object)oficinaId ?? DBNull.Value);
						cmd.Parameters.AddWithValue("@TipoCargaId", (object)tipoCargaId ?? DBNull.Value);
						cmd.Parameters.AddWithValue("@FolioCarga", (object)folioCarga ?? DBNull.Value);
						cmd.Parameters.AddWithValue("@MunicipioInicio", (object)municipioInicio ?? DBNull.Value);
						cmd.Parameters.AddWithValue("@MunicipioFin", (object)municipioFin ?? DBNull.Value);

						// 🚀 Pasamos las fechas a SQL Server
						cmd.Parameters.AddWithValue("@FechaInicio", (object)fechaInicio ?? DBNull.Value);
						cmd.Parameters.AddWithValue("@FechaFin", (object)fechaFin ?? DBNull.Value);

						using (var reader = await cmd.ExecuteReaderAsync())
						{
							if (folioCarga.HasValue)
							{
								// =======================================================
								// MODO 1: Vista de Folio (4 Result Sets devueltos por SQL)
								// =======================================================
								response["Cabecera"] = MapearDataReader(reader);                 // Tabla 1: Rendimiento
								if (await reader.NextResultAsync()) response["AnioEnCurso"] = MapearDataReader(reader);  // Tabla 2: Dinero Año Actual
								if (await reader.NextResultAsync()) response["Historico"] = MapearDataReader(reader);    // Tabla 3: Dinero Histórico
								if (await reader.NextResultAsync()) response["FocosRojos"] = MapearDataReader(reader);   // Tabla 4: Errores
							}
							else
							{
								// =======================================================
								// MODO 2: Dashboard General (7 Result Sets devueltos por SQL)
								// =======================================================
								var productividad = MapearDataReader(reader);
								response["Productividad"] = productividad; // Tabla 1

								if (await reader.NextResultAsync()) response["AnioEnCurso"] = MapearDataReader(reader);    // Tabla 2
								if (await reader.NextResultAsync()) response["Historico"] = MapearDataReader(reader);      // Tabla 3
								if (await reader.NextResultAsync()) response["DesgloseAnios"] = MapearDataReader(reader);  // Tabla 4
								if (await reader.NextResultAsync()) response["DesglosePredio"] = MapearDataReader(reader); // Tabla 5
								if (await reader.NextResultAsync()) response["DesglosePago"] = MapearDataReader(reader);   // Tabla 6
								if (await reader.NextResultAsync()) response["FocosRojos"] = MapearDataReader(reader);     // Tabla 7

								// 🚀 MAGIA EN C#: Reconstruimos los KPIs para la barra superior del Dashboard
								response["KPIsOperativos"] = new
								{
									ArchivosSubidos = productividad.Sum(x => Convert.ToInt32(x["ArchivosSubidos"])),
									RegistrosLeidos = productividad.Sum(x => Convert.ToInt32(x["RegistrosLeidosFormato"])),
									RegistrosRechazados = productividad.Sum(x => Convert.ToInt32(x["RegistrosRechazadosFormato"]))
								};

								// 🚀 KPIs Financieros Acumulados
								var tablaAnioEnCurso = (List<Dictionary<string, object>>)response["AnioEnCurso"];
								var tablaHistorico = (List<Dictionary<string, object>>)response["Historico"];

								response["ImpactoFinanciero"] = new
								{
									MontoAnioEnCurso = tablaAnioEnCurso.Sum(x => Convert.ToDecimal(x["MontoRecuperado"])),
									MontoHistorico = tablaHistorico.Sum(x => Convert.ToDecimal(x["MontoRecuperado"])),
									CuentasConsolidadasTotales = tablaAnioEnCurso.Sum(x => Convert.ToInt32(x["CuentasConsolidadasBD"])) +
																 tablaHistorico.Sum(x => Convert.ToInt32(x["CuentasConsolidadasBD"]))
								};
							}
						}
					}
				}

				return Ok(response);
			}
			catch (Exception ex)
			{
				LogService.WriteLogAsync("ERROR", "", "EstadisticasController", ex.Message).Wait();
				return InternalServerError(ex);
			}
		}

		/// <summary>
		/// Mapea un SqlDataReader a una lista de diccionarios, donde cada diccionario representa una fila con nombre de columna y valor.
		/// </summary>
		/// <param name="reader"></param>
		/// <returns></returns>
		private List<Dictionary<string, object>> MapearDataReader(SqlDataReader reader)
		{
			var lista = new List<Dictionary<string, object>>();
			while (reader.Read())
			{
				var fila = new Dictionary<string, object>();
				for (int i = 0; i < reader.FieldCount; i++)
				{
					fila[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
				}
				lista.Add(fila);
			}
			return lista;
		}
	}
}