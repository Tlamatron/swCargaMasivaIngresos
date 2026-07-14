using System.IO;

namespace swCargaMasivaIngresos.Services
{
	/// <summary>
	/// Servicio especializado en la manipulación física de archivos en el disco duro del servidor.
	/// </summary>
	public static class ServicioEnsamblador
	{
		/// <summary>
		/// Une los fragmentos temporales (.tmp) previamente cargados en un solo archivo físico final.
		/// </summary>
		/// <param name="folioCarga">Identificador único de la carga.</param>
		/// <param name="nombreArchivoOriginal">Nombre original enviado por el cliente para extraer la extensión.</param>
		/// <param name="totalChunks">Número total de fragmentos esperados.</param>
		/// <param name="rutaDirectorio">Ruta física en el servidor donde se alojan los fragmentos.</param>
		/// <returns>La ruta completa y absoluta del nuevo archivo ensamblado.</returns>
		public static string UnirFragmentos(int folioCarga, string nombreArchivoOriginal, int totalChunks, string rutaDirectorio)
		{
			// Rescatar si era .txt, .xlsx o .csv
			string extension = Path.GetExtension(nombreArchivoOriginal);

			// Archivo final: Ej. "C:\Cargas\Temporales\8c9f5...b2a.txt"
			string rutaArchivoCompleto = Path.Combine(rutaDirectorio, $"{folioCarga}{extension}");

			// Abrimos el Stream de destino con un buffer optimizado de 4096 bytes (4KB)
			using (var streamDestino = new FileStream(rutaArchivoCompleto, FileMode.Create, FileAccess.Write, FileShare.None, 4096, true))
			{
				for (int i = 1; i <= totalChunks; i++)
				{
					string rutaChunk = Path.Combine(rutaDirectorio, $"{folioCarga}_chunk_{i}.tmp");

					if (!File.Exists(rutaChunk))
					{
						throw new FileNotFoundException($"Corrupción de datos: No se encontró el fragmento {i} para el folio {folioCarga}.");
					}

					// Leemos el fragmento y lo volcamos directamente en el archivo final
					using (var streamChunk = new FileStream(rutaChunk, FileMode.Open, FileAccess.Read))
					{
						streamChunk.CopyTo(streamDestino);
					}

					// 🧹 BUENA PRÁCTICA: Eliminar el fragmento inmediatamente para no dejar basura en el disco duro.
					File.Delete(rutaChunk);
				}
			}

			return rutaArchivoCompleto;
		}
	}
}