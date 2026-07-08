using System;

namespace swCargaMasivaIngresos.Services
{
	/// <summary>
	/// Patrón Factory: Centraliza la instanciación del procesador adecuado según el tipo de documento y formato.
	/// Evita tener múltiples "if/else" esparcidos por todo el MotorPrincipalCarga.
	/// </summary>
	public static class FabricaProcesadores
	{
		/// <summary>
		/// Permite obtener el procesador adecuado según el tipo de carga y la extensión del archivo.
		/// </summary>
		/// <param name="tipoCargaId"></param>
		/// <param name="extension"></param>
		/// <returns></returns>
		/// <exception cref="NotSupportedException"></exception>
		public static IProcesadorFormato ObtenerProcesador(int tipoCargaId, string extension)
		{
			string ext = extension.ToLower().Trim();

			// 1. RUTA PARA ARCHIVOS DE TEXTO PLANO (TXT y CSV)
			if (ext == ".txt" || ext == ".csv")
			{
				switch (tipoCargaId)
				{
					case 1: return new ProcesadorPadronTXT();
					case 2: return new ProcesadorPagosUniversal(); 
					case 3: return new ProcesadorReduccionesTXT();
					default: throw new NotSupportedException($"El TipoCargaId '{tipoCargaId}' no existe en texto.");
				}
			}

			// 2. RUTA PARA ARCHIVOS DE EXCEL (XLSX y XLS)
			if (ext == ".xlsx" || ext == ".xls")
			{
				switch (tipoCargaId)
				{
					case 1: return new ProcesadorPadronExcel();
					case 2: return new ProcesadorPagosExcel(); 
					case 3: return new ProcesadorReduccionesExcel();
					default: throw new NotSupportedException($"El TipoCargaId '{tipoCargaId}' no existe en Excel.");
				}
			}

			throw new NotSupportedException($"Extensión de archivo no soportada: {extension}");
		}
	}
}