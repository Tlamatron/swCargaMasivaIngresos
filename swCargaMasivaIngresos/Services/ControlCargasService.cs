using System;
using System.Data;
using System.Data.SqlClient;

namespace swCargaMasivaIngresos.Services
{
	public static class ControlCargasService
	{
		private static readonly string CadenaConexion = System.Configuration.ConfigurationManager.ConnectionStrings["ConexionSQL"].ConnectionString;

		// 🚀 1. Agregamos el parámetro tipoCargaId en la firma del método
		public static void RegistrarInicio(string folioCarga, int oficinaId, string usuarioLogin, int tipoCargaId)
		{
			using (SqlConnection conn = new SqlConnection(CadenaConexion))
			using (SqlCommand cmd = new SqlCommand("dbo.sp_RegistrarInicioCarga", conn))
			{
				cmd.CommandType = CommandType.StoredProcedure;
				cmd.Parameters.AddWithValue("@FolioCarga", folioCarga);
				cmd.Parameters.AddWithValue("@OficinaId", oficinaId);
				cmd.Parameters.AddWithValue("@UsuarioLogin", usuarioLogin);

				// 🚀 2. Lo enviamos a la Base de Datos
				cmd.Parameters.AddWithValue("@TipoCargaId", tipoCargaId);

				conn.Open();
				cmd.ExecuteNonQuery();
			}
		}

		public static void ActualizarEstatus(string folioCarga, string estatus, int totalExitosos = 0, int totalFallidos = 0, string mensajeDetalle = null)
		{
			using (SqlConnection conn = new SqlConnection(CadenaConexion))
			using (SqlCommand cmd = new SqlCommand("dbo.sp_ActualizarEstatusCarga", conn))
			{
				cmd.CommandType = CommandType.StoredProcedure;
				cmd.Parameters.AddWithValue("@FolioCarga", folioCarga);
				cmd.Parameters.AddWithValue("@Estatus", estatus);
				cmd.Parameters.AddWithValue("@TotalExitosos", totalExitosos);
				cmd.Parameters.AddWithValue("@TotalFallidos", totalFallidos);
				cmd.Parameters.AddWithValue("@MensajeDetalle", (object)mensajeDetalle ?? DBNull.Value);

				conn.Open();
				cmd.ExecuteNonQuery();
			}
		}
	
	}
}