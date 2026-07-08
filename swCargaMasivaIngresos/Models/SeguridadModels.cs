namespace swCargaMasivaIngresos.Models
{
	/// <summary>
	/// Datos enviados por el Front-end para intentar iniciar sesión.
	/// </summary>
	public class LoginRequest
	{
		/// <summary>
		/// Usuario que intenta iniciar sesión. Puede ser un nombre de usuario, correo electrónico o cualquier identificador único definido por la aplicación.
		/// </summary>
		public string Usuario { get; set; }

		/// <summary>
		/// Contraseña asociada al usuario que intenta iniciar sesión. Debe ser manejada de manera segura y nunca almacenada en texto plano en la base de datos.
		/// </summary>
		public string Password { get; set; }
	}

	/// <summary>
	/// Perfil del usuario que la API le devuelve al Front-end si las credenciales son correctas.
	/// </summary>
	public class UsuarioResponse
	{
		/// <summary>
		/// Usuario que ha iniciado sesión correctamente. Este valor puede ser utilizado por el Front-end para mostrar información personalizada o para realizar futuras solicitudes autenticadas.
		/// </summary>
		public string UsuarioLogin { get; set; }

		/// <summary>
		/// Nombre completo del usuario que ha iniciado sesión. Este valor puede ser utilizado por el Front-end para mostrar información personalizada o para realizar futuras solicitudes autenticadas.
		/// </summary>
		public string NombreCompleto { get; set; }

		/// <summary>
		/// Correo electrónico del usuario que ha iniciado sesión. Este valor puede ser utilizado por el Front-end para mostrar información personalizada o para realizar futuras solicitudes autenticadas.
		/// </summary>
		public string CorreoElectronico { get; set; }

		/// <summary>
		/// Oficina a la que pertenece el usuario que ha iniciado sesión. Este valor puede ser utilizado por el Front-end para mostrar información personalizada o para realizar futuras solicitudes autenticadas.
		/// </summary>
		public int OficinaId { get; set; }

		/// <summary>
		/// Muestra el nombre de la oficina a la que pertenece el usuario que ha iniciado sesión. Este valor puede ser utilizado por el Front-end para mostrar información personalizada o para realizar futuras solicitudes autenticadas.
		/// </summary>
		public string NombreOficina { get; set; }

		/// <summary>
		/// Rol del usuario que ha iniciado sesión. Este valor puede ser utilizado por el Front-end para mostrar información personalizada o para realizar futuras solicitudes autenticadas.
		/// </summary>
		public int RolId { get; set; }

		/// <summary>
		/// Muestra el municipio al que pertenece el usuario que ha iniciado sesión. Este valor puede ser utilizado por el Front-end para mostrar información personalizada o para realizar futuras solicitudes autenticadas.
		/// </summary>
		public int ClaveMunicipio { get; set; }
	}
}