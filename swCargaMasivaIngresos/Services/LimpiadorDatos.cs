using swCargaMasivaIngresos.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;

namespace swCargaMasivaIngresos.Services
{
	public class ResultadoLimpieza
	{
		public DataTable TablaValidos { get; set; }
		public DataTable TablaRechazados { get; set; }
		public List<string> DetallesErrores { get; set; } = new List<string>();
		public int RegistrosIgnorados { get; set; } // Para los pagos históricos que simplemente saltamos
	}

	public static class LimpiadorDatos
	{
		public static ResultadoLimpieza LimpiarYValidar(DataTable tablaMapeada, string contextoPestaña, ParametrosCarga param)
		{
			var resultado = new ResultadoLimpieza
			{
				TablaValidos = tablaMapeada.Clone(),
				TablaRechazados = tablaMapeada.Clone()
			};

			// Agregamos una columna extra a los rechazados para saber por qué fallaron
			resultado.TablaRechazados.Columns.Add("MotivoRechazo", typeof(string));

			int numeroFila = 0;

			foreach (DataRow fila in tablaMapeada.Rows)
			{
				numeroFila++;
				List<string> erroresFila = new List<string>();

				// 1. Extraemos los valores crudos
				string claveMun = fila["ClaveMunicipio"]?.ToString().Trim();
				string tipoPre = fila["TipoPredio"]?.ToString().Trim();
				string cuenta = fila["CuentaPredial"]?.ToString().Trim();
				string clasePago = fila["ClasePago"]?.ToString().Trim();
				string bimestre = fila["Bimestre"]?.ToString().Trim();
				string impuestoStr = fila["ImpuestoDeterminado"]?.ToString().Trim();
				string fechaStr = fila["FechaVigencia"]?.ToString().Trim();

				// =================================================================================
				// 🚀 REGLAS DE NEGOCIO Y AUTOCORRECCIÓN
				// =================================================================================

				// REGLA A: Filtro de "Basura Histórica" (Si es archivo de pagos y no trae pago 2026, ignorar)
				if (param.TipoCargaId == 2)
				{
					if (string.IsNullOrWhiteSpace(impuestoStr) || impuestoStr == "0" || impuestoStr == "0.00" || impuestoStr == "-")
					{
						resultado.RegistrosIgnorados++;
						continue; // Saltamos la fila, ni siquiera es un error, es basura histórica
					}
				}

				// REGLA B: Limpieza de Cuenta Predial y Tipo de Predio (Ej. "U-2270" o "1-2270")
				if (!string.IsNullOrWhiteSpace(cuenta) && (cuenta.Contains("-") || cuenta.StartsWith("U") || cuenta.StartsWith("R") || cuenta.StartsWith("S")))
				{
					if (cuenta.StartsWith("U", StringComparison.OrdinalIgnoreCase) || cuenta.StartsWith("1-")) { tipoPre = "1"; }
					else if (cuenta.StartsWith("R", StringComparison.OrdinalIgnoreCase) || cuenta.StartsWith("2-")) { tipoPre = "2"; }
					else if (cuenta.StartsWith("S", StringComparison.OrdinalIgnoreCase) || cuenta.StartsWith("3-")) { tipoPre = "3"; }

					// Extraemos solo los números de la cuenta predial
					cuenta = new string(cuenta.Where(char.IsDigit).ToArray());
				}

				// Si el Tipo de Predio viene como U, R, S en su propia columna, lo corregimos a número
				if (tipoPre.Equals("U", StringComparison.OrdinalIgnoreCase)) tipoPre = "1";
				if (tipoPre.Equals("R", StringComparison.OrdinalIgnoreCase)) tipoPre = "2";
				if (tipoPre.Equals("S", StringComparison.OrdinalIgnoreCase)) tipoPre = "3";

				// REGLA C: Inferencia de Fechas y Bimestres por el nombre de la Pestaña
				if (string.IsNullOrWhiteSpace(fechaStr) && !string.IsNullOrWhiteSpace(contextoPestaña))
				{
					if (contextoPestaña.Contains("ENERO"))
					{
						fechaStr = "02/01/2026"; // 2 de enero (Primer día hábil)
						if (string.IsNullOrWhiteSpace(clasePago)) clasePago = "1"; // Anual
						if (string.IsNullOrWhiteSpace(bimestre)) bimestre = "0";
					}
					// Aquí puedes agregar más meses (Febrero, Marzo, etc.)
				}

				// REGLA D: Clave de Municipio (Fallback inteligente)
				// Si viene vacío o trae texto en lugar de número, usamos la OficinaId que seleccionó el usuario
				if (string.IsNullOrWhiteSpace(claveMun) || !short.TryParse(claveMun, out _))
				{
					// Si el admin seleccionó el municipio en la interfaz, lo usamos para salvar el registro
					claveMun = param.OficinaId.ToString();
				}

				// =================================================================================
				// 🛑 VALIDACIONES FINALES (Si después de autocorregir siguen mal, se rechazan)
				// =================================================================================

				if (string.IsNullOrWhiteSpace(cuenta)) erroresFila.Add("No se pudo identificar un número de cuenta predial válido.");
				if (!short.TryParse(claveMun, out short numMun) || numMun < 1 || numMun > 217) erroresFila.Add($"Clave de municipio '{claveMun}' inválida.");
				if (!byte.TryParse(tipoPre, out byte numPre) || numPre < 1 || numPre > 3) erroresFila.Add($"Tipo de predio '{tipoPre}' inválido (1=Urbano, 2=Rústico, 3=Suburbano).");

				// =================================================================================
				// 🚦 DISTRIBUCIÓN DE CARRILES
				// =================================================================================
				if (erroresFila.Count > 0)
				{
					// CARRIL ROJO (Rechazados)
					DataRow filaError = resultado.TablaRechazados.NewRow();
					filaError.ItemArray = fila.ItemArray.Clone() as object[];
					filaError["MotivoRechazo"] = string.Join(" | ", erroresFila);
					resultado.TablaRechazados.Rows.Add(filaError);

					resultado.DetallesErrores.Add($"Fila {numeroFila}: {string.Join(", ", erroresFila)}");
				}
				else
				{
					// CARRIL VERDE (Aceptados)
					// Reescribimos los valores en la fila para que SQL reciba los datos ya corregidos (limpios)
					fila["ClaveMunicipio"] = claveMun;
					fila["TipoPredio"] = tipoPre;
					fila["CuentaPredial"] = cuenta;
					fila["ClasePago"] = clasePago;
					fila["Bimestre"] = bimestre;
					fila["FechaVigencia"] = fechaStr;
					fila["FolioCarga"] = param.FolioCarga.ToString(); // Inyectamos el control

					resultado.TablaValidos.ImportRow(fila);
				}
			}

			return resultado;
		}
	}
}