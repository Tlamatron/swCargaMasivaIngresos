using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace swCargaMasivaIngresos.Utils
{
	public static class CryptoHelper
	{
		// 🚨 IMPORTANTE: Llave secreta de 32 caracteres (256 bits)
		private static readonly byte[] Key = Encoding.UTF8.GetBytes("Puebl4_C4rg4_M4s1v4_S3gur1d4d!26");

		// Vector de inicialización de 16 caracteres (128 bits)
		private static readonly byte[] IV = Encoding.UTF8.GetBytes("V3ct0r_In1c14l_X");

		public static string Encriptar(string textoPlano)
		{
			if (string.IsNullOrEmpty(textoPlano)) return textoPlano;

			using (Aes aesAlg = Aes.Create())
			{
				aesAlg.Key = Key;
				aesAlg.IV = IV;
				ICryptoTransform encryptor = aesAlg.CreateEncryptor(aesAlg.Key, aesAlg.IV);

				using (MemoryStream msEncrypt = new MemoryStream())
				{
					using (CryptoStream csEncrypt = new CryptoStream(msEncrypt, encryptor, CryptoStreamMode.Write))
					using (StreamWriter swEncrypt = new StreamWriter(csEncrypt))
					{
						swEncrypt.Write(textoPlano);
					}
					return Convert.ToBase64String(msEncrypt.ToArray());
				}
			}
		}

		public static string Desencriptar(string textoCifrado)
		{
			if (string.IsNullOrEmpty(textoCifrado)) return textoCifrado;

			using (Aes aesAlg = Aes.Create())
			{
				aesAlg.Key = Key;
				aesAlg.IV = IV;
				ICryptoTransform decryptor = aesAlg.CreateDecryptor(aesAlg.Key, aesAlg.IV);

				using (MemoryStream msDecrypt = new MemoryStream(Convert.FromBase64String(textoCifrado)))
				using (CryptoStream csDecrypt = new CryptoStream(msDecrypt, decryptor, CryptoStreamMode.Read))
				using (StreamReader srDecrypt = new StreamReader(csDecrypt))
				{
					return srDecrypt.ReadToEnd();
				}
			}
		}
	}
}