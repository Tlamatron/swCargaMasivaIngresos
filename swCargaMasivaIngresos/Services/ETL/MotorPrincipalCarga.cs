using swCargaMasivaIngresos.Models;
using System;
using System.IO;
using System.Threading.Tasks;
using Hangfire;

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

		/// <summary>
		/// Este es el método que Hangfire ejecutará de forma aislada e ininterrumpible
		/// </summary>
		/// <param name="rutaArchivo"></param>
		/// <param name="extension"></param>
		/// <param name="parametros"></param>
		/// <returns></returns>
		[AutomaticRetry(Attempts = 0)]
		public static async Task EjecutarEnSegundoPlano(string rutaArchivo, string extension, ParametrosCarga parametros)
		{
			ResultadoProceso resultado = null;
			bool ocurrioErrorFatal = false;
			string mensajeErrorFatal = "";

			try
			{
				ControlCargasService.ActualizarEstatus(parametros.FolioCarga, "Procesando en Servidor");

				IProcesadorFormato procesador = FabricaProcesadores.ObtenerProcesador(parametros.TipoCargaId, extension);
				resultado = await procesador.ProcesarAsync(rutaArchivo, parametros);

				// 1. DETECCIÓN DE ARCHIVO "FANTASMA"
				// Si leyó el archivo pero no sacó nada, forzamos un error para notificar al usuario.
				if (resultado.RegistrosExitosos == 0 && resultado.RegistrosFallidos == 0)
				{
					throw new Exception("El archivo fue escaneado, pero no se encontró información válida (Ej. Encabezados no detectados o archivo vacío).");
				}

				if (resultado.RegistrosFallidos > 0)
				{
					await LogService.WriteLogAsync("WARN", parametros.UsuarioLogin, "MotorPrincipalCarga", $"La carga terminó con {resultado.RegistrosFallidos} fallos y {resultado.RegistrosExitosos} exitosos.");
				}

				ControlCargasService.ActualizarEstatus(parametros.FolioCarga, "Procesado Correctamente", resultado.RegistrosExitosos, resultado.RegistrosFallidos);
			}
			catch (Exception ex)
			{
				ocurrioErrorFatal = true;
				mensajeErrorFatal = ex.Message;

				// 🚀 2. CREAMOS EL RESULTADO DE ERROR FATAL PARA EL CORREO
				resultado = new ResultadoProceso
				{
					RegistrosExitosos = 0,
					RegistrosFallidos = 1,
					ErroresDetalle = new System.Collections.Generic.List<string> { $"Error Crítico: {ex.Message}" }
				};

				ControlCargasService.ActualizarEstatus(parametros.FolioCarga, "Error Fatal", 0, 1, ex.Message);
				await LogService.WriteLogAsync("ERROR", parametros.UsuarioLogin, "MotorPrincipalCarga", $"ERROR FATAL: {ex.Message}");
			}
			finally
			{
				if (File.Exists(rutaArchivo)) File.Delete(rutaArchivo);
			}

			// 🚀 3. EL CORREO AHORA SIEMPRE SE ENVÍA (Incluso si explotó el catch)
			if (!string.IsNullOrWhiteSpace(parametros.CorreoNotificacion))
			{
				await ServicioNotificacion.EnviarCorreoNotificacion(parametros, resultado, parametros.CorreoNotificacion);

				if (!ocurrioErrorFatal && resultado.RegistrosFallidos == 0)
				{
					ControlCargasService.ActualizarEstatus(parametros.FolioCarga, "Procesado y Notificado");
				}
			}

			// 🚀 4. AHORA SÍ, LE AVISAMOS A HANGFIRE QUE EL TRABAJO FALLÓ (Para su panel de control)
			if (ocurrioErrorFatal)
			{
				throw new Exception(mensajeErrorFatal);
			}
		}
	
	}
}