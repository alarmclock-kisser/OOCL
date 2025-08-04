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

		[HttpGet("encrypt/{keyphrase}/{message}")]
		public async Task<CryptoObjInfo> Encrypt(string message, string keyphrase)
		{
			// Create new CryptoObj
			var cryptoObj = await Task.Run(() => new Core.CryptoObj(message, "AES", 256, keyphrase));

			// Encrypt the message
			// cryptoObj = await this.openClService.Encrypt(cryptoObj, "aesEncrypt", "01", keyphrase, bits);

			// Return the CryptoObjInfo
			return new CryptoObjInfo(cryptoObj);
		}

		[HttpGet("decrypt/{keyphrase}/{encryptedMessage}")]
		public async Task<CryptoObjInfo> Decrypt(string encryptedMessage, string keyphrase)
		{
			// Create new CryptoObj
			var cryptoObj = await Task.Run(() => new Core.CryptoObj("", "AES", 256));
			cryptoObj.SetEncryptedMessage(encryptedMessage);
			cryptoObj.Decrypt(keyphrase);

			// Decrypt the message
			// cryptoObj = await this.openClService.Decrypt(cryptoObj, "aesDecrypt", "01", keyphrase, bits);

			// Return the CryptoObjInfo
			return new CryptoObjInfo(cryptoObj);
		}
	}
}
