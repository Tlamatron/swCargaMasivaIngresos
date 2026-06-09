using swCargaMasivaIngresos.Models;
using System;
using System.IO;
using System.Threading.Tasks;

namespace swCargaMasivaIngresos.Services
{
	/// <summary>
	/// Orchestrates a background bulk-import workflow invoked by Hangfire, delegating format-specific processing, logging,
	/// file cleanup and user notification.
	/// </summary>
	/// <remarks>Selects an IProcesadorFormato based on document type and extension, executes processing to produce
	/// a ResultadoProceso, writes INFO/WARN/ERROR entries via LogService, deletes the source file in a finally block, and
	/// attempts to send a notification email when CorreoNotificacion is provided. Designed to be executed asynchronously
	/// and to handle fatal file-read errors by populating ResultadoProceso with error details.</remarks>
	public static class MotorPrincipalCarga
	{
		private static readonly string AppName = System.Configuration.ConfigurationManager.AppSettings["NombAplicacion"] ?? "APICargaMasivaIngresos";



		public static async Task EjecutarEnSegundoPlano(string rutaArchivo, string extension, ParametrosCarga parametros)
		{
			ResultadoProceso resultado = null;
			string nombreOficina = $"OficinaId_{parametros.OficinaId}";

			try
			{
				// 1. Avisamos a la BD que iniciamos el procesamiento
				ControlCargasService.ActualizarEstatus(parametros.FolioCarga, "Procesando en Servidor");

				IProcesadorFormato procesador = FabricaProcesadores.ObtenerProcesador(parametros.TipoCargaId, extension);
				resultado = procesador.Procesar(rutaArchivo, parametros);

				if (resultado.RegistrosFallidos > 0)
				{
					await LogService.WriteLogAsync(AppName, "WARN", parametros.UsuarioLogin, "MotorPrincipalCarga", $"La carga terminó con {resultado.RegistrosFallidos} fallos y {resultado.RegistrosExitosos} exitosos.");
				}

				// 2. Avisamos a la BD que el proceso terminó bien, pasando los contadores
				ControlCargasService.ActualizarEstatus(parametros.FolioCarga, "Procesado Correctamente", resultado.RegistrosExitosos, resultado.RegistrosFallidos);
			}
			catch (Exception ex)
			{
				resultado = new ResultadoProceso { RegistrosExitosos = 0, RegistrosFallidos = 1, ErroresDetalle = new System.Collections.Generic.List<string> { ex.Message } };

				// 3. Avisamos a la BD que hubo un fallo catastrófico
				ControlCargasService.ActualizarEstatus(parametros.FolioCarga, "Error Fatal", 0, 1, ex.Message);
				await LogService.WriteLogAsync(AppName, "ERROR", parametros.UsuarioLogin, "MotorPrincipalCarga", $"ERROR FATAL: {ex.Message}");
				throw;
			}
			finally
			{
				if (File.Exists(rutaArchivo)) File.Delete(rutaArchivo);
			}

			if (!string.IsNullOrWhiteSpace(parametros.CorreoNotificacion))
			{
				await ServicioNotificacion.EnviarCorreoNotificacion(parametros, resultado, parametros.CorreoNotificacion);
				// 4. Actualizamos estatus final post-correo
				if (resultado.RegistrosFallidos == 0) ControlCargasService.ActualizarEstatus(parametros.FolioCarga, "Procesado y Notificado");
			}
		}

		/// <summary>
		/// Este es el método que Hangfire ejecutará de forma aislada e ininterrumpible
		/// </summary>
		/// <param name="rutaArchivo"></param>
		/// <param name="extension"></param>
		/// <param name="parametros"></param>
		/// <returns></returns>
		public static async Task EjecutarEnSegundoPlano_v01(string rutaArchivo, string extension, ParametrosCarga parametros)
		{
			ResultadoProceso resultado = null;
			string nombreOficina = $"OficinaId_{parametros.OficinaId}"; // Esto puede venir de tu BD después

			try 
			{
				// 1. Obtenemos el motor correcto sin importar si es TXT, Excel o JSON en el futuro
				IProcesadorFormato procesador = FabricaProcesadores.ObtenerProcesador(parametros.TipoCargaId, extension);
				// 2. Ejecutamos el procesamiento pesado
				resultado = procesador.Procesar(rutaArchivo, parametros);
				// --- INTEGRACIÓN DE LOG DE RESUMEN / ADVERTENCIAS ---
				if (resultado.RegistrosFallidos > 0)
				{
					await LogService.WriteLogAsync(AppName, "WARN", parametros.UsuarioLogin, "MotorPrincipalCarga", $"[Folio: {parametros.FolioCarga}] La carga de la {nombreOficina} terminó con {resultado.RegistrosFallidos} registros fallidos por layout y {resultado.RegistrosExitosos} exitosos.");
				}
				else
				{
					await LogService.WriteLogAsync(AppName, "INFO", parametros.UsuarioLogin, "MotorPrincipalCarga", $"[Folio: {parametros.FolioCarga}] Carga masiva de la {nombreOficina} finalizada. {resultado.RegistrosExitosos} registros insertados correctamente.");
				}
			}
			catch (Exception ex)
			{
				// Si el archivo estaba corrupto a nivel sistema, lo capturamos
				resultado = new ResultadoProceso
				{
					RegistrosExitosos = 0,
					RegistrosFallidos = 1,
					ErroresDetalle = new System.Collections.Generic.List<string> { "Error fatal al leer el archivo: " + ex.Message }
				};

				await LogService.WriteLogAsync(AppName, "ERROR", parametros.UsuarioLogin, "MotorPrincipalCarga", $"[Folio: {parametros.FolioCarga}] ERROR FATAL: {ex.Message}. Traza: {ex.StackTrace}");
			}
		
			finally
			{
				// 3. BUENA PRÁCTICA: Borrar el archivo TXT de 500MB del servidor una vez insertado en SQL
				if (File.Exists(rutaArchivo))
				{
					File.Delete(rutaArchivo);
				}
			}

			// 4. Enviar el correo al usuario con su resumen
			// --- INTEGRACIÓN CORRECTA DE NOTIFICACIÓN ---
			// Validamos que el correo exista, y se lo pasamos como 3er parámetro
			if (!string.IsNullOrWhiteSpace(parametros.CorreoNotificacion))
			{
				await ServicioNotificacion.EnviarCorreoNotificacion(parametros, resultado, parametros.CorreoNotificacion);
			}
			else
			{
				await LogService.WriteLogAsync(AppName, "WARN", parametros.UsuarioLogin, "MotorPrincipalCarga", $"[Folio: {parametros.FolioCarga}] No se envió correo porque no se proporcionó una dirección válida.");
			}
		}
	}
}