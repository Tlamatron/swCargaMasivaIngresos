using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace swCargaMasivaIngresos.Models
{
	/// <summary>
	/// Clase que representa los datos necesarios para generar documentos PDF relacionados con un usuario, como el Acta de Entrega, la Carta de Confidencialidad y la Credencial de Acceso. Contiene información relevante del usuario y del municipio para asegurar la correcta emisión y trazabilidad de los documentos.
	/// </summary>
	public class DatosDocumentoUsuario
	{
		/// <summary>
		/// Consecutivo único que identifica al usuario en el sistema. Este campo es esencial para la trazabilidad y para asegurar que los documentos se generen para el usuario correcto.
		/// </summary>
		public int idUsuario { get; set; }
		/// <summary>
		/// Número de acta de entrega, que puede ser generado por el sistema o proporcionado por el municipio. Este campo es importante para la trazabilidad y registro del documento.
		/// Dicho número se solicita a la Dirección de Ingresos mediante la liga: https://docs.google.com/forms/d/e/1FAIpQLSeoSfPNEPzBGSTbhZRkNaRVpxN_7hGao_a04GZ_iBLXX_7fCw/viewform
		/// </summary>
		public string NumActa { get; set; }

		/// <summary>
		/// Login o usuario de la credencial de acceso. Este campo es esencial para identificar al usuario que recibirá el documento y para la generación de la credencial.
		/// </summary>
		public string Login { get; set; }

		/// <summary>
		/// CURP del usuario, que es un identificador único en México. Este campo es importante para la validación de identidad y para asegurar que el documento se emita a la persona correcta.
		/// </summary>
		public string CURP { get; set; }

		/// <summary>
		/// Nombre completo del usuario, que se utilizará en los documentos PDF para personalizarlos y asegurar que el destinatario sea claramente identificado.
		/// </summary>
		public string NombreCompleto { get; set; }

		/// <summary>
		/// Puesto o cargo del usuario dentro de la organización o municipio. Este campo es relevante para contextualizar el rol del usuario y puede ser utilizado en los documentos para reflejar su posición oficial.
		/// </summary>
		public string Puesto { get; set; }

		/// <summary>
		/// Adscripción o dependencia a la que pertenece el usuario. Este campo ayuda a identificar la unidad administrativa del usuario y puede ser útil para fines de registro y seguimiento en los documentos emitidos.
		/// </summary>
		public string Adscripcion { get; set; }

		/// <summary>
		/// Número de identificación del usuario, que puede ser un número de empleado, matrícula o cualquier otro identificador oficial. Este campo es importante para la trazabilidad y verificación de la identidad del usuario en los documentos generados.
		/// </summary>
		public string NumIdentificacion { get; set; }

		/// <summary>
		/// Tipo de credencial de donde se tomo el número de identificación, por ejemplo: INE, Pasaporte, Cédula Profesional, etc. Este campo es relevante para contextualizar el tipo de documento oficial que respalda la identidad del usuario y puede ser utilizado en los documentos para reflejar su validez.
		/// </summary>
		public string TipoCredencial { get; set; }

		/// <summary>
		/// Correo electrónico del usuario, que se utilizará para notificaciones y comunicaciones relacionadas con la emisión de los documentos. Este campo es esencial para asegurar que el usuario reciba información relevante sobre su credencial y otros documentos emitidos.
		/// </summary>
		public string CorreoElectronico { get; set; }

		/// <summary>
		/// Domicilio del usuario, que puede incluir calle, número, colonia, ciudad y código postal. Este campo es importante para la correspondencia oficial y para asegurar que los documentos se envíen a la dirección correcta del usuario.
		/// </summary>
		public string Domicilio { get; set; }

		/// <summary>
		/// Clave del municipio al que pertenece el usuario. Este campo es crucial para identificar la ubicación geográfica del usuario y puede ser utilizado en los documentos para reflejar su jurisdicción administrativa.
		/// </summary>
		public string ClaveMunicipio { get; set; }

		/// <summary>
		/// Rol del usuario dentro del sistema o la organización. Este campo es importante para determinar los permisos y accesos del usuario, así como para personalizar los documentos emitidos según su función o nivel jerárquico.
		/// </summary>
		public string Rol { get; set; }

		/// <summary>
		/// Fecha y hora en que se emite el documento. Este campo es esencial para la validez y registro de los documentos generados, asegurando que se pueda rastrear cuándo fueron emitidos.
		/// </summary>
		public DateTime FechaEmision { get; set; } = DateTime.Now;

		/// <summary>
		/// Estatus del usuario, indicando si está activo o inactivo. Este campo es crucial para determinar si el usuario puede recibir documentos y acceder a servicios relacionados con su credencial.
		/// </summary>
		public bool Activo { get; set; }
	}
}