using System;

namespace swCargaMasivaIngresos.Services
{
	/// <summary>
	/// Patrón Factory: Centraliza la instanciación del procesador adecuado según el tipo de documento y formato.
	/// Evita tener múltiples "if/else" esparcidos por todo el MotorPrincipalCarga.
	/// </summary>
	public static class FabricaProcesadores
	{
		public static IProcesadorFormato ObtenerProcesador(int tipoCargaId, string extension)
		{
			string ext = extension.ToLower().Trim();

			// 🚀 REGLA UNIVERSAL PARA TIPO 2 (PAGOS)
			// No importa si es Excel, TXT o CSV, el Motor Universal lo procesa.
			if (tipoCargaId == 2)
			{
				return new ProcesadorPagosUniversal();
			}

			// -------------------------------------------------------------
			// REGLAS ANTIGUAS (Para Tipo 1 y 3) que se mantienen intactas
			// -------------------------------------------------------------
			if (ext == ".txt" || ext == ".csv")
			{
				switch (tipoCargaId)
				{
					case 1: return new ProcesadorPadronTXT();
					case 3: return new ProcesadorReduccionesTXT();
					default: throw new NotSupportedException($"El TipoCargaId '{tipoCargaId}' no existe en texto.");
				}
			}

			if (ext == ".xlsx" || ext == ".xls")
			{
				switch (tipoCargaId)
				{
					case 1: return new ProcesadorPadronExcel();
					case 3: return new ProcesadorReduccionesExcel();
					default: throw new NotSupportedException($"El TipoCargaId '{tipoCargaId}' no existe en Excel.");
				}
			}

			throw new NotSupportedException($"La extensión '{ext}' no está soportada actualmente por el sistema.");
		}
	}
}