using swCargaMasivaIngresos.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;

namespace swCargaMasivaIngresos.Services
{
	/// <summary>
	/// Contiene los resultados de la limpieza y validación de datos, incluyendo las filas válidas, las filas rechazadas con motivos, los detalles de errores y el conteo de registros ignorados.
	/// </summary>
	public class ResultadoLimpieza
	{
		/// <summary>
		/// Tabla que contiene las filas válidas después de la limpieza y validación.
		/// </summary>
		public DataTable TablaValidos { get; set; }
		/// <summary>
		/// Tabla que contiene las filas rechazadas después de la limpieza y validación, junto con los motivos de rechazo.
		/// </summary>
		public DataTable TablaRechazados { get; set; }
		/// <summary>
		/// Detalles de errores encontrados durante la limpieza y validación, incluyendo el número de fila y la descripción del error.|
		/// </summary>
		public List<string> DetallesErrores { get; set; } = new List<string>();
		/// <summary>
		/// Registro de la cantidad de filas que fueron ignoradas durante el proceso de limpieza y validación, por ejemplo, debido a filtros de basura histórica.
		/// </summary>
		public int RegistrosIgnorados { get; set; }
	}

	/// <summary>
	/// Limpia y valida los datos de la tabla mapeada según las reglas de negocio definidas, devolviendo un objeto ResultadoLimpieza con los resultados del proceso.
	/// </summary>
	public static class LimpiadorDatos
	{
		/// <summary>
		/// Limpia un número de cuenta predial cruda, eliminando cualquier carácter que no sea un dígito. Esto asegura que la cuenta predial esté en un formato estandarizado para su posterior procesamiento.
		/// </summary>
		/// <param name="cuentaCruda"></param>
		/// <returns></returns>
		public static string LimpiarCuenta(string cuentaCruda)
		{
			if (string.IsNullOrWhiteSpace(cuentaCruda)) return string.Empty;
			return new string(cuentaCruda.Where(char.IsDigit).ToArray());
		}

		/// <summary>
		/// Limpia y valida los datos de la tabla mapeada según las reglas de negocio definidas, devolviendo un objeto ResultadoLimpieza con las filas válidas, las filas rechazadas con motivos, los detalles de errores y el conteo de registros ignorados.
		/// </summary>
		/// <param name="tablaMapeada"></param>
		/// <param name="contextoPestaña"></param>
		/// <param name="param"></param>
		/// <returns></returns>
		public static ResultadoLimpieza LimpiarYValidar(DataTable tablaMapeada, string contextoPestaña, ParametrosCarga param)
		{
			var resultado = new ResultadoLimpieza
			{
				TablaValidos = tablaMapeada.Clone(),
				TablaRechazados = tablaMapeada.Clone()
			};

			resultado.TablaRechazados.Columns.Add("MotivoRechazo", typeof(string));

			int numeroFila = 0;

			foreach (DataRow fila in tablaMapeada.Rows)
			{
				numeroFila++;
				List<string> erroresFila = new List<string>();

				string claveMun = fila["ClaveMunicipio"]?.ToString().Trim();
				string tipoPre = fila["TipoPredio"]?.ToString().Trim();
				string cuenta = fila["CuentaPredial"]?.ToString().Trim();
				string clasePago = fila["ClasePago"]?.ToString().Trim();
				string bimestre = fila["Bimestre"]?.ToString().Trim();
				string impuestoStr = fila["ImpuestoDeterminado"]?.ToString().Trim();
				string fechaStr = fila["FechaVigencia"]?.ToString().Trim();

				// =======================================================================
				// 🚀 REGLA A: Filtro de Basura Histórica (Pagos en ceros)
				// =======================================================================
				//if (param.TipoCargaId == 2)
				//{
				//	if (string.IsNullOrWhiteSpace(impuestoStr) || impuestoStr == "0" || impuestoStr == "0.00" || impuestoStr == "-")
				//	{
				//		resultado.RegistrosIgnorados++;
				//		continue;
				//	}
				//}

				// =======================================================================
				// 🚀 REGLA B: Limpieza Estricta de Cuenta Predial y Predio
				// =======================================================================
				if (!string.IsNullOrWhiteSpace(cuenta) && (cuenta.Contains("-") || cuenta.StartsWith("U") || cuenta.StartsWith("R") || cuenta.StartsWith("S")))
				{
					if (cuenta.StartsWith("U", StringComparison.OrdinalIgnoreCase) || cuenta.StartsWith("1-")) tipoPre = "1";
					else if (cuenta.StartsWith("R", StringComparison.OrdinalIgnoreCase) || cuenta.StartsWith("2-")) tipoPre = "2";
					else if (cuenta.StartsWith("S", StringComparison.OrdinalIgnoreCase) || cuenta.StartsWith("3-")) tipoPre = "3";

					cuenta = LimpiarCuenta(cuenta);
				}

				if (tipoPre?.Equals("U", StringComparison.OrdinalIgnoreCase) == true) tipoPre = "1";
				if (tipoPre?.Equals("R", StringComparison.OrdinalIgnoreCase) == true) tipoPre = "2";
				if (tipoPre?.Equals("S", StringComparison.OrdinalIgnoreCase) == true) tipoPre = "3";

				// =======================================================================
				// 🚀 REGLA C: Motor de Inferencia por Pestaña (Ayotoxco y Otros)
				// =======================================================================
				if (!string.IsNullOrWhiteSpace(contextoPestaña))
				{
					string pestañaMayus = contextoPestaña.ToUpper();

					if (string.IsNullOrWhiteSpace(fechaStr))
					{
						if (pestañaMayus.Contains("ENERO")) fechaStr = "02/01/2026";
						else if (pestañaMayus.Contains("FEBRERO")) fechaStr = "03/02/2026";
						else if (pestañaMayus.Contains("MARZO")) fechaStr = "02/03/2026";
						else if (pestañaMayus.Contains("ABRIL")) fechaStr = "01/04/2026";
						else if (pestañaMayus.Contains("MAYO")) fechaStr = "04/05/2026";
						else if (pestañaMayus.Contains("JUNIO")) fechaStr = "01/06/2026";
					}

					if (string.IsNullOrWhiteSpace(clasePago))
					{
						if (pestañaMayus.Contains("ANUAL") || pestañaMayus.Contains("ENERO") || pestañaMayus.Contains("FEBRERO"))
							clasePago = "1";
						else if (pestañaMayus.Contains("BIMESTRAL") || pestañaMayus.Contains("BIMESTRE") || pestañaMayus.Contains("-BIM") || pestañaMayus.Contains(" BIM"))
							clasePago = "2";
					}

					if (string.IsNullOrWhiteSpace(tipoPre))
					{
						if (pestañaMayus.Contains("SUB-URBANO") || pestañaMayus.Contains("SUBURBANO") || pestañaMayus.Contains("SUB")) tipoPre = "3";
						else if (pestañaMayus.Contains("URBANO")) tipoPre = "1";
						else if (pestañaMayus.Contains("RUSTICO") || pestañaMayus.Contains("RÚSTICO")) tipoPre = "2";
					}
				}

				// =======================================================================
				// 🚀 REGLA D: Fallback Seguro de Municipio (Prevención Error 500)
				// =======================================================================
				// Intentamos convertirlo. Si falla, o si está fuera del rango poblano (1 a 217), forzamos el Fallback.
				if (string.IsNullOrWhiteSpace(claveMun) ||
					!short.TryParse(claveMun, out short numMpioEvaluado) ||
					numMpioEvaluado < 1 || numMpioEvaluado > 217)
				{
					if (param != null && param.ClaveMunicipioDestino > 0)
					{
						claveMun = param.ClaveMunicipioDestino.ToString();
					}
				}
				//if (string.IsNullOrWhiteSpace(claveMun) || !short.TryParse(claveMun, out _))
				//{
				//	if (param.ClaveMunicipioDestino > 0) claveMun = param.ClaveMunicipioDestino.ToString();
				//}

				// =======================================================================
				// 🚀 REGLA E: Blindaje Contable de Bimestres
				// =======================================================================
				//if (!string.IsNullOrWhiteSpace(bimestre) && bimestre.Contains(","))
				//{
				//	bimestre = bimestre.Split(',').Last().Trim(); // "1,2,3" -> "3"
				//}
				//if (clasePago == "1" || string.IsNullOrWhiteSpace(bimestre))
				//{
				//	bimestre = "0"; // Regla SQL: Si es Anual, el bimestre es 0
				//}
				if (!string.IsNullOrWhiteSpace(bimestre) && bimestre.Contains(","))
				{
					bimestre = bimestre.Split(',').Last().Trim(); // "1,2,3" -> "3"
				}

				// Implementación de la Regla de Negocio (Anual vs Bimestral)
				if (clasePago == "1")
				{
					// Si es Anual, NO IMPORTA si omitieron la columna de bimestre, siempre será 0
					bimestre = "0";
				}
				else if (clasePago == "2")
				{
					// Si es Bimestral, la columna es obligatoria. Si no viene o viene en 0, es un error.
					if (string.IsNullOrWhiteSpace(bimestre) || bimestre == "0" || bimestre == "99")
					{
						erroresFila.Add("El pago es Bimestral pero no se indicó el Bimestre.");
					}
				}
				else if (string.IsNullOrWhiteSpace(bimestre))
				{
					bimestre = "99";
				}


				// =======================================================================
				// 🛑 ADUANA FINAL DE VALIDACIÓN
				// =======================================================================
				if (string.IsNullOrWhiteSpace(cuenta)) erroresFila.Add("No se pudo identificar un número de cuenta predial válido.");
				if (!short.TryParse(claveMun, out short numMun) || numMun < 1 || numMun > 217) erroresFila.Add($"Clave de municipio '{claveMun}' inválida.");
				if (!byte.TryParse(tipoPre, out byte numPre) || numPre < 1 || numPre > 3) erroresFila.Add($"Tipo de predio '{tipoPre}' inválido (1=Urbano, 2=Rústico, 3=Suburbano).");

				// 🚀 CANDADO FINANCIERO 2: Si es una carga de Pagos (Tipo 2), el monto es obligatorio y debe ser mayor a 0
				//if (param != null && param.TipoCargaId == 2)
				//{
				//	string importeStr = fila["ImpuestoDeterminado"]?.ToString().Replace("$", "").Replace(",", "").Trim();
				//	if (string.IsNullOrWhiteSpace(importeStr) || !decimal.TryParse(importeStr, out decimal importePagado) || importePagado <= 0)
				//	{
				//		erroresFila.Add("El monto del pago está vacío, es $0.00 o tiene un formato inválido.");
				//	}
				//}
				// =======================================================================
				// 🚦 DISTRIBUCIÓN A TABLAS DE SALIDA
				// =======================================================================
				if (erroresFila.Count > 0)
				{
					DataRow filaError = resultado.TablaRechazados.NewRow();
					filaError.ItemArray = fila.ItemArray.Clone() as object[];
					filaError["MotivoRechazo"] = string.Join(" | ", erroresFila);
					resultado.TablaRechazados.Rows.Add(filaError);

					resultado.DetallesErrores.Add($"[Hoja: {contextoPestaña}] Fila {numeroFila}: {string.Join(", ", erroresFila)}");
				}
				else
				{
					fila["ClaveMunicipio"] = claveMun;
					fila["TipoPredio"] = tipoPre;
					fila["CuentaPredial"] = cuenta;
					fila["ClasePago"] = clasePago;
					fila["Bimestre"] = bimestre;
					fila["FechaVigencia"] = fechaStr;
					fila["FolioCarga"] = param.FolioCarga.ToString();

					resultado.TablaValidos.ImportRow(fila);
				}
			}

			return resultado;
		}
	}
}