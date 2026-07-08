using System.Collections.Generic;

namespace swCargaMasivaIngresos.Models
{
	/// <summary>
	/// DTO utilizado para estructurar la información que se mostrará en el dashboard de monitoreo de cargas masivas. Contiene un objeto con KPIs generales del día, una lista para los datos de la gráfica de pastel (estatus vs cantidad) y una lista con los top 5 focos rojos (oficinas con más errores). Este DTO es el formato que se envía desde el controlador EstadisticasController al cliente para su visualización en el frontend.
	/// </summary>
	public class DashboardDTO
	{
		/// <summary>
		/// Guarda los KPIs generales que se mostrarán en el dashboard, incluyendo el total de cargas realizadas hoy, la cantidad de registros insertados exitosamente, la cantidad de registros que fallaron y la cantidad de cargas que fueron interrumpidas. Estos datos se obtienen a través de una consulta a la base de datos y se utilizan para proporcionar una visión rápida del desempeño del sistema en el día actual.
		/// </summary>
		public KpiGenerales Kpis { get; set; } = new KpiGenerales();

		/// <summary>
		/// Guarda los datos necesarios para construir la gráfica de pastel en el dashboard, donde cada instancia de GraficaEstatus corresponde a un estatus específico (por ejemplo, "Exitosos", "Fallidos", "Interrumpidos") y la cantidad de registros que corresponden a ese estatus. Esta información se utiliza para visualizar la distribución de los resultados de las cargas masivas en el día actual.
		/// </summary>
		public List<GraficaEstatus> Grafica { get; set; } = new List<GraficaEstatus>();

		/// <summary>
		/// Guarda los top 5 focos rojos, es decir, las oficinas que han tenido un alto número de errores en las cargas masivas. Cada instancia de FocoRojo contiene el nombre de la oficina y el total de errores asociados a esa oficina. En el dashboard se mostrarán estos focos rojos para que los usuarios puedan identificar rápidamente qué oficinas requieren atención para mejorar su desempeño en las cargas masivas.
		/// </summary>
		public List<FocoRojo> FocosRojos { get; set; } = new List<FocoRojo>();
	}

	/// <summary>
	/// Clase que representa los KPIs generales que se mostrarán en el dashboard. Incluye el total de cargas realizadas hoy, la cantidad de registros insertados exitosamente, la cantidad de registros que fallaron y la cantidad de cargas que fueron interrumpidas. Estos datos se obtienen a través de una consulta a la base de datos y se utilizan para proporcionar una visión rápida del desempeño del sistema en el día actual.
	/// </summary>
	public class KpiGenerales
	{
		/// <summary>
		/// Guarda el total de cargas realizadas hoy. Este valor se obtiene a través de una consulta a la base de datos que cuenta todas las cargas masivas que se han ejecutado en el día actual, independientemente de su estatus (exitosas, fallidas o interrumpidas). Este KPI proporciona una visión general del volumen de actividad en el sistema durante el día.
		/// </summary>
		public int CargasTotalesHoy { get; set; }

		/// <summary>
		/// Guarda la cantidad de registros que fueron insertados exitosamente en la base de datos durante las cargas masivas del día actual. Este valor se obtiene a través de una consulta a la base de datos que cuenta todos los registros que se han procesado y almacenado correctamente, proporcionando una métrica clave sobre la efectividad del sistema en la inserción de datos válidos.
		/// </summary>
		public int RegistrosInsertadosHoy { get; set; }

		/// <summary>
		/// Guarda la cantidad de registros que fallaron durante las cargas masivas del día actual. Este valor se obtiene a través de una consulta a la base de datos que cuenta todos los registros que no pudieron ser procesados o insertados correctamente, proporcionando una métrica clave sobre los problemas o errores que ocurrieron durante el procesamiento de datos.
		/// </summary>
		public int RegistrosFallidosHoy { get; set; }

		/// <summary>
		/// Guarda la cantidad de cargas que fueron interrumpidas durante el día actual. Este valor se obtiene a través de una consulta a la base de datos que cuenta todas las cargas masivas que no pudieron completarse debido a errores críticos, problemas de conectividad u otras interrupciones, proporcionando una métrica clave sobre la estabilidad y confiabilidad del sistema en el procesamiento de cargas masivas.
		/// </summary>
		public int CargasInterrumpidas { get; set; }
	}

	/// <summary>
	/// Clase que representa los datos necesarios para construir la gráfica de pastel en el dashboard. Cada instancia de esta clase corresponde a un estatus específico (por ejemplo, "Exitosos", "Fallidos", "Interrumpidos") y la cantidad de registros que corresponden a ese estatus. Esta información se utiliza para visualizar la distribución de los resultados de las cargas masivas en el día actual.
	/// </summary>
	public class GraficaEstatus
	{
		/// <summary>
		/// Identifica el estatus de los registros en la carga masiva, como "Exitosos", "Fallidos" o "Interrumpidos". Este valor se utiliza para categorizar los resultados de las cargas masivas y se muestra en la gráfica de pastel del dashboard para proporcionar una visión clara de cómo se distribuyen los resultados según su estatus.
		/// </summary>
		public string Estatus { get; set; }

		/// <summary>
		/// Indica la cantidad de registros que corresponden al estatus especificado. Este valor se obtiene a través de una consulta a la base de datos que cuenta todos los registros asociados a cada estatus, y se utiliza para construir la gráfica de pastel en el dashboard, permitiendo a los usuarios visualizar rápidamente la proporción de registros exitosos, fallidos e interrumpidos en las cargas masivas del día actual.
		/// </summary>
		public int Cantidad { get; set; }
	}

	/// <summary>
	/// Clase que representa un foco rojo en el dashboard, es decir, una oficina que ha tenido un alto número de errores en las cargas masivas. Cada instancia de esta clase contiene el nombre de la oficina y el total de errores asociados a esa oficina. En el dashboard se mostrarán los top 5 focos rojos para que los usuarios puedan identificar rápidamente qué oficinas requieren atención para mejorar su desempeño en las cargas masivas.
	/// </summary>
	public class FocoRojo
	{
		/// <summary>
		/// Identifica el nombre de la oficina que ha tenido un alto número de errores en las cargas masivas. Este valor se obtiene a través de una consulta a la base de datos que agrupa los errores por oficina y se utiliza para mostrar en el dashboard cuáles son las oficinas con mayor cantidad de problemas, permitiendo a los usuarios tomar medidas correctivas.
		/// </summary>
		public string NombreOficina { get; set; }

		/// <summary>
		/// Guarda el total de errores asociados a la oficina especificada. Este valor se obtiene a través de una consulta a la base de datos que cuenta todos los registros fallidos o con problemas asociados a cada oficina, y se utiliza para mostrar en el dashboard la magnitud de los problemas en cada foco rojo, permitiendo a los usuarios priorizar acciones correctivas según la gravedad de los errores.
		/// </summary>
		public int TotalErrores { get; set; }
	}
}