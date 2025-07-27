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
		private readonly RollingFileLogger logger;

		public ImageController(ImageCollection imageCollection, IClipboard clipboard, RollingFileLogger logger)
		{
			this.logger = logger;
			this.imageCollection = imageCollection;
			this.clipboard = clipboard;
		}

		[HttpGet("images")]
		[ProducesResponseType(typeof(IEnumerable<ImageObjInfo>), 200)]
		[ProducesResponseType(typeof(ProblemDetails), 500)]
		public async Task<ActionResult<IEnumerable<ImageObjInfo>>> GetImages()
		{
			try
			{
				var infos = (await Task.Run(() =>
					this.imageCollection.Images.AsParallel().Select(i => new ImageObjInfo(i)))).ToArray();
				return this.Ok(infos.Length > 0 ? infos : Array.Empty<ImageObjInfo>());
			}
			catch (Exception ex)
			{
				return this.StatusCode(500, new ProblemDetails
				{
					Title = "Get Images Error",
					Detail = ex.Message,
					Status = 500
				});
			}
		}

		[HttpGet("{guid}/info")]
		[ProducesResponseType(typeof(ImageObjInfo), 200)]
		[ProducesResponseType(typeof(ProblemDetails), 500)]
		public async Task<ActionResult<ImageObjInfo>> GetImageInfo(Guid guid)
		{
			try
			{
				var info = await Task.Run(() => this.imageCollection[guid]);
				return this.Ok(info != null ? new ImageObjInfo(info) : new ImageObjInfo());
			}
			catch (Exception ex)
			{
				return this.StatusCode(500, new ProblemDetails
				{
					Title = "Image Info Error",
					Detail = ex.Message,
					Status = 500
				});
			}
		}

		[HttpDelete("{guid}/remove")]
		[ProducesResponseType(typeof(bool), 200)]
		[ProducesResponseType(typeof(ProblemDetails), 404)]
		[ProducesResponseType(typeof(ProblemDetails), 400)]
		[ProducesResponseType(typeof(ProblemDetails), 500)]
		public async Task<ActionResult<bool>> RemoveImage(Guid guid)
		{
			try
			{
				var obj = await Task.Run(() => this.imageCollection[guid]);
				if (obj == null || obj.Id == Guid.Empty)
				{
					return this.NotFound(new ProblemDetails
					{
						Title = "Image Not Found",
						Detail = $"No image found with Guid '{guid}'",
						Status = 404
					});
				}

				var result = await Task.Run(() => this.imageCollection.Remove(guid));
				if (!result)
				{
					return this.BadRequest(new ProblemDetails
					{
						Title = "Remove Failed",
						Detail = $"Couldn't remove image with Guid '{guid}'",
						Status = 400
					});
				}

				return this.Ok(this.imageCollection[guid] == null);
			}
			catch (Exception ex)
			{
				return this.StatusCode(500, new ProblemDetails
				{
					Title = "Remove Image Error",
					Detail = ex.Message,
					Status = 500
				});
			}
		}

		[HttpPost("empty/{width}/{height}")]
		[ProducesResponseType(typeof(ImageObjInfo), 201)]
		[ProducesResponseType(typeof(ProblemDetails), 400)]
		[ProducesResponseType(typeof(ProblemDetails), 404)]
		[ProducesResponseType(typeof(ProblemDetails), 500)]
		public async Task<ActionResult<ImageObjInfo>> AddEmptyImage(int width = 1920, int height = 1080)
		{
			try
			{
				var obj = await Task.Run(() => this.imageCollection.PopEmpty(new(width, height), true));
				if (obj == null || obj.Id == Guid.Empty)
				{
					return this.BadRequest(new ProblemDetails
					{
						Title = "Creation Failed",
						Detail = $"Failed to create empty image with size {width}x{height}",
						Status = 400
					});
				}

				var info = new ImageObjInfo(obj);
				if (this.imageCollection[info.Id] == null)
				{
					return this.NotFound(new ProblemDetails
					{
						Title = "Image Missing",
						Detail = "Couldn't get image in collection after creating",
						Status = 404
					});
				}

				return this.Created($"api/images/{info.Id}/info", info);
			}
			catch (Exception ex)
			{
				return this.StatusCode(500, new ProblemDetails
				{
					Title = "Create Image Error",
					Detail = ex.Message,
					Status = 500
				});
			}
		}

		[HttpPost("upload")]
		[RequestSizeLimit(256_000_000)]
		[ProducesResponseType(typeof(ImageObjInfo), 201)]
		[ProducesResponseType(typeof(ProblemDetails), 400)]
		[ProducesResponseType(typeof(ProblemDetails), 404)]
		[ProducesResponseType(typeof(ProblemDetails), 500)]
		public async Task<ActionResult<ImageObjInfo>> UploadImage(IFormFile file, bool copyGuid = true)
		{
			if (file.Length == 0)
			{
				return this.BadRequest(new ProblemDetails
				{
					Title = "Empty File",
					Detail = "Uploaded file is empty",
					Status = 400
				});
			}

			Stopwatch sw = Stopwatch.StartNew();
			var tempDir = Path.Combine(Path.GetTempPath(), "image_uploads");
			Directory.CreateDirectory(tempDir);
			var tempPath = Path.Combine(tempDir, Path.GetFileName(file.FileName));

			try
			{
				await using (var stream = System.IO.File.Create(tempPath))
				{
					await file.CopyToAsync(stream);
				}

				var imgObj = await this.imageCollection.LoadImage(tempPath);
				if (imgObj == null || imgObj.Id == Guid.Empty)
				{
					return this.BadRequest(new ProblemDetails
					{
						Title = "Upload Failed",
						Detail = "Failed to load image from uploaded file",
						Status = 400
					});
				}

				imgObj.Name ??= Path.GetFileNameWithoutExtension(file.FileName);
				imgObj.Filepath = file.FileName;
				this.imageCollection.Add(imgObj);

				var info = this.imageCollection[imgObj.Id];
				if (info == null)
				{
					imgObj.Dispose();
					return this.NotFound(new ProblemDetails
					{
						Title = "Upload Info Missing",
						Detail = "Failed to retrieve image information after upload",
						Status = 404
					});
				}

				if (copyGuid) await this.clipboard.SetTextAsync(info.Id.ToString());
				return this.Created($"api/image/{info.Id}/info", info);
			}
			catch (Exception ex)
			{
				return this.StatusCode(500, new ProblemDetails
				{
					Title = "Upload Error",
					Detail = ex.Message,
					Status = 500
				});
			}
			finally
			{
				try { if (System.IO.File.Exists(tempPath)) System.IO.File.Delete(tempPath); }
				catch (Exception ex) { Console.WriteLine($"Error deleting temp file: {ex.Message}"); }
				sw.Stop();
			}
		}

		[HttpGet("download/{guid}/{format?}")]
		[ProducesResponseType(typeof(FileResult), 200)]
		[ProducesResponseType(typeof(ProblemDetails), 404)]
		[ProducesResponseType(typeof(ProblemDetails), 400)]
		[ProducesResponseType(typeof(ProblemDetails), 500)]
		public async Task<IActionResult> DownloadImage(Guid guid, string format = "png")
		{
			var allowedFormats = new[] { "bmp", "png", "jpg" };
			format = allowedFormats.Contains(format?.ToLower() ?? string.Empty) ? format ?? "png" : "png";

			try
			{
				var obj = this.imageCollection[guid];
				if (obj == null)
				{
					return this.NotFound(new ProblemDetails
					{
						Title = "Image Not Found",
						Detail = $"Image with GUID {guid} not found",
						Status = 404
					});
				}

				var filePath = await obj.Export("", format);
				if (string.IsNullOrEmpty(filePath))
				{
					return this.BadRequest(new ProblemDetails
					{
						Title = "Export Failed",
						Detail = "Failed to export image to file",
						Status = 400
					});
				}

				var fileBytes = await System.IO.File.ReadAllBytesAsync(filePath);
				return this.File(fileBytes, "application/octet-stream", Path.GetFileName(filePath));
			}
			catch (Exception ex)
			{
				return this.StatusCode(500, new ProblemDetails
				{
					Title = "Download Error",
					Detail = ex.Message,
					Status = 500
				});
			}
		}

		[HttpGet("{guid}/image64/{format?}")]
		[ProducesResponseType(typeof(ImageData), 200)]
		[ProducesResponseType(typeof(ProblemDetails), 404)]
		[ProducesResponseType(typeof(ProblemDetails), 500)]
		public async Task<ActionResult<ImageData>> GetBase64(Guid guid, string format = "png")
		{
			var allowedFormats = new[] { "bmp", "png", "jpg" };
			format = allowedFormats.Contains(format?.ToLower() ?? string.Empty) ? format ?? "png" : "bmp";

			try
			{
				var obj = this.imageCollection[guid];
				if (obj == null || obj.Id == Guid.Empty)
				{
					return this.NotFound(new ProblemDetails
					{
						Title = "Image Not Found",
						Detail = $"No image found with Guid '{guid}'",
						Status = 404
					});
				}

				var code = await obj.AsBase64Image(format);
				if (string.IsNullOrEmpty(code))
				{
					return this.Ok(new ImageData ());
				}

				var data = new ImageData(obj);
				return this.Ok(data);
			}
			catch (Exception ex)
			{
				return this.StatusCode(500, new ProblemDetails
				{
					Title = "Base64 Error",
					Detail = ex.Message,
					Status = 500
				});
			}
		}


	}
}
