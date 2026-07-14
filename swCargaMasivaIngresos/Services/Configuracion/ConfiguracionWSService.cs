using swCargaMasivaIngresos.Models;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Web;

namespace swCargaMasivaIngresos.Services
{
	/// <summary>
	/// Este servicio se conectará a tu tabla Catalogo.ConfiguracionWSOficinas utilizando tu clase centralizadora de conexiones.
	/// </summary>
	public static class ConfiguracionWSService
	{
		public static ConfiguracionWSDTO ObtenerConfiguracion(int oficinaId)
		{
			ConfiguracionWSDTO config = null;

			// Usamos tu conector dinámico (Apunta a Local, Test o Prod automáticamente)
			string cadenaConexion = ConfiguracionApp.ObtenerCadenaConexion();

			// Usamos un esquema dinámico por si estás en Local (dbo) o Nube (pred_)
			// NOTA: Para este ejemplo en tu Localhost usaremos la sintaxis directa a Catalogo
			using (SqlConnection conn = new SqlConnection(cadenaConexion))
			using (SqlCommand cmd = new SqlCommand("SELECT EndpointUrl, TipoAutenticacion, Credencial1, Credencial2 FROM Catalogo.ConfiguracionWSOficinas WHERE OficinaId = @OficinaId AND Activo = 1", conn))
			{
				cmd.Parameters.AddWithValue("@OficinaId", oficinaId);
				conn.Open();

				using (SqlDataReader reader = cmd.ExecuteReader())
				{
					if (reader.Read())
					{
						config = new ConfiguracionWSDTO
						{
							OficinaId = oficinaId,
							EndpointUrl = reader["EndpointUrl"].ToString(),
							TipoAutenticacion = Convert.ToByte(reader["TipoAutenticacion"]),
							Credencial1 = reader["Credencial1"]?.ToString(),
							Credencial2 = reader["Credencial2"]?.ToString()
						};
					}
				}
			}

			return config;
		}
	}
}