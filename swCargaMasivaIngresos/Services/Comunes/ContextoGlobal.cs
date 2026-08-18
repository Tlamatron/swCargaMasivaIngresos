using System;
using System.Web;

namespace swCargaMasivaIngresos.Services.Comunes
{
	public static class ContextoGlobal
	{
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