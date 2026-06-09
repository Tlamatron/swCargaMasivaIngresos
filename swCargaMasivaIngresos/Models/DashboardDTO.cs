using System.Collections.Generic;

namespace swCargaMasivaIngresos.Models
{
	/// <summary>
	/// DTO utilizado para estructurar la información que se mostrará en el dashboard de monitoreo de cargas masivas. Contiene un objeto con KPIs generales del día, una lista para los datos de la gráfica de pastel (estatus vs cantidad) y una lista con los top 5 focos rojos (oficinas con más errores). Este DTO es el formato que se envía desde el controlador EstadisticasController al cliente para su visualización en el frontend.
	/// </summary>
	public class DashboardDTO
	{
		public KpiGenerales Kpis { get; set; } = new KpiGenerales();
		public List<GraficaEstatus> Grafica { get; set; } = new List<GraficaEstatus>();
		public List<FocoRojo> FocosRojos { get; set; } = new List<FocoRojo>();
	}

	/// <summary>
	/// Clase que representa los KPIs generales que se mostrarán en el dashboard. Incluye el total de cargas realizadas hoy, la cantidad de registros insertados exitosamente, la cantidad de registros que fallaron y la cantidad de cargas que fueron interrumpidas. Estos datos se obtienen a través de una consulta a la base de datos y se utilizan para proporcionar una visión rápida del desempeño del sistema en el día actual.
	/// </summary>
	public class KpiGenerales
	{
		public int CargasTotalesHoy { get; set; }
		public int RegistrosInsertadosHoy { get; set; }
		public int RegistrosFallidosHoy { get; set; }
		public int CargasInterrumpidas { get; set; }
	}

	/// <summary>
	/// Clase que representa los datos necesarios para construir la gráfica de pastel en el dashboard. Cada instancia de esta clase corresponde a un estatus específico (por ejemplo, "Exitosos", "Fallidos", "Interrumpidos") y la cantidad de registros que corresponden a ese estatus. Esta información se utiliza para visualizar la distribución de los resultados de las cargas masivas en el día actual.
	/// </summary>
	public class GraficaEstatus
	{
		public string Estatus { get; set; }
		public int Cantidad { get; set; }
	}

	/// <summary>
	/// Clase que representa un foco rojo en el dashboard, es decir, una oficina que ha tenido un alto número de errores en las cargas masivas. Cada instancia de esta clase contiene el nombre de la oficina y el total de errores asociados a esa oficina. En el dashboard se mostrarán los top 5 focos rojos para que los usuarios puedan identificar rápidamente qué oficinas requieren atención para mejorar su desempeño en las cargas masivas.
	/// </summary>
	public class FocoRojo
	{
		public string NombreOficina { get; set; }
		public int TotalErrores { get; set; }
	}
}