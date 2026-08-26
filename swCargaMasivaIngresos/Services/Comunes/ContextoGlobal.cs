using System;
using System.Web;

namespace swCargaMasivaIngresos.Services.Comunes
{
	/// <summary>
	/// Clase estática que proporciona acceso a información global del contexto de la aplicación, como el usuario actual y el rol actual. Esta información se obtiene principalmente del contexto HTTP si la llamada proviene de una solicitud web, o devuelve valores por defecto si no hay contexto disponible (por ejemplo, en trabajos en segundo plano).
	/// </summary>
	public static class ContextoGlobal
	{
		/// <summary>
		/// Obtiene el nombre del usuario actual desde el contexto HTTP si está disponible, o devuelve "Sistema" como valor por defecto si no hay contexto (por ejemplo, en trabajos en segundo plano). Esta propiedad es útil para registrar acciones y eventos con el usuario que los realizó.
		/// </summary>
		public static string UsuarioActual
		{
			get
			{
				// Si la llamada viene de la web, sacamos el dato de la petición
				if (HttpContext.Current != null && HttpContext.Current.Items.Contains("MS_HttpRequestMessage"))
				{
					var request = (System.Net.Http.HttpRequestMessage)HttpContext.Current.Items["MS_HttpRequestMessage"];
					if (request.Properties.ContainsKey("UsuarioActual"))
					{
						return request.Properties["UsuarioActual"].ToString();
					}
				}
				return "Sistema"; // Valor por defecto si no hay petición (ej. Background Jobs de Hangfire)
			}
		}

		/// <summary>
		/// Obtiene el ID del rol actual desde el contexto HTTP si está disponible, o devuelve 0 como valor por defecto si no hay contexto (por ejemplo, en trabajos en segundo plano). Esta propiedad es útil para determinar los permisos y accesos del usuario que realiza la acción.
		/// </summary>
		public static int RolIdActual
		{
			get
			{
				if (HttpContext.Current != null && HttpContext.Current.Items.Contains("MS_HttpRequestMessage"))
				{
					var request = (System.Net.Http.HttpRequestMessage)HttpContext.Current.Items["MS_HttpRequestMessage"];
					if (request.Properties.ContainsKey("RolIdActual"))
					{
						return Convert.ToInt32(request.Properties["RolIdActual"]);
					}
				}
				return 0;
			}
		}

		// Puedes agregar más propiedades como OficinaIdActual, etc.
	}
}