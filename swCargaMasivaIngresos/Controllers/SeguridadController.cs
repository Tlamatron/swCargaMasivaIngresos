using Microsoft.IdentityModel.Tokens;
using swCargaMasivaIngresos.Models;
using swCargaMasivaIngresos.Services;
using swCargaMasivaIngresos.Services.Comunes;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Web.Http;

namespace swCargaMasivaIngresos.Controllers
{
	/// <summary>
	/// Controlador API encargado de la seguridad y autenticación de usuarios. Expone endpoints para validar credenciales de login y obtener el menú dinámico basado en el rol del usuario. Utiliza procedimientos almacenados en la base de datos para realizar las validaciones y obtener la información necesaria.
	/// </summary>
	[RoutePrefix("api/Seguridad")]
	public class SeguridadController : ApiController
	{
		private readonly string CadenaConexion = ConfiguracionApp.ObtenerCadenaConexion();

		/// <summary>
		/// Valida las credenciales de un usuario contra la base de datos.
		/// </summary>
		/// <param name="request">Objeto con el usuario y contraseña.</param>
		/// <returns>El perfil del usuario si es exitoso, o un error 401 si es incorrecto.</returns>
		[HttpPost]
		[Route("Login")]
		public async Task<IHttpActionResult> ValidarLoginAsync([FromBody] LoginRequest request)
		{
			if (request == null || string.IsNullOrWhiteSpace(request.Usuario) || string.IsNullOrWhiteSpace(request.Password))
			{
				return BadRequest("El usuario y la contraseña son obligatorios.");
			}

			try
			{
				// 🚀 SE ELIMINÓ LA VALIDACIÓN DE SEGURIDAD AQUÍ
				// Vamos directo a validar las credenciales contra la base de datos

				using (SqlConnection conn = new SqlConnection(CadenaConexion))
				using (SqlCommand cmd = new SqlCommand("pred.sp_ValidarUsuario", conn))
				{
					cmd.CommandType = CommandType.StoredProcedure;
					cmd.Parameters.AddWithValue("@UsuarioLogin", request.Usuario.Trim());
					cmd.Parameters.AddWithValue("@Password", request.Password.Trim());

					conn.Open();

					using (SqlDataReader reader = cmd.ExecuteReader())
					{
						if (reader.Read())
						{
							var perfil = new UsuarioResponse
							{
								UsuarioLogin = reader["UsuarioLogin"].ToString(),
								NombreCompleto = reader["NombreCompleto"].ToString(),
								CorreoElectronico = reader["CorreoElectronico"].ToString(),
								OficinaId = Convert.ToInt32(reader["OficinaId"]),
								NombreOficina = reader["NombreOficina"].ToString(),
								RolId = Convert.ToInt32(reader["RolId"]),
								ClaveMunicipio = reader["ClaveMunicipio"] != DBNull.Value ? Convert.ToInt32(reader["ClaveMunicipio"]) : 0
							};
							return Ok(perfil);
						}
						else
						{
							return Unauthorized();
						}
					}
				}
			}
			catch (Exception ex)
			{
				Services.LogService.WriteLogAsync("ERROR", request.Usuario, "SeguridadController", $"Fallo en Login: {ex.Message}").Wait();
				return InternalServerError(ex);
			}
		}

		/// <summary>
		/// Obtiene el menú dinámico para un usuario basado en su rol y la aplicación a la que accede.
		/// </summary>
		/// <param name="appId">Identificador de la aplicación.</param>
		/// <param name="rolId">Identificador del rol del usuario.</param>
		/// <returns>Lista de objetos MenuDTO que representan el menú dinámico.</returns>
		[HttpGet]
		[Route("Menu")]
		public async Task<IHttpActionResult> ObtenerMenuAsync(int appId, int rolId)
		{
			try
			{
				List<MenuDTO> listaMenus = new List<MenuDTO>();

				using (SqlConnection conn = new SqlConnection(CadenaConexion))
				using (SqlCommand cmd = new SqlCommand("pred.sp_ObtenerMenuDinamico", conn))
				{
					cmd.CommandType = CommandType.StoredProcedure;
					cmd.Parameters.AddWithValue("@AppId", appId);
					cmd.Parameters.AddWithValue("@RolId", rolId);

					conn.Open();

					using (SqlDataReader reader = cmd.ExecuteReader())
					{
						while (reader.Read())
						{
							listaMenus.Add(new MenuDTO
							{
								MenuId = Convert.ToInt32(reader["MenuId"]),
								MenuPadreId = reader["MenuPadreId"] != DBNull.Value ? (int?)Convert.ToInt32(reader["MenuPadreId"]) : null,
								Nombre = reader["Nombre"].ToString(),
								RutaUrl = reader["RutaUrl"].ToString(),
								Icono = reader["Icono"]?.ToString(),
								Orden = Convert.ToInt32(reader["Orden"])
							});
						}
					}
				}

				return Ok(listaMenus);
			}
			catch (Exception ex)
			{
				return InternalServerError(ex);
			}
		}


		/// <summary>
		/// 🛡️ Valida matemáticamente que un Token SSO no haya sido alterado por un hacker
		/// y que provenga de una aplicación permitida en el Web.config.
		/// </summary>
		[HttpPost]
		[Route("ValidarTokenSSO")]
		[AllowAnonymous]
		public IHttpActionResult ValidarTokenSSO([FromBody] PeticionToken request)
		{
			if (request == null || string.IsNullOrWhiteSpace(request.Token))
				return BadRequest("Token vacío.");

			// 1. Leemos las reglas de seguridad del Web.config
			string secretKey = ConfigurationManager.AppSettings["SsoSharedSecret"];
			string aplicacionPermitida = ConfigurationManager.AppSettings["SsoAppOrigenPermitida"];

			try
			{
				var tokenHandler = new JwtSecurityTokenHandler();
				var key = Encoding.ASCII.GetBytes(secretKey);

				// 2. Ejecutamos la validación criptográfica estricta
				tokenHandler.ValidateToken(request.Token, new TokenValidationParameters
				{
					ValidateIssuerSigningKey = true,
					IssuerSigningKey = new SymmetricSecurityKey(key),

					// 🚀 Validamos que el nombre de la App origen sea el correcto
					ValidateIssuer = true,
					ValidIssuer = aplicacionPermitida,

					ValidateAudience = false, // Puedes encenderlo si también validas para quién es el token

					// Validamos que no esté caducado
					ValidateLifetime = true,
					ClockSkew = TimeSpan.Zero
				}, out SecurityToken validatedToken);

				// 3. Si llega a esta línea, el token es 100% auténtico y seguro.
				var jwtToken = (JwtSecurityToken)validatedToken;

				// Devolvemos el Payload (el cerebro del token) ya validado al Front-End
				return Ok(new
				{
					Exito = true,
					Payload = jwtToken.Payload
				});
			}
			catch (SecurityTokenExpiredException)
			{
				return Unauthorized(); // "El token ha caducado."
			}
			catch (Exception ex)
			{
				// Si la firma matemática no coincide o la AppOrigen no es la permitida
				Services.LogService.WriteLogAsync("WARN", "SSO", "SeguridadController", $"Intento de intrusión o token inválido: {ex.Message}").Wait();
				return Unauthorized();
			}
		}
	}

	/// <summary>
	/// Clase que representa la petición para validar un token SSO. Contiene la propiedad Token que se enviará desde el cliente para su validación en el servidor.
	/// </summary>
	public class PeticionToken
	{
		/// <summary>
		/// El token SSO que se desea validar. Este token debe ser enviado desde el cliente y será verificado en el servidor para asegurar su autenticidad y origen.
		/// </summary>
		public string Token { get; set; }
	}
}