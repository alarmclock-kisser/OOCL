using Microsoft.AspNetCore.Mvc;
using OOCL.OpenCl;
using OOCL.Shared;

namespace OOCL.Api.Controllers
{
	[ApiController]
	[Route("api/[controller]")]
	public class CryptoController : ControllerBase
	{
		private readonly OpenClService openClService;

		public CryptoController(OpenClService openClService)
		{
			this.openClService = openClService;
		}

		[HttpGet("encrypt")]
		public async Task<CryptoObjInfo> Encrypt(string message, string keyphrase, int bits = 256)
		{
			// Create new CryptoObj
			var cryptoObj = new Core.CryptoObj(message, "AES", bits, keyphrase);

			// Encrypt the message
			// cryptoObj = await this.openClService.Encrypt(cryptoObj, "aesEncrypt", "01", keyphrase, bits);

			// Return the CryptoObjInfo
			return new CryptoObjInfo(cryptoObj);
		}

		[HttpGet("decrypt")]
		public async Task<CryptoObjInfo> Decrypt(string encryptedMessage, string keyphrase, int bits = 256)
		{
			// Create new CryptoObj
			var cryptoObj = new Core.CryptoObj("", "AES", bits);
			cryptoObj.SetEncryptedMessage(encryptedMessage);
			cryptoObj.Decrypt(keyphrase);

			// Decrypt the message
			// cryptoObj = await this.openClService.Decrypt(cryptoObj, "aesDecrypt", "01", keyphrase, bits);

			// Return the CryptoObjInfo
			return new CryptoObjInfo(cryptoObj);
		}
	}
}
