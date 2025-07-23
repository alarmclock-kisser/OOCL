using System.Diagnostics;
using OOCL.Core;
using OOCL.Shared;
using Microsoft.AspNetCore.Mvc;
using TextCopy;

namespace OOCL.Api.Controllers
{
	[ApiController]
	[Route("api/[controller]")]
	public class AudioController : ControllerBase
	{
		private readonly AudioCollection audioCollection;
		private readonly IClipboard clipboard;

		public AudioController(AudioCollection audioCollection, IClipboard clipboard)
		{
			this.audioCollection = audioCollection;
			this.clipboard = clipboard;
		}

		[HttpGet("audios")]
		[ProducesResponseType(typeof(IEnumerable<AudioObjInfo>), 200)]
		[ProducesResponseType(typeof(ProblemDetails), 500)]
		public async Task<ActionResult<IEnumerable<AudioObjInfo>>> GetAudios()
		{
			try
			{
				var infos = await Task.Run(() =>
					this.audioCollection.Tracks.Select(i => new AudioObjInfo(i)).ToArray());
				return this.Ok(infos.Length > 0 ? infos : Array.Empty<AudioObjInfo>());
			}
			catch (Exception ex)
			{
				return this.StatusCode(500, new ProblemDetails
				{
					Title = "Get Audios Error",
					Detail = ex.Message,
					Status = 500
				});
			}
		}

		[HttpGet("audios/{guid}/info")]
		[ProducesResponseType(typeof(AudioObjInfo), 200)]
		[ProducesResponseType(typeof(ProblemDetails), 500)]
		public async Task<ActionResult<AudioObjInfo>> GetAudioInfo(Guid guid)
		{
			try
			{
				var obj = this.audioCollection[guid];
				return this.Ok(obj != null ? new AudioObjInfo(obj) : new AudioObjInfo());
			}
			catch (Exception ex)
			{
				return this.StatusCode(500, new ProblemDetails
				{
					Title = "Audio Info Error",
					Detail = ex.Message,
					Status = 500
				});
			}
			finally
			{
				await Task.Yield();
			}
		}

		[HttpDelete("audios/{guid}/remove")]
		[ProducesResponseType(typeof(bool), 200)]
		[ProducesResponseType(typeof(ProblemDetails), 404)]
		[ProducesResponseType(typeof(ProblemDetails), 400)]
		[ProducesResponseType(typeof(ProblemDetails), 500)]
		public async Task<ActionResult<bool>> RemoveAudio(Guid guid)
		{
			try
			{
				var obj = this.audioCollection[guid];
				if (obj == null)
				{
					return this.NotFound(new ProblemDetails
					{
						Title = "Audio Not Found",
						Detail = $"No audio found with Guid '{guid}'",
						Status = 404
					});
				}

				await Task.Run(() =>
					this.audioCollection.RemoveTrackAt(this.audioCollection.Tracks.ToList().IndexOf(obj)));

				var result = this.audioCollection[guid] == null;
				if (!result)
				{
					return this.BadRequest(new ProblemDetails
					{
						Title = "Remove Failed",
						Detail = $"Couldn't remove audio with Guid '{guid}'",
						Status = 400
					});
				}

				return this.Ok(result);
			}
			catch (Exception ex)
			{
				return this.StatusCode(500, new ProblemDetails
				{
					Title = "Remove Audio Error",
					Detail = ex.Message,
					Status = 500
				});
			}
		}

		[HttpPost("audios/upload")]
		[RequestSizeLimit(512_000_000)]
		[ProducesResponseType(typeof(AudioObjInfo), 201)]
		[ProducesResponseType(typeof(ProblemDetails), 400)]
		[ProducesResponseType(typeof(ProblemDetails), 404)]
		[ProducesResponseType(typeof(ProblemDetails), 500)]
		public async Task<ActionResult<AudioObjInfo>> UploadAudio(IFormFile file, bool copyGuid = true)
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
			var tempDir = Path.Combine(Path.GetTempPath(), "audio_uploads");
			Directory.CreateDirectory(tempDir);
			var tempPath = Path.Combine(tempDir, Path.GetFileName(file.FileName));

			try
			{
				await using (var stream = System.IO.File.Create(tempPath))
				{
					await file.CopyToAsync(stream);
				}

				var obj = await this.audioCollection.AddTrack(tempPath);
				if (obj == null)
				{
					return this.BadRequest(new ProblemDetails
					{
						Title = "Upload Failed",
						Detail = "Failed to load audio from uploaded file",
						Status = 400
					});
				}

				obj.Name ??= Path.GetFileNameWithoutExtension(file.FileName);
				obj.Filepath = file.FileName;

				var info = new AudioObjInfo(obj);
				if (info.Id == Guid.Empty)
				{
					obj.Dispose();
					return this.NotFound(new ProblemDetails
					{
						Title = "Upload Info Missing",
						Detail = "Failed to retrieve audio information after upload",
						Status = 404
					});
				}

				if (copyGuid) this.clipboard.SetText(info.Id.ToString());
				return this.Created($"api/audio/audios/{info.Id}/info", info);
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

		[HttpGet("audios/{guid}/download")]
		[ProducesResponseType(typeof(FileResult), 200)]
		[ProducesResponseType(typeof(ProblemDetails), 404)]
		[ProducesResponseType(typeof(ProblemDetails), 400)]
		[ProducesResponseType(typeof(ProblemDetails), 500)]
		public async Task<IActionResult> DownloadAudio(Guid guid)
		{
			try
			{
				var obj = await Task.Run(() => this.audioCollection[guid]);
				if (obj == null)
				{
					return this.NotFound(new ProblemDetails
					{
						Title = "Audio Not Found",
						Detail = $"No audio found with Guid '{guid}'",
						Status = 404
					});
				}

				if (obj.Data.LongLength <= 0)
				{
					return this.BadRequest(new ProblemDetails
					{
						Title = "Empty Audio",
						Detail = "Audio contains no data",
						Status = 400
					});
				}

				var format = obj.Filepath?.Split('.').Last() ?? "wav";
				var filePath = await Task.Run(() => obj.Export());
				if (string.IsNullOrEmpty(filePath))
				{
					return this.BadRequest(new ProblemDetails
					{
						Title = "Export Failed",
						Detail = "Failed to export audio to file",
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

		[HttpGet("audios/{guid}/waveform64")]
		[ProducesResponseType(typeof(AudioData), 200)]
		[ProducesResponseType(typeof(ProblemDetails), 404)]
		[ProducesResponseType(typeof(ProblemDetails), 500)]
		public async Task<ActionResult<AudioData>> GetBase64Waveform(Guid guid)
		{
			try
			{
				var obj = this.audioCollection[guid];
				if (obj == null || obj.Id == Guid.Empty)
				{
					return this.NotFound(new ProblemDetails
					{
						Title = "Audio Not Found",
						Detail = $"No audio found with Guid '{guid}'",
						Status = 404
					});
				}

				var data = await Task.Run(() => new AudioData(obj));
				if (string.IsNullOrEmpty(data.Base64Image))
				{
					return this.Ok(new AudioData());
				}

				return this.Ok(data);
			}
			catch (Exception ex)
			{
				return this.StatusCode(500, new ProblemDetails
				{
					Title = "Waveform Error",
					Detail = ex.Message,
					Status = 500
				});
			}
		}

		[HttpPost("audios/{guid}/play/{volume}")]
		[ProducesResponseType(typeof(ProblemDetails), 404)]
		[ProducesResponseType(typeof(ProblemDetails), 500)]
		public async Task<IActionResult> PlayAudio(Guid guid, float volume = 0.66f)
		{
			try
			{
				volume = Math.Clamp(volume, 0.0f, 1.0f);
				var obj = await Task.Run(() => this.audioCollection[guid]);

				if (obj == null || obj.Id == Guid.Empty)
				{
					return this.NotFound(new ProblemDetails
					{
						Title = "Audio Not Found",
						Detail = $"No audio found with Guid '{guid}'",
						Status = 404
					});
				}

				await Task.Run(() => obj.Play(this.HttpContext.RequestAborted, null, volume));
				return this.Ok();
			}
			catch (Exception ex)
			{
				return this.StatusCode(500, new ProblemDetails
				{
					Title = "Play Error",
					Detail = ex.Message,
					Status = 500
				});
			}
		}

		[HttpPost("audios/{guid}/stop")]
		[ProducesResponseType(typeof(AudioObjInfo), 200)]
		[ProducesResponseType(typeof(ProblemDetails), 404)]
		[ProducesResponseType(typeof(ProblemDetails), 400)]
		[ProducesResponseType(typeof(ProblemDetails), 500)]
		public async Task<ActionResult<AudioObjInfo>> StopAudio(Guid guid)
		{
			try
			{
				var obj = this.audioCollection[guid];
				if (obj == null || obj.Id == Guid.Empty)
				{
					return this.NotFound(new ProblemDetails
					{
						Title = "Audio Not Found",
						Detail = $"No audio found with Guid '{guid}'",
						Status = 404
					});
				}

				obj.Stop();
				var info = await Task.Run(() => new AudioObjInfo(obj));

				if (info.Playing)
				{
					return this.BadRequest(new ProblemDetails
					{
						Title = "Stop Failed",
						Detail = $"Audio with Guid '{guid}' is still playing",
						Status = 400
					});
				}

				return this.Ok(info);
			}
			catch (Exception ex)
			{
				return this.StatusCode(500, new ProblemDetails
				{
					Title = "Stop Error",
					Detail = ex.Message,
					Status = 500
				});
			}
		}

		[HttpPost("audios/stopAll")]
		[ProducesResponseType(typeof(int), 200)]
		[ProducesResponseType(typeof(ProblemDetails), 400)]
		[ProducesResponseType(typeof(ProblemDetails), 500)]
		public async Task<ActionResult<int>> StopAllAudio()
		{
			try
			{
				var result = await this.audioCollection.StopAll();
				if (result < 0)
				{
					return this.BadRequest(new ProblemDetails
					{
						Title = "Stop Failed",
						Detail = "Some tracks are still playing",
						Status = 400
					});
				}

				return this.Ok(result);
			}
			catch (Exception ex)
			{
				return this.StatusCode(500, new ProblemDetails
				{
					Title = "Stop All Error",
					Detail = ex.Message,
					Status = 500
				});
			}
		}




	}
}
