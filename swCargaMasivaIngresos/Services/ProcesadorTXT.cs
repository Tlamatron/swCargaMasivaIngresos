using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Text;

namespace swCargaMasivaIngresos.Services
{
	public class ProcesadorTXT : IProcesadorFormato
	{
		// Traemos la cadena de conexión desde tu Web.config
		private readonly string CadenaConexion = System.Configuration.ConfigurationManager.ConnectionStrings["ConexionSQL"].ConnectionString;

		public ResultadoProceso Procesar(string rutaArchivo, ParametrosCarga parametros)
		{
			var resultado = new ResultadoProceso { /*...*/ };
			int tamanoLote = 10000;
			DataTable tablaLote = CrearEstructuraDataTable();

			// 🔥 NUEVO: HashSet para detectar duplicados en el mismo archivo ultra rápido
			HashSet<string> cuentasProcesadas = new HashSet<string>();

			using (var reader = new StreamReader(rutaArchivo, Encoding.UTF8))
			{
				string linea;
				int numeroLinea = 0;

				while ((linea = reader.ReadLine()) != null)
				{
					numeroLinea++;
					// ... (Tus validaciones de layout que ya hicimos) ...

					string cuenta = columnas[0].Trim();

					// 🔥 REGLA DE NEGOCIO: Si la cuenta ya existe en este mismo archivo, la marcamos como repetida
					if (cuentasProcesadas.Contains(cuenta))
					{
						resultado.RegistrosFallidos++;
						resultado.ErroresDetalle.Add($"Línea {numeroLinea}: La cuenta {cuenta} viene duplicada en el archivo. Se omitió.");
						continue; // Saltamos a la siguiente línea
					}

					// Agregamos a la memoria RAM para que no se nos pase si vuelve a aparecer abajo
					cuentasProcesadas.Add(cuenta);

					// ... (Validaciones de Fecha, Monto, etc.) ...

					tablaLote.Rows.Add(cuenta, fechaValida, montoValido, idLargoValido, observaciones, parametros.OficinaId, parametros.FolioCarga);
					resultado.RegistrosExitosos++;

					// Insertar Lote
					if (tablaLote.Rows.Count >= tamanoLote)
					{
						InsertarLoteEnBD(tablaLote, parametros.FolioCarga, parametros.OficinaId);
						tablaLote.Clear();
					}
				}

				// Insertar remanente
				if (tablaLote.Rows.Count > 0)
				{
					InsertarLoteEnBD(tablaLote, parametros.FolioCarga, parametros.OficinaId);
				}
			}
			return resultado;
		}

		private void MarcarError(ResultadoProceso res, int linea, string mensaje)
		{
			res.RegistrosFallidos++;
			res.ErroresDetalle.Add($"Línea {linea}: {mensaje}");
		}

		private DataTable CrearEstructuraDataTable()
		{
			DataTable tabla = new DataTable();
			tabla.Columns.Add("Cuenta20", typeof(string));
			tabla.Columns.Add("FechaOperacion", typeof(DateTime));
			tabla.Columns.Add("Monto", typeof(decimal));
			tabla.Columns.Add("ReferenciaBigInt", typeof(long));
			tabla.Columns.Add("Observaciones", typeof(string));
			// Agregamos datos del parámetro general para ligarlos al registro
			tabla.Columns.Add("OficinaId", typeof(int));
			tabla.Columns.Add("FolioCarga", typeof(string));
			return tabla;
		}

		private void InsertarLoteEnBD(DataTable lote, string folioCarga, int oficinaId)
		{
			using (SqlConnection conn = new SqlConnection(CadenaConexion))
			{
				conn.Open();

				// PASO 1: Inyectar masivamente a la tabla Staging
				using (SqlBulkCopy bulkCopy = new SqlBulkCopy(conn))
				{
					bulkCopy.DestinationTableName = "dbo.Staging_Predial";
					bulkCopy.BatchSize = 10000;
					// Configurar timeout alto por si la base de datos está ocupada
					bulkCopy.BulkCopyTimeout = 120;

					// bulkCopy.ColumnMappings.Add(...); // Si fuera necesario
					bulkCopy.WriteToServer(lote);
				}

				// PASO 2: Realizar el Upsert (MERGE) de Staging a Destino
				string queryMerge = @"
            -- Sincronizamos las tablas basándonos en la Cuenta y el Municipio
            MERGE dbo.TablaIngresosDestino AS Destino
            USING (SELECT * FROM dbo.Staging_Predial WHERE FolioCarga = @Folio) AS Origen
            ON (Destino.CuentaBancaria = Origen.CuentaBancaria AND Destino.OficinaId = @OficinaId)
            
            -- Si la cuenta ya existía, ACTUALIZAMOS los datos (Actualización de deuda)
            WHEN MATCHED THEN
                UPDATE SET 
                    Destino.Monto = Origen.Monto,
                    Destino.FechaOperacion = Origen.FechaOperacion,
                    Destino.FolioCarga = Origen.FolioCarga,
                    Destino.Observaciones = Origen.Observaciones
            
            -- Si la cuenta es nueva, la INSERTAMOS
            WHEN NOT MATCHED THEN
                INSERT (CuentaBancaria, FechaOperacion, Monto, ReferenciaBigInt, Observaciones, OficinaId, FolioCarga)
                VALUES (Origen.CuentaBancaria, Origen.FechaOperacion, Origen.Monto, Origen.ReferenciaBigInt, Origen.Observaciones, Origen.OficinaId, Origen.FolioCarga);

            -- PASO 3: Limpiamos la tabla Staging para no dejar basura
            DELETE FROM dbo.Staging_Predial WHERE FolioCarga = @Folio;
        ";

				using (SqlCommand cmd = new SqlCommand(queryMerge, conn))
				{
					cmd.CommandTimeout = 180; // 3 minutos máximo para el Merge
					cmd.Parameters.AddWithValue("@Folio", folioCarga);
					cmd.Parameters.AddWithValue("@OficinaId", oficinaId);

					// Ejecutamos el motor de base de datos
					cmd.ExecuteNonQuery();
				}
			}
		}
	}
}