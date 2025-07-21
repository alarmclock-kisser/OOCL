using System.Diagnostics;
using OOCL.Core;
using OOCL.Shared;
using Microsoft.AspNetCore.Mvc;
using TextCopy;

namespace OOCL.Api.Controllers
{
	[ApiController]
	[Route("api/[controller]")]
	public class ImageController : ControllerBase
	{
		private readonly ImageCollection imageCollection;
		private readonly IClipboard clipboard;

		public ImageController(ImageCollection imageCollection, IClipboard clipboard)
		{
			this.imageCollection = imageCollection;
			this.clipboard = clipboard;
		}

		[HttpGet("images")]
		[ProducesResponseType(typeof(IEnumerable<ImageObjInfo>), 200)]
		[ProducesResponseType(204)]
		[ProducesResponseType(500)]
		public async Task<ActionResult<IEnumerable<ImageObjInfo>>> GetImages()
		{
			try
			{
				var infos = (await Task.Run(() =>
					this.imageCollection.Images.AsParallel().Select(i => new ImageObjInfo(i))
				)).AsEnumerable();

				if (!infos.Any())
				{
					return this.NoContent();
				}

				return this.Ok(infos);
			}
			catch (Exception ex)
			{
				Console.WriteLine("Error getting 'api/image/images': " + ex);
				return this.StatusCode(500, ex);
			}
		}

		[HttpGet("images/{guid}/info")]
		[ProducesResponseType(typeof(ImageObjInfo), 200)]
		[ProducesResponseType(204)]
		[ProducesResponseType(500)]
		public async Task<ActionResult<ImageObjInfo>> GetImageInfo(Guid guid)
		{
			try
			{
				var info = await Task.Run(() =>
				{
					return this.imageCollection[guid];
				});

				if (info == null || info.Id == Guid.Empty)
				{
					return this.NoContent();
				}

				return this.Ok(info);
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Error calling 'api/image/images/{guid}/info': " + ex);
				return this.StatusCode(500, ex);
			}
		}

		[HttpDelete("images/{guid}/remove")]
		[ProducesResponseType(typeof(bool), 200)]
		[ProducesResponseType(404)]
		[ProducesResponseType(400)]
		[ProducesResponseType(500)]
		public async Task<ActionResult<bool>> RemoveImage(Guid guid)
		{
			try
			{
				var obj = await Task.Run(() => this.imageCollection[guid]);

				if (obj == null || obj.Id == Guid.Empty)
				{
					return this.NotFound($"No image found with Guid '{guid}'");
				}

				var result = await Task.Run(() => this.imageCollection.Remove(guid));

				if (!result)
				{
					return this.BadRequest($"Couldn't remove image with Guid '{guid}'");
				}

				result = this.imageCollection[guid] == null;

				if (!result)
				{
					return this.BadRequest($"Couldn't remove image with Guid '{guid}'");
				}

				return this.Ok(result);
			}
			catch (Exception ex)
			{
				return this.StatusCode(500, $"Error calling api/image/{guid}/remove': {ex.Message}");
			}
		}

		[HttpPost("images/empty/{width}/{height}")]
		[ProducesResponseType(typeof(ImageObjInfo), 201)]
		[ProducesResponseType(404)]
		[ProducesResponseType(400)]
		[ProducesResponseType(500)]
		public async Task<ActionResult<ImageObjInfo>> AddEmptyImage(int width = 1920, int height = 1080)
		{
			try
			{
				var obj = await Task.Run(() => this.imageCollection.PopEmpty(new(width, height), true));

				if (obj == null || obj.Id == Guid.Empty)
				{
					return this.BadRequest($"Failed to create empty image with size {width}x{height}");
				}

				var info = new ImageObjInfo(obj);

				var result = this.imageCollection[info.Id] != null;

				if (!result)
				{
					return this.NotFound("Couldn't get image in collection after creating");
				}

				return this.Created($"api/audios/{info.Id}/info", info);
			}
			catch (Exception ex)
			{
				return this.StatusCode(500, $"Error calling 'images/empty/{width}/{height}': {ex.Message}");
			}
		}

		[HttpPost("images/upload")]
        [RequestSizeLimit(256_000_000)]
        [ProducesResponseType(typeof(ImageObjInfo), 201)]
		[ProducesResponseType(204)]
		[ProducesResponseType(404)]
		[ProducesResponseType(400)]
		[ProducesResponseType(500)]
		public async Task<ActionResult<ImageObjInfo>> UploadImage(IFormFile file, bool copyGuid = true)
		{
			if (file.Length == 0)
			{
				return this.NoContent();
			}

			Stopwatch sw = Stopwatch.StartNew();

			// Temp dir for storing uploaded files
			var tempDir = Path.Combine(Path.GetTempPath(), "image_uploads");
			Directory.CreateDirectory(tempDir);

			// Store original file name and path
			var originalFileName = Path.GetFileName(file.FileName);
			var tempPath = Path.Combine(tempDir, originalFileName);

			try
			{
				// Save file with original name
				await using (var stream = System.IO.File.Create(tempPath))
				{
					await file.CopyToAsync(stream);
				}

				// Load the image into the collection
				var imgObj = await this.imageCollection.LoadImage(tempPath);

				// Keep the original file name in the ImgObj
				if (imgObj != null && imgObj.Id != Guid.Empty && imgObj.Img != null)
				{
					imgObj.Name = imgObj.Name ?? Path.GetFileNameWithoutExtension(originalFileName);
					imgObj.Filepath = originalFileName;
					this.imageCollection.Add(imgObj);
				}
				else
				{
					return this.BadRequest("Failed to load image from uploaded file.");
				}

				var info = this.imageCollection[imgObj.Id];

				if (info == null || info.Id == Guid.Empty)
				{
					imgObj.Dispose();
					return this.NotFound("Failed to retrieve image information after upload.");
				}

				await this.clipboard.SetTextAsync(info.Id.ToString());
				return this.Created($"api/images/{info.Id}/info", info);
			}
			catch (Exception ex)
			{
				string msg = $"Error uploading image: {ex.Message} ({ex.InnerException?.Message})";
				Console.WriteLine(msg);

				return this.BadRequest(msg);
			}
			finally
			{
				try
				{
					if (System.IO.File.Exists(tempPath))
					{
						System.IO.File.Delete(tempPath);
					}
				}
				catch (Exception ex)
				{
					Console.WriteLine($"Error deleting temporary file: {ex.Message}");
				}
				finally
				{
					sw.Stop();
					await Task.Yield();
				}
			}
		}

		[HttpGet("images/{guid}/download/{format}")]
		[ProducesResponseType(200)]
		[ProducesResponseType(204)]
		[ProducesResponseType(404)]
		[ProducesResponseType(400)]
		[ProducesResponseType(500)]
		public async Task<IActionResult> DownloadImage(Guid guid, string format = "bmp")
		{
			string? filePath = null;
			byte[] fileBytes = [];

			try
			{
				var obj = this.imageCollection[guid];

				if (obj == null)
				{
					return this.NotFound($"Image with GUID {guid} not found.");
				}

				var info = new ImageObjInfo(obj);

				if (obj.Img == null)
				{
					return this.NoContent();
				}

				// Download image as file
				filePath = await obj.Export("", format);
				if (string.IsNullOrEmpty(filePath))
				{
					return this.BadRequest("Failed to export image to file.");
				}

				fileBytes = await System.IO.File.ReadAllBytesAsync(filePath);

				return this.File(fileBytes, "application/octet-stream", Path.GetFileName(filePath));
			}
			catch (Exception ex)
			{
				return this.StatusCode(500, $"Error downloading image: {ex.Message}");
			}
		}

		[HttpGet("images/{guid}/image64/{format}")]
		[ProducesResponseType(typeof(ImageData), 200)]
		[ProducesResponseType(204)]
		[ProducesResponseType(404)]
		[ProducesResponseType(500)]
		[Produces("application/json")]
		public async Task<ActionResult<ImageData>> GetBase64(Guid guid, string format = "bmp")
		{
			try
			{
				var obj = this.imageCollection[guid];
				if (obj == null || obj.Id == Guid.Empty)
				{
					return this.NotFound($"No image found with Guid '{guid}'");
				}

				var code = await obj.AsBase64Image(format);

				if (string.IsNullOrEmpty(code))
				{
					return this.NoContent();
				}

				//var data = new ImageData(code, obj.Width, obj.Height);
				var data = new ImageData(obj);
				if (data.Base64Image == string.Empty)
				{
					return this.NoContent();
				}

				return this.Ok(data);
			}
			catch (Exception ex)
			{
				Console.WriteLine("Error getting base64 string: " + ex.Message);
				return this.StatusCode(500, ex);
			}
		}


	}
}
