using System;
using System.Data;
using System.Data.SqlClient;

namespace swCargaMasivaIngresos.Services
{
	/// <summary>
	/// Clase de servicio para el control de cargas masivas. Esta clase proporciona métodos para generar folios, registrar inicios de carga y actualizar estatus de cargas en la base de datos mediante procedimientos almacenados.
	/// </summary>
	public static class ControlCargasService
	{
		private static readonly string CadenaConexion = ConfiguracionApp.ObtenerCadenaConexion();

		/// <summary>
		/// Genera un nuevo folio para una carga masiva en la base de datos. Este método llama al procedimiento almacenado 'sp_GenerarFolio' y devuelve el folio generado como un entero.
		/// </summary>
		/// <param name="idTipoFolio"></param>
		/// <returns></returns>
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

		/// <summary>
		/// Registra el inicio de una carga masiva en la base de datos. Este método llama al procedimiento almacenado 'sp_RegistrarInicioCarga' y permite opcionalmente especificar un municipio de destino.
		/// </summary>
		/// <param name="folioCarga"></param>
		/// <param name="oficinaId"></param>
		/// <param name="usuarioLogin"></param>
		/// <param name="tipoCargaId"></param>
		/// <param name="claveMunicipioDestino"></param>
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

		/// <summary>
		/// Actualiza el estatus de una carga masiva en la base de datos. Este método llama al procedimiento almacenado 'sp_ActualizarEstatusCarga' y permite especificar el número total de registros exitosos y fallidos, así como un mensaje de detalle opcional.
		/// </summary>
		/// <param name="folioCarga"></param>
		/// <param name="estatus"></param>
		/// <param name="totalExitosos"></param>
		/// <param name="totalFallidos"></param>
		/// <param name="mensajeDetalle"></param>
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