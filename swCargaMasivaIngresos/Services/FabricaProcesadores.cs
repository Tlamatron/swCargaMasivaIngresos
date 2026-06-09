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

			if (ext == ".txt")
			{
				switch (tipoCargaId)
				{
					case 1:
						return new ProcesadorPadronTXT(); // Layout 24 Columnas (Crea Deuda)
					case 2:
						return new ProcesadorPagosTXT(); // Layout 24 Columnas (Etiqueta Pagados)
					case 3:
						return new ProcesadorReduccionesTXT(); // Layout 5 Columnas (Asigna Descuentos)
					default:
						throw new NotSupportedException($"El TipoCargaId '{tipoCargaId}' no existe en el catálogo de reglas de negocio.");
				}
			}

			// En el futuro, si soportan Excel, agregaríamos: if (ext == ".xlsx") { ... }

			throw new NotSupportedException($"La extensión '{ext}' no está soportada actualmente por el sistema.");
		}
	}
}