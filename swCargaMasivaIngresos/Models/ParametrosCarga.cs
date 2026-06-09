using System;

namespace swCargaMasivaIngresos.Models
{
	/// <summary>
	/// Representa el conjunto de parámetros de control y negocio requeridos para procesar una carga masiva.
	/// </summary>
	public class ParametrosCarga
	{
		/// <summary>
		/// Identificador único de la carga masiva, generado como un GUID. Este valor se utiliza para rastrear y correlacionar todas las operaciones, registros y logs asociados a esta carga específica a lo largo de su ciclo de vida.
		/// </summary>
		public string FolioCarga { get; set; }

		/// <summary>
		/// Identificador de la oficina para la cual se está realizando la carga masiva. Este valor es crucial para segmentar y organizar los datos, así como para aplicar las reglas de negocio específicas de cada oficina durante el procesamiento de la carga.
		/// </summary>
		public int OficinaId { get; set; }

		/// <summary>
		/// Nombre de usuario del operador que está iniciando la carga masiva. Este valor se utiliza para propósitos de auditoría, control de acceso y para personalizar las notificaciones o reportes relacionados con esta carga. Es importante que este valor sea preciso para mantener la integridad de los registros y facilitar el seguimiento de las actividades del usuario.
		/// </summary>
		public string UsuarioLogin { get; set; }

		// 🚀 RENOMBRADO PARA COINCIDIR CON LA BASE DE DATOS
		/// <summary>
		/// 1 = Padrón de Adeudos, 2 = Pagos Locales (Etiquetado), 3 = Reducciones.
		/// </summary>
		public int TipoCargaId { get; set; }
		
		/// <summary>
		/// Periodo de inicio para el cual se está realizando la carga masiva.
		/// </summary>
		public int PeriodoInicio { get; set; }

		/// <summary>
		/// Periodo de fin para el cual se está realizando la carga masiva. Este valor, junto con el periodo de inicio, define el rango temporal de los datos que se procesarán en esta carga. Es fundamental que ambos periodos sean consistentes y válidos para evitar errores durante el procesamiento y garantizar que los datos se asignen correctamente a los periodos correspondientes en la base de datos.
		/// </summary>
		public int PeriodoFin { get; set; }

		/// <summary>
		/// Correo electrónico al cual se enviarán las notificaciones relacionadas con el estado y resultado de la carga masiva. Este valor es esencial para mantener informados a los usuarios o administradores sobre el progreso, éxito o cualquier error que ocurra durante el proceso de carga. Es importante que este correo sea válido y esté monitoreado para asegurar una comunicación efectiva.
		/// </summary>
		public string CorreoNotificacion { get; set; }
	}
}