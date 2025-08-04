using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Diagnostics;

namespace OOCL.Core
{
	public class CryptoObj
	{
		public string ClearMessage { get; private set; } = "";
		public string EncryptedMessage { get; private set; } = "";
		public string Algorithm { get; private set; } = "AES";
		public int Bits { get; private set; } = 256;

		public double ElapsedEncryption { get; private set; } = 0.0;
		public double ElapsedDecryption { get; private set; } = 0.0;

		public CryptoObj(string clearMessage, string algorithm = "AES", int bits = 256, string keyphrase = "")
		{
			this.ClearMessage = clearMessage;
			this.Algorithm = algorithm;
			this.Bits = bits;

			if (!string.IsNullOrEmpty(keyphrase))
			{
				this.Encrypt(keyphrase, bits);
			}
		}

		public byte[] GetClearMessageBytes()
		{
			return Encoding.UTF8.GetBytes(this.ClearMessage);
		}

		public byte[] GetEncryptedMessageBytes()
		{
			if (string.IsNullOrEmpty(this.EncryptedMessage))
			{
				return [];
			}

			return Convert.FromBase64String(this.EncryptedMessage);
		}

		public void SetEncryptedMessage(string encryptedMessage)
		{
			this.ClearMessage = string.Empty;
			this.EncryptedMessage = encryptedMessage;
		}

		public void SetEncryptedMessage(byte[] encryptedMessageBytes)
		{
			this.ClearMessage = string.Empty;
			this.EncryptedMessage = Convert.ToBase64String(encryptedMessageBytes);
		}

		public void SetClearMessage(string clearMessage)
		{
			this.EncryptedMessage = string.Empty;
			this.ClearMessage = clearMessage;
		}

		public void SetClearMessage(byte[] clearMessageBytes)
		{
			this.EncryptedMessage = string.Empty;
			this.ClearMessage = Encoding.UTF8.GetString(clearMessageBytes);
		}

		public double Encrypt(string keyphrase, int bits = 256)
		{
			this.Bits = bits;
			if (string.IsNullOrEmpty(this.ClearMessage))
			{
				Console.WriteLine("Clear message is empty. Can't encrypt.");
			}
			Stopwatch sw = Stopwatch.StartNew();

			using var aes = Aes.Create();
			aes.KeySize = bits;
			aes.GenerateIV();
			var key = Encoding.UTF8.GetBytes(keyphrase.PadRight(aes.KeySize / 8));
			aes.Key = key;
			using var encryptor = aes.CreateEncryptor(aes.Key, aes.IV);
			using var ms = new MemoryStream();
			ms.Write(aes.IV, 0, aes.IV.Length);
			using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
			{
				var clearBytes = this.GetClearMessageBytes();
				cs.Write(clearBytes, 0, clearBytes.Length);
			}
			this.EncryptedMessage = Convert.ToBase64String(ms.ToArray());
			this.ClearMessage = string.Empty;

			sw.Stop();
			this.ElapsedEncryption = sw.Elapsed.TotalSeconds;
			return sw.Elapsed.TotalSeconds;
		}

		public double Decrypt(string keyphrase)
		{
			if (string.IsNullOrEmpty(this.EncryptedMessage))
			{
				Console.WriteLine("Encrypted message is empty, can't decrypt.");
			}

			Stopwatch sw = Stopwatch.StartNew();

			var encryptedBytes = this.GetEncryptedMessageBytes();
			using var aes = Aes.Create();
			aes.KeySize = this.Bits;
			var key = Encoding.UTF8.GetBytes(keyphrase.PadRight(aes.KeySize / 8));
			aes.Key = key;
			using var ms = new MemoryStream(encryptedBytes);
			var iv = new byte[aes.IV.Length];
			ms.Read(iv, 0, iv.Length);
			aes.IV = iv;
			using var decryptor = aes.CreateDecryptor(aes.Key, aes.IV);
			using var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
			using var reader = new StreamReader(cs);
			this.ClearMessage = reader.ReadToEnd();
			this.EncryptedMessage = string.Empty;

			sw.Stop();
			this.ElapsedDecryption = sw.Elapsed.TotalSeconds;
			return sw.Elapsed.TotalSeconds;
		}
	}
}
