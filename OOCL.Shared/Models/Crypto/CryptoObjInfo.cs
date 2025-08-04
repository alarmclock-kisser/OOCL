using OOCL.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace OOCL.Shared
{
	public class CryptoObjInfo
	{
		public string MessageText { get; set; } = string.Empty;
		public double ElapsedTime { get; set; } = 0.0;
		public string Algorithm { get; set; } = "";
		public int Bits { get; set; } = 0;

		public CryptoObjInfo()
		{
			// Default constructor for serialization
		}

		[JsonConstructor]
		public CryptoObjInfo(CryptoObj obj)
		{
			this.MessageText = string.IsNullOrEmpty(obj.ClearMessage) ? obj.EncryptedMessage : obj.ClearMessage;
			this.ElapsedTime = string.IsNullOrEmpty(obj.EncryptedMessage) ? obj.ElapsedDecryption : obj.ElapsedEncryption;
			this.Algorithm = obj.Algorithm;
			this.Bits = obj.Bits;
		}
	}
}
