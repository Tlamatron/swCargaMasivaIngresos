using swCargaMasivaIngresos.Models; // Importamos el modelo correcto
using System.Collections.Generic;
using System.Threading.Tasks;

namespace swCargaMasivaIngresos.Services
{
	/// <summary>
	/// Interfas que permite procesar cualquier tipo de formato de archivo (txt, xlsx, csv, etc.) y extraer los datos para insertarlos en la base de datos. Cada formato específico tendrá su propia implementación de esta interfaz.
	/// </summary>
	public interface IProcesadorFormato
	{
		/// <summary>
		/// Método asíncrono que procesa un archivo de entrada, lee sus datos y los inserta en la base de datos según el formato específico implementado. Devuelve un objeto ResultadoProceso que contiene información sobre el número de registros exitosos, fallidos y detalles de errores.
		/// </summary>
		/// <param name="rutaArchivo"></param>
		/// <param name="param"></param>
		/// <returns></returns>
		Task<ResultadoProceso> ProcesarAsync(string rutaArchivo, ParametrosCarga param);
	}

	
	/// <summary>
	/// Clase que regresa el resultado del proceso de lectura e inserción de datos en la base de datos, incluyendo el número de registros exitosos, fallidos y una lista de errores detallados para los registros que no se pudieron procesar.
	/// </summary>
	public class ResultadoProceso
	{
		/// <summary>
		/// Conteo de los registros exitosos que se insertaron correctamente en la base de datos después de procesar el archivo. Este número se incrementa por cada línea que cumple con el formato esperado y se inserta sin errores.
		/// </summary>
		public int RegistrosExitosos { get; set; }
		
		/// <summary>
		/// Gets or sets the number of records that failed processing.
		/// </summary>
		/// <remarks>Value is non-negative. Ensure synchronization when reading or updating this property from
		/// multiple threads.</remarks>
		public int RegistrosFallidos { get; set; }
		
		/// <summary>
		/// Gets or sets the collection of error detail messages.
		/// </summary>
		/// <remarks>Contains human-readable messages describing individual errors. The list may be null or
		/// empty.</remarks>
		public List<string> ErroresDetalle { get; set; }

		/// <summary>
		/// Tabla que contiene los registros que fueron rechazados durante el proceso de lectura e inserción de datos. Esta tabla puede incluir información como el número de línea del archivo original, los valores de las columnas y un mensaje de error que explique por qué el registro fue rechazado. Se utiliza para generar reportes o para que el usuario pueda revisar y corregir los datos antes de intentar procesarlos nuevamente.
		/// </summary>
		public System.Data.DataTable TablaRechazados { get; set; }
	}
}