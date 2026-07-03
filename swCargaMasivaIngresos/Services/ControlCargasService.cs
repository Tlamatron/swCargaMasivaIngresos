using System;
using System.Data;
using System.Data.SqlClient;

namespace swCargaMasivaIngresos.Services
{
	public static class ControlCargasService
	{
		private static readonly string CadenaConexion = ConfiguracionApp.ObtenerCadenaConexion();

		// =========================================================================
		// NUEVO MÉTODO: Generar el Folio usando el SP con parámetro OUTPUT
		// =========================================================================
		public static int GenerarFolio(int idTipoFolio)
		{
			using (SqlConnection conn = new SqlConnection(CadenaConexion))
			using (SqlCommand cmd = new SqlCommand("pred_Operacion.sp_GenerarFolio", conn))
			{
				cmd.CommandType = CommandType.StoredProcedure;

				// 1. Enviamos el parámetro de entrada
				cmd.Parameters.AddWithValue("@IdTipoFolio", idTipoFolio);

				// 2. Configuramos el parámetro de SALIDA (OUTPUT)
				SqlParameter paramNuevoFolio = new SqlParameter("@NuevoFolio", SqlDbType.Int)
				{
					Direction = ParameterDirection.Output
				};
				cmd.Parameters.Add(paramNuevoFolio);

				// 3. Ejecutamos el Procedimiento Almacenado
				conn.Open();
				cmd.ExecuteNonQuery();

				// 4. Recuperamos el valor que la base de datos nos devolvió
				return Convert.ToInt32(paramNuevoFolio.Value);
			}
		}

		public static void RegistrarInicio_v01(int folioCarga, int oficinaId, string usuarioLogin, int tipoCargaId)
		{
			using (SqlConnection conn = new SqlConnection(CadenaConexion))
			using (SqlCommand cmd = new SqlCommand("pred_Operacion.sp_RegistrarInicioCarga", conn))
			{
				cmd.CommandType = CommandType.StoredProcedure;
				cmd.Parameters.AddWithValue("@FolioCarga", folioCarga);
				cmd.Parameters.AddWithValue("@OficinaId", oficinaId);
				cmd.Parameters.AddWithValue("@UsuarioLogin", usuarioLogin);
				cmd.Parameters.AddWithValue("@TipoCargaId", tipoCargaId);

				conn.Open();
				cmd.ExecuteNonQuery();
			}
		}
		public static void RegistrarInicio(int folioCarga, int oficinaId, string usuarioLogin, int tipoCargaId, int claveMunicipioDestino = 0)
		{
			using (SqlConnection conn = new SqlConnection(CadenaConexion))
			using (SqlCommand cmd = new SqlCommand("pred_Operacion.sp_RegistrarInicioCarga", conn))
			{
				cmd.CommandType = CommandType.StoredProcedure;
				cmd.Parameters.AddWithValue("@FolioCarga", folioCarga);
				cmd.Parameters.AddWithValue("@OficinaId", oficinaId);
				cmd.Parameters.AddWithValue("@UsuarioLogin", usuarioLogin);
				cmd.Parameters.AddWithValue("@TipoCargaId", tipoCargaId);
				cmd.Parameters.AddWithValue("@ClaveMunicipioDestino", claveMunicipioDestino > 0 ? (object)claveMunicipioDestino : DBNull.Value);

				conn.Open();
				cmd.ExecuteNonQuery();
			}
		}

		public static void ActualizarEstatus(int folioCarga, string estatus, int totalExitosos = 0, int totalFallidos = 0, string mensajeDetalle = null)
		{
			using (SqlConnection conn = new SqlConnection(CadenaConexion))
			using (SqlCommand cmd = new SqlCommand("pred_Operacion.sp_ActualizarEstatusCarga", conn))
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