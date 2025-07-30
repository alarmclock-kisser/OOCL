using System.Diagnostics;
using OOCL.Core;
using OOCL.OpenCl;
using OOCL.Shared;
using Microsoft.AspNetCore.Mvc;
using TextCopy;
using OOCL.Shared.Models;

namespace OOCL.Api.Controllers
{
	[ApiController]
    [Route("api/[controller]")]
    public class OpenClController : ControllerBase
    {
        private readonly OpenClService openClService;
        private readonly ImageCollection imageCollection;
        private readonly AudioCollection audioCollection;
        private readonly IClipboard clipboard;
		private readonly RollingFileLogger logger;

		// ApiConfig pipethrough
		public ApiConfig Config { get; }



		private Task<OpenClServiceInfo> statusInfoTask =>
            Task.Run(() => new OpenClServiceInfo(this.openClService));

        private Task<IEnumerable<OpenClDeviceInfo>> deviceInfosTask =>
            Task.Run(() =>
            {
                return this.openClService.GetDevices().Select((device, index) => new OpenClDeviceInfo(this.openClService, index));
            });

		public string sizeMagnitude { get; set; } = "MB";
		private Task<OpenClUsageInfo> usageInfoTask =>
            Task.Run(() => new OpenClUsageInfo(this.openClService.MemoryRegister, this.sizeMagnitude));

        private Task<IEnumerable<OpenClMemoryInfo>> memoryInfosTask =>
			Task.Run(() =>
			{
                return this.openClService.MemoryRegister?.Memory
                    .Select((memory, index) => new OpenClMemoryInfo(memory)) ?? [];
			});

        private string kernelFilter { get; set; } = string.Empty;
		private Task<IEnumerable<OpenClKernelInfo>> kernelInfosTask =>
            Task.Run(() =>
            {
                return this.openClService.KernelCompiler?.KernelFiles
                    .Select((file, index) => new OpenClKernelInfo(this.openClService.KernelCompiler, index))
                    .Where(i => i.Filepath.ToLower().Contains(this.kernelFilter.ToLower()))?? [];
            });

		public bool FlagReadable { get; set; } = false;


		public OpenClController(OpenClService openClService, ImageCollection imageCollection, AudioCollection audioCollection, IClipboard clipboard, ApiConfig apiConfig, RollingFileLogger logger)
        {
			this.logger = logger;
			this.openClService = openClService;
            this.imageCollection = imageCollection;
            this.audioCollection = audioCollection;
            this.clipboard = clipboard;

			this.Config = apiConfig;

			// Apply configuration settings: Initialize device
			this.openClService.Initialize(apiConfig.InitializeDeviceId);
			if (this.openClService.Online == false)
			{
				Console.WriteLine($"Error initializing Config's device by ID: {this.Config.InitializeDeviceId}");
			}

		}

		// Endpoint to reflect the ApiConfig
		[HttpGet("config")]
		[ProducesResponseType(typeof(ApiConfigInfo), 200)]
		[ProducesResponseType(typeof(ApiConfigInfo), 500)]
		public ActionResult<ApiConfig> GetConfig()
		{
			var info = new ApiConfigInfo();
			try
			{
				info = new ApiConfigInfo
				{
					ServerName = this.Config.ServerName,
					ServerProtocol = this.Config.ServerProtocol,
					ServerPort = this.Config.ServerPort,
					ServerUrl = this.Config.ServerUrl,
					FQDN = this.Config.FQDN,
					FQDN_fallback = this.Config.FQDN_fallback,
					ServerVersion = this.Config.ServerVersion,
					ServerDescription = this.Config.ServerDescription,
					InitializeDeviceId = this.Config.InitializeDeviceId,
					DefaultDeviceName = this.Config.DefaultDeviceName
				};
				return this.Ok(info);
			}
			catch (Exception ex)
			{
				Console.WriteLine("No ApiConfig available (!)");

				var error = new ProblemDetails
				{
					Title = "Error retrieving configuration",
					Detail = ex.Message,
					Status = 500
				};

				return this.Ok(info);
			}
		}

		[HttpGet("status")]
		[ProducesResponseType(typeof(OpenClServiceInfo), 200)]
		[ProducesResponseType(typeof(ProblemDetails), 500)]
		public async Task<ActionResult<OpenClServiceInfo>> GetStatus()
		{
			try
			{
				return this.Ok(await this.statusInfoTask);
			}
			catch (Exception ex)
			{
				return this.StatusCode(500, new ProblemDetails
				{
					Title = "Error getting OpenCL status",
					Detail = ex.Message,
					Status = 500
				});
			}
		}

		[HttpGet("devices")]
		[ProducesResponseType(typeof(IEnumerable<OpenClDeviceInfo>), 200)]
		[ProducesResponseType(typeof(OpenClDeviceInfo[]), 204)]
		[ProducesResponseType(typeof(ProblemDetails), 500)]
		public async Task<ActionResult<IEnumerable<OpenClDeviceInfo>>> GetDevices()
		{
			try
			{
				var infos = await this.deviceInfosTask;
				return this.Ok(infos.Any() ? infos : Array.Empty<OpenClDeviceInfo>());
			}
			catch (Exception ex)
			{
				return this.StatusCode(500, new ProblemDetails
				{
					Title = "Error getting devices",
					Detail = ex.Message,
					Status = 500
				});
			}
		}

		[HttpPost("initialize/{deviceId}")]
		[ProducesResponseType(typeof(OpenClServiceInfo), 201)]
		[ProducesResponseType(typeof(ProblemDetails), 404)]
		[ProducesResponseType(typeof(ProblemDetails), 400)]
		[ProducesResponseType(typeof(ProblemDetails), 500)]
		public async Task<ActionResult<OpenClServiceInfo>> Initialize(int deviceId = 2)
		{
			int count = this.openClService.DeviceCount;

			if (deviceId < 0 || deviceId >= count)
			{
				return this.NotFound(new ProblemDetails
				{
					Title = "Invalid device ID",
					Detail = $"Invalid device ID (max:{count})",
					Status = 404
				});
			}

			try
			{
				await Task.Run(() => this.openClService.Initialize(deviceId));
				var status = await this.statusInfoTask;

				if (!status.Initialized)
				{
					return this.BadRequest(new ProblemDetails
					{
						Title = "Initialization failed",
						Detail = "OpenCL service could not be initialized. Device might not be available.",
						Status = 400
					});
				}

				return this.Created($"api/opencl/status", status);
			}
			catch (Exception ex)
			{
				return this.StatusCode(500, new ProblemDetails
				{
					Title = "Initialization error",
					Detail = ex.Message,
					Status = 500
				});
			}
		}

		[HttpDelete("dispose")]
		[ProducesResponseType(typeof(OpenClServiceInfo), 200)]
		[ProducesResponseType(typeof(OpenClServiceInfo), 400)]
		[ProducesResponseType(typeof(OpenClServiceInfo), 500)]
		public async Task<ActionResult<OpenClServiceInfo>> Dispose()
		{
			try
			{
				await Task.Run(() => this.openClService.Dispose());

				if ((await this.statusInfoTask).Initialized)
				{
					return this.BadRequest(new ProblemDetails
					{
						Title = "Disposing failed",
						Detail = "OpenCL service could not be disposed since it's still initialized on " + (await this.statusInfoTask).DeviceName.ToUpper(),
						Status = 400
					});
				}

				return this.Ok(await this.statusInfoTask);
			}
			catch (Exception ex)
			{
				return this.StatusCode(500, new ProblemDetails
				{
					Title = "Dispose error",
					Detail = ex.Message,
					Status = 500
				});
			}
		}

		[HttpGet("usage")]
		[ProducesResponseType(typeof(OpenClUsageInfo), 200)]
		[ProducesResponseType(typeof(OpenClUsageInfo), 404)]
		[ProducesResponseType(typeof(ProblemDetails), 500)]
		public async Task<ActionResult<OpenClUsageInfo>> GetUsage(string magnitude = "KB")
		{
			this.sizeMagnitude = magnitude;

			try
			{
				if (this.openClService.MemoryRegister == null)
				{
					return this.Ok(new OpenClUsageInfo());
				}

				return this.Ok(await this.usageInfoTask);
			}
			catch (Exception ex)
			{
				return this.StatusCode(500, new ProblemDetails
				{
					Title = "Error getting usage info",
					Detail = ex.Message,
					Status = 500
				});
			}
		}

		[HttpGet("memory")]
		[ProducesResponseType(typeof(IEnumerable<OpenClMemoryInfo>), 200)]
		[ProducesResponseType(typeof(OpenClMemoryInfo[]), 204)]
		[ProducesResponseType(typeof(OpenClMemoryInfo[]), 404)]
		[ProducesResponseType(typeof(ProblemDetails), 500)]
		public async Task<ActionResult<IEnumerable<OpenClMemoryInfo>>> GetMemoryObjects()
		{
			try
			{
				if (this.openClService.MemoryRegister == null)
				{
					return this.Ok(Array.Empty<OpenClMemoryInfo>());
				}

				var infos = await this.memoryInfosTask;
				return this.Ok(infos.Any() ? infos : Array.Empty<OpenClMemoryInfo>());
			}
			catch (Exception ex)
			{
				return this.StatusCode(500, new ProblemDetails
				{
					Title = "Error getting memory objects",
					Detail = ex.Message,
					Status = 500
				});
			}
		}

		[HttpGet("kernels/{filter?}")]
		public async Task<ActionResult<IEnumerable<OpenClKernelInfo>>> GetKernels(string filter = "")
		{
			if (this.openClService.KernelCompiler == null)
			{
				return this.Ok(Array.Empty<OpenClKernelInfo>());
			}

			this.kernelFilter = filter ?? string.Empty;

			var kernels = await this.kernelInfosTask;
			return this.Ok(kernels.Any() ? kernels : Array.Empty<OpenClKernelInfo>());
		}

		[HttpGet("executeMandelbrot/{kernel?}/{version?}/{width?}/{height?}/{zoom?}/{x?}/{y?}/{coeff?}/{r?}/{g?}/{b?}/{allowTempSession?}")]
		[ProducesResponseType(typeof(ImageObjInfo), 201)]
		[ProducesResponseType(typeof(ProblemDetails), 404)]
		[ProducesResponseType(typeof(ProblemDetails), 400)]
		[ProducesResponseType(typeof(ProblemDetails), 500)]
		public async Task<ActionResult<ImageObjInfo>> ExecuteMandelbrot(
	string kernel = "mandelbrotPrecise",
	string version = "01",
	int width = 1920,
	int height = 1080,
	double zoom = 1.0,
	double x = 0.0,
	double y = 0.0,
	int coeff = 16,
	int r = 0,
	int g = 0,
	int b = 0,
	bool allowTempSession = true)
		{
			bool temp = false;
			Stopwatch sw = Stopwatch.StartNew();

			try
			{
				// Get status
				if (!(await this.statusInfoTask).Initialized)
				{
					if (allowTempSession)
					{
						int count = this.openClService.DeviceCount;
						await Task.Run(() => this.openClService.Initialize(this.Config.DefaultDeviceName));
						temp = true;
					}

					if (!(await this.statusInfoTask).Initialized)
					{
						return this.BadRequest(new ProblemDetails
						{
							Title = "OpenCL Not Initialized",
							Detail = "OpenCL service is not initialized. Please initialize it first and check devices available.",
							Status = 400
						});
					}
				}

				// Create an empty image
				var obj = await Task.Run(() => this.imageCollection.PopEmpty(new(width, height), true));
				if (obj == null || !this.imageCollection.Images.Contains(obj))
				{
					return this.NotFound(new ProblemDetails
					{
						Title = "Image Creation Failed",
						Detail = "Failed to create empty image or couldn't add it to the collection.",
						Status = 404
					});
				}

				// Build variable arguments
				object[] variableArgs = [0, 0, width, height, zoom, x, y, coeff, r, g, b];

				var result = await Task.Run(() =>
					this.openClService.ExecuteImageKernel(obj, kernel, version, variableArgs, true));


				var info = await Task.Run(() => new ImageObjInfo(obj));
				if (!info.OnHost)
				{
					return this.NotFound(new ProblemDetails
					{
						Title = "Execution Failed",
						Detail = "Failed to execute OpenCL kernel or image is not on the host after execution call.",
						Status = 404
					});
				}

				info.LastProcessingTime = sw.ElapsedMilliseconds;

				return this.Created($"api/image/{info.Id}/image64", info);
			}
			catch (Exception ex)
			{
				return this.StatusCode(500, new ProblemDetails
				{
					Title = "Mandelbrot Execution Error",
					Detail = ex.Message,
					Status = 500
				});
			}
			finally
			{
				sw.Stop();
				
				if (temp)
				{
					await Task.Run(() => this.openClService.Dispose());
				}
			}
		}

		[HttpGet("executeTimestretch/{guid}/{factor}/{kernel?}/{version?}/{chunkSize?}/{overlap?}/{allowTempSession?}")]
		[ProducesResponseType(typeof(AudioObjInfo), 201)]
		[ProducesResponseType(typeof(ProblemDetails), 404)]
		[ProducesResponseType(typeof(ProblemDetails), 400)]
		[ProducesResponseType(typeof(ProblemDetails), 500)]
		public async Task<ActionResult<AudioObjInfo>> ExecuteTimestretch(
			Guid guid,
			string kernel = "timestretch_double",
			string version = "03",
			double factor = 0.8,
			int chunkSize = 16384,
			float overlap = 0.5f,
			bool allowTempSession = true)
		{
			bool temp = false;

			try
			{
				var obj = this.audioCollection[guid];
				if (obj == null || obj.Id == Guid.Empty)
				{
					return this.NotFound(new ProblemDetails
					{
						Title = "Audio Not Found",
						Detail = $"No audio object found with Guid '{guid}'",
						Status = 404
					});
				}

				if (!(await this.statusInfoTask).Initialized)
				{
					if (allowTempSession)
					{
						int count = this.openClService.DeviceCount;
						await Task.Run(() => this.openClService.Initialize(this.Config.DefaultDeviceName));
						temp = true;
					}

					if (!(await this.statusInfoTask).Initialized)
					{
						return this.BadRequest(new ProblemDetails
						{
							Title = "OpenCL Not Initialized",
							Detail = "OpenCL service is not initialized. Please initialize it first.",
							Status = 400
						});
					}
				}

				var optionalArguments = new Dictionary<string, object>
				{
					["factor"] = kernel.Contains("double", StringComparison.OrdinalIgnoreCase)
						? (double) factor
						: (float) factor
				};

				Stopwatch sw = Stopwatch.StartNew();
				var result = await this.openClService.ExecuteAudioKernel(
					obj, kernel, version, chunkSize, overlap, optionalArguments, true);
				
				sw.Stop();

				var info = await Task.Run(() => new AudioObjInfo(obj));
				return this.Created($"api/audio/{obj.Id}/info", info);
			}
			catch (Exception ex)
			{
				return this.StatusCode(500, new ProblemDetails
				{
					Title = "Timestretch Execution Error",
					Detail = ex.Message,
					Status = 500
				});
			}
			finally
			{
				if (temp)
				{
					await Task.Run(() => this.openClService.Dispose());
				}
			}
		}

		[HttpPost("moveAudio/{guid}")]
		[ProducesResponseType(typeof(AudioObjInfo), 200)]
		[ProducesResponseType(typeof(ProblemDetails), 404)]
		[ProducesResponseType(typeof(ProblemDetails), 400)]
		[ProducesResponseType(typeof(ProblemDetails), 500)]
		public async Task<ActionResult<AudioObjInfo>> MoveAudio(Guid guid)
		{
			try
			{
				var obj = this.audioCollection[guid];
				if (obj == null || obj.Id == Guid.Empty)
				{
					return this.NotFound(new ProblemDetails
					{
						Title = "Audio Not Found",
						Detail = $"No audio object found with Guid '{guid}'",
						Status = 404
					});
				}

				var wasOnHost = new AudioObjInfo(obj).OnHost;

				if (!(await this.statusInfoTask).Initialized)
				{
					return this.BadRequest(new ProblemDetails
					{
						Title = "OpenCL Not Initialized",
						Detail = "OpenCL service is not initialized. Please initialize it first.",
						Status = 400
					});
				}

				var result = await this.openClService.MoveAudio(obj);
				var info = new AudioObjInfo(obj);

				if (info.OnHost == wasOnHost)
				{
					return this.BadRequest(new ProblemDetails
					{
						Title = "Move Operation Failed",
						Detail = $"Audio object was not moved to the host or device as expected. Now on {(info.OnHost ? "Host" : "OpenCL")}",
						Status = 400
					});
				}

				return this.Ok(info);
			}
			catch (Exception ex)
			{
				return this.StatusCode(500, new ProblemDetails
				{
					Title = "Audio Move Error",
					Detail = ex.Message,
					Status = 500
				});
			}
		}

		[HttpPost("moveImage/{guid}")]
		[ProducesResponseType(typeof(ImageObjInfo), 200)]
		[ProducesResponseType(typeof(ProblemDetails), 404)]
		[ProducesResponseType(typeof(ProblemDetails), 400)]
		[ProducesResponseType(typeof(ProblemDetails), 500)]
		public async Task<ActionResult<ImageObjInfo>> MoveImage(Guid guid)
		{
			try
			{
				var obj = this.imageCollection[guid];
				if (obj == null || obj.Id == Guid.Empty)
				{
					return this.NotFound(new ProblemDetails
					{
						Title = "Image Not Found",
						Detail = $"No image object found with Guid '{guid}'",
						Status = 404
					});
				}

				var wasOnHost = new ImageObjInfo(obj).OnHost;

				if (!(await this.statusInfoTask).Initialized)
				{
					return this.BadRequest(new ProblemDetails
					{
						Title = "OpenCL Not Initialized",
						Detail = "OpenCL service is not initialized. Please initialize it first.",
						Status = 400
					});
				}

				var result = await this.openClService.MoveImage(obj);
				var info = new ImageObjInfo(obj);

				if (info.OnHost == wasOnHost)
				{
					return this.BadRequest(new ProblemDetails
					{
						Title = "Move Operation Failed",
						Detail = $"Image object was not moved to the host or device as expected. Now on {(info.OnHost ? "Host" : "OpenCL")}",
						Status = 400
					});
				}

				return this.Ok(info);
			}
			catch (Exception ex)
			{
				return this.StatusCode(500, new ProblemDetails
				{
					Title = "Image Move Error",
					Detail = ex.Message,
					Status = 500
				});
			}
		}

	}
}
