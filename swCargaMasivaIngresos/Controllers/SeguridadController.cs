using swCargaMasivaIngresos.Models;
using swCargaMasivaIngresos.Services;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Web.Http;

namespace swCargaMasivaIngresos.Controllers
{
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
		public IHttpActionResult ValidarLogin([FromBody] LoginRequest request)
		{
			if (request == null || string.IsNullOrWhiteSpace(request.Usuario) || string.IsNullOrWhiteSpace(request.Password))
			{
				return BadRequest("El usuario y la contraseña son obligatorios.");
			}

			try
			{
				using (SqlConnection conn = new SqlConnection(CadenaConexion))
				using (SqlCommand cmd = new SqlCommand("pred_Seguridad.sp_ValidarUsuario", conn))
				{
					cmd.CommandType = CommandType.StoredProcedure;
					cmd.Parameters.AddWithValue("@UsuarioLogin", request.Usuario.Trim());
					cmd.Parameters.AddWithValue("@Password", request.Password.Trim()); // Nota: En producción esto debería venir encriptado

					conn.Open();

					using (SqlDataReader reader = cmd.ExecuteReader())
					{
						if (reader.Read())
						{
							// Si el SP regresa datos, el login fue exitoso
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
							// Si el SP no regresa nada, credenciales inválidas o usuario inactivo
							return Unauthorized();
						}
					}
				}
			}
			catch (Exception ex)
			{
				// Registramos el error internamente y devolvemos 500
				Services.LogService.WriteLogAsync("APICargaMasivaIngresos", "ERROR", request.Usuario, "SeguridadController", $"Fallo en Login: {ex.Message}").Wait();
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
		public IHttpActionResult ObtenerMenu(int appId, int rolId)
		{
			try
			{
				List<MenuDTO> listaMenus = new List<MenuDTO>();

				using (SqlConnection conn = new SqlConnection(CadenaConexion))
				using (SqlCommand cmd = new SqlCommand("pred_Seguridad.sp_ObtenerMenuDinamico", conn))
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
	}
}