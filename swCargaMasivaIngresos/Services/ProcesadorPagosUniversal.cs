using swCargaMasivaIngresos.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.IO;

namespace swCargaMasivaIngresos.Services
{
	public class ProcesadorPagosUniversal : IProcesadorFormato
	{
		private readonly string AppName = System.Configuration.ConfigurationManager.AppSettings["NombAplicacion"] ?? "APICargaMasivaIngresos";
		private readonly string CadenaConexion = ConfiguracionApp.ObtenerCadenaConexion();

		public ResultadoProceso Procesar(string rutaArchivo, ParametrosCarga param)
		{
			var resultadoFinal = new ResultadoProceso { ErroresDetalle = new List<string>() };
			string extension = Path.GetExtension(rutaArchivo);

			LogService.WriteLogAsync("INFO", param.UsuarioLogin, "ProcesadorPagosUniversal", $"Inicia lectura inteligente de Pagos. Folio: {param.FolioCarga}. Extensión: {extension}");

			// =========================================================================
			// FASE 1: LECTURA CRUDA (Lee CSV, TXT o Excel)
			// =========================================================================
			var lectura = LectorUniversal.LeerArchivo(rutaArchivo, extension);
			if (lectura.ErroresEstructurales.Count > 0)
			{
				resultadoFinal.RegistrosFallidos = 1;
				resultadoFinal.ErroresDetalle.AddRange(lectura.ErroresEstructurales);
				return resultadoFinal;
			}

			// =========================================================================
			// FASE 2: MAPEO INTELIGENTE (Busca columnas sin importar el orden)
			// =========================================================================
			DataTable tablaMapeada = MapeadorInteligente.EstandarizarTabla(lectura.TablaCruda, out List<string> erroresMapeo);
			if (erroresMapeo.Count > 0)
			{
				resultadoFinal.RegistrosFallidos = 1;
				resultadoFinal.ErroresDetalle.AddRange(erroresMapeo);
				return resultadoFinal;
			}

			// =========================================================================
			// FASE 3: LIMPIEZA Y AUTOCORRECCIÓN (Aduana de Reglas de Negocio)
			// =========================================================================
			var limpieza = LimpiadorDatos.LimpiarYValidar(tablaMapeada, lectura.ContextoPestaña, param);

			// =========================================================================
			// FASE 4: PREPARACIÓN PARA SQL (Reducir a las 5 columnas de Staging)
			// =========================================================================
			DataTable tablaStaging = CrearEstructuraPagos();
			HashSet<string> pagosProcesados = new HashSet<string>();

			foreach (DataRow filaValida in limpieza.TablaValidos.Rows)
			{
				string claveMun = filaValida["ClaveMunicipio"].ToString();
				string tipoPre = filaValida["TipoPredio"].ToString();
				string cuenta = filaValida["CuentaPredial"].ToString();
				string bimestre = filaValida["Bimestre"].ToString();

				// Evitar procesar el mismo pago dos veces en el mismo archivo
				string llaveUnica = $"{claveMun}-{tipoPre}-{cuenta}-{bimestre}";
				if (pagosProcesados.Contains(llaveUnica)) continue;
				pagosProcesados.Add(llaveUnica);

				// Pasamos solo los datos que requiere Staging_Etiquetado
				tablaStaging.Rows.Add(claveMun, tipoPre, cuenta, param.FolioCarga, bimestre);
			}

			// =========================================================================
			// FASE 5: INSERCIÓN MASIVA
			// =========================================================================
			if (tablaStaging.Rows.Count > 0)
			{
				InsertarLoteEnBD(tablaStaging, param);
			}

			// Consolidamos los resultados para el correo/log
			resultadoFinal.RegistrosExitosos = tablaStaging.Rows.Count;
			resultadoFinal.RegistrosFallidos = limpieza.TablaRechazados.Rows.Count;
			resultadoFinal.ErroresDetalle.AddRange(limpieza.DetallesErrores);

			LogService.WriteLogAsync("INFO", param.UsuarioLogin, "ProcesadorPagosUniversal",
				$"Fin. Éxitos: {resultadoFinal.RegistrosExitosos}, Errores: {resultadoFinal.RegistrosFallidos}, Ignorados (Viejos): {limpieza.RegistrosIgnorados}");
			resultadoFinal.TablaRechazados = limpieza.TablaRechazados;
			return resultadoFinal;
		}

		private DataTable CrearEstructuraPagos()
		{
			var tabla = new DataTable();
			tabla.Columns.Add("ClaveMunicipio", typeof(string));
			tabla.Columns.Add("TipoPredio", typeof(string));
			tabla.Columns.Add("CuentaPredial", typeof(string));
			tabla.Columns.Add("FolioCarga", typeof(int));
			tabla.Columns.Add("Bimestre", typeof(string));
			return tabla;
		}

		private void InsertarLoteEnBD(DataTable lote, ParametrosCarga param)
		{
			using (SqlConnection conn = new SqlConnection(CadenaConexion))
			{
				conn.Open();

				using (SqlBulkCopy bulkCopy = new SqlBulkCopy(conn))
				{
					bulkCopy.DestinationTableName = "pred_Operacion.Staging_Etiquetado";
					bulkCopy.BulkCopyTimeout = 120;
					bulkCopy.WriteToServer(lote);
				}

				// Llamamos al SP que marca las cuentas como pagadas
				using (SqlCommand cmd = new SqlCommand("pred_Operacion.sp_ProcesarMergeEtiquetado", conn))
				{
					cmd.CommandType = CommandType.StoredProcedure;
					cmd.CommandTimeout = 180;
					cmd.Parameters.AddWithValue("@FolioCarga", param.FolioCarga);
					cmd.ExecuteNonQuery();
				}
			}
		}
	}
}