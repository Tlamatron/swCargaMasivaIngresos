namespace swCargaMasivaIngresos.Models
{
	/// <summary>
	/// Datos enviados por el Front-end para intentar iniciar sesión.
	/// </summary>
	public class LoginRequest
	{
		public string Usuario { get; set; }
		public string Password { get; set; }
	}

	/// <summary>
	/// Perfil del usuario que la API le devuelve al Front-end si las credenciales son correctas.
	/// </summary>
	public class UsuarioResponse
	{
		public string UsuarioLogin { get; set; }
		public string NombreCompleto { get; set; }
		public string CorreoElectronico { get; set; }
		public int OficinaId { get; set; }
		public string NombreOficina { get; set; }
	}
}