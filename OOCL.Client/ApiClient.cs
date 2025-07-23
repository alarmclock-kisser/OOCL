using OOCL.Shared;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OOCL.Client
{
	public class ApiClient
	{
		private InternalClient internalClient;
		private HttpClient httpClient;

		public ApiClient(string baseUrl)
		{
			this.httpClient = new HttpClient { BaseAddress = new Uri(baseUrl) };
			this.internalClient = new InternalClient(baseUrl, this.httpClient);
		}

		public ApiClient(HttpClient httpClient)
		{
			string baseUrl = httpClient.BaseAddress?.ToString() ?? "NO BASE URL AVAILABLE (!)";
			this.httpClient = httpClient;
			this.internalClient = new InternalClient(baseUrl, this.httpClient);
		}

		// OpenCL-Controls
		public async Task<OpenClServiceInfo> GetOpenClServiceInfo()
		{
			var task = this.internalClient.StatusAsync();

			try
			{
				return await task;
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Error retrieving OpenCL service info: {ex.Message}");
				return new OpenClServiceInfo();
			}
		}

		public async Task<ICollection<OpenClDeviceInfo>> GetOpenClDeviceInfos()
		{
			var task = this.internalClient.DevicesAsync();

			try
			{
				return await task;
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Error retrieving OpenCL devices: {ex.Message}");
				return new List<OpenClDeviceInfo>();
			}
		}

		public async Task<OpenClServiceInfo> InitializeOpenCl(int index = -1, string deviceName = "")
		{
			// Get index by name
			if (index < 0)
			{
				index = index < 0 ? (await this.internalClient.DevicesAsync()).FirstOrDefault(d => d.DeviceName.ToLower().Contains(deviceName.ToLower()))?.DeviceId ?? -1 : index;
			}

			// Initialize OpenCL service with index
			var task =  this.internalClient.InitializeAsync(index);

			try
			{
				return await task;
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Error initializing OpenCL service: {ex.Message}");
				return new OpenClServiceInfo();
			}
		}

		public async Task<OpenClServiceInfo> DisposeOpenCl()
		{
			var task = this.internalClient.DisposeAsync();

			try
			{
				return await task;
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Error disposing OpenCL service: {ex.Message}");
				return new OpenClServiceInfo();
			}
		}

		public async Task<OpenClUsageInfo> GetOpenClUsageInfo()
		{
			var task = this.internalClient.UsageAsync();

			try
			{
				return await task;
			}
			catch (Exception exception)
			{
				Console.WriteLine(exception);
				return new OpenClUsageInfo();
			}
		}

		public async Task<ICollection<OpenClMemoryInfo>> GetOpenClMemoryInfos()
		{
			var task = this.internalClient.MemoryAsync();

			try
			{
				return await task;
			}
			catch (Exception exception)
			{
				Console.WriteLine(exception);
				return [];
			}
		}

		public async Task<(bool Success, string Message, ICollection<OpenClKernelInfo> Kernels)> GetOpenClKernelInfos(string filter = "")
		{
			try
			{
				var response = await internalClient.KernelsAsync(filter);
				return (true, string.Empty, response);
			}
			catch (ApiException ex) when (ex.StatusCode == 404)
			{
				return (false, "Kernel service not available", []);
			}
			catch (Exception ex)
			{
				Console.WriteLine(ex);
				return (false, ex.Message, []);
			}
		}

		// Image-Controls
		public async Task<ICollection<ImageObjInfo>> GetImageInfos()
		{
			var task = this.internalClient.ImagesAsync();

			try
			{
				return await task;
			}
			catch (Exception exception)
			{
				Console.WriteLine(exception);
				return [];
			}
		}

		public async Task<ImageObjInfo> GetImageInfo(Guid guid)
		{
			var task = this.internalClient.Info2Async(guid);

			try
			{
				return await task;
			}
			catch (Exception exception)
			{
				Console.WriteLine(exception);
				return new ImageObjInfo();
			}
		}

		public async Task<bool> RemoveImage(Guid guid)
		{
			var task = this.internalClient.Remove2Async(guid);

			try
			{
				return await task;
			}
			catch (Exception exception)
			{
				Console.WriteLine(exception);
				return false;
			}
		}

		public async Task<ImageObjInfo> AddEmptyImage(int width = 720, int height = 480)
		{
			var task = this.internalClient.EmptyAsync(width, height);

			try
			{
				return await task;
			}
			catch (Exception exception)
			{
				Console.WriteLine(exception);
				return new ImageObjInfo();
			}
		}

		public async Task<ImageObjInfo> UploadImage(FileParameter file, bool copyGuid = false)
		{
			var task = this.internalClient.Upload2Async(copyGuid, file);

			try
			{
				return await task;
			}
			catch (Exception exception)
			{
				Console.WriteLine(exception);
				return new ImageObjInfo();
			}
		}

		public async Task<ImageData> GetBase64Image(Guid guid, string format = "bmp")
		{
			var task = this.internalClient.Image64Async(guid, format);

			try
			{
				return await task;
			}
			catch (Exception ex)
			{
				Console.WriteLine(ex);
				return new ImageData();
			}
		}

		// Audio-Controls
		public async Task<ICollection<AudioObjInfo>> GetAudioInfos()
		{
			var task = this.internalClient.AudiosAsync();

			try
			{
				return await task;
			}
			catch (Exception exception)
			{
				Console.WriteLine(exception);
				return [];
			}
		}

		public async Task<AudioObjInfo> GetAudioInfo(Guid guid)
		{
			var task = this.internalClient.InfoAsync(guid);

			try
			{
				return await task;
			}
			catch (Exception exception)
			{
				Console.WriteLine(exception);
				return new AudioObjInfo(null);
			}
		}

		public async Task<bool> RemoveAudio(Guid guid)
		{
			var task = this.internalClient.RemoveAsync(guid);

			try
			{
				return await task;
			}
			catch (Exception exception)
			{
				Console.WriteLine(exception);
				return false;
			}
		}

		public async Task<AudioObjInfo> UploadAudio(FileParameter file, bool copyGuid = false)
		{
			Stopwatch sw = Stopwatch.StartNew();
			var info = new AudioObjInfo(null);
			var task = this.internalClient.UploadAsync(copyGuid, file);

			try
			{
				info = await task;
			}
			catch (Exception exception)
			{
				Console.WriteLine(exception);
				return new AudioObjInfo(null);
			}
			finally
			{
				sw.Stop();
				info.LastLoadingTime = sw.Elapsed.TotalSeconds;
			}

			return info;
		}

		public async Task<AudioData> GetBase64Waveform(Guid guid, string format = "png")
		{
			var task = this.internalClient.Waveform64Async(guid);

			try
			{
				return await task;
			}
			catch (Exception ex)
			{
				Console.WriteLine(ex);
				return new AudioData();
			}
		}

		// Kernel Tasks
		public async Task<ImageObjInfo> ExecuteMandelbrot(string kernel = "mandelbrotPrecise", string version = "01",
			int width = 480, int height = 360, double zoom = 1.0, double x = 0.0, double y = 0.0, int coeff = 8,
			int r = 0, int g = 0, int b = 0, bool copyGuid = true, bool allowTempSession = true)
		{
			Stopwatch sw = Stopwatch.StartNew();

			var task = this.internalClient.ExecuteMandelbrotAsync(kernel, version, width, height, zoom, x, y, coeff, r, g, b, copyGuid, allowTempSession);
			var info = new ImageObjInfo();

			try
			{
				info = await task;
				info.LastProcessingTime = sw.Elapsed.TotalSeconds;
			}
			catch (Exception ex)
			{
				info.ErrorMessage = $"'{ex.Message}' ({ex.InnerException?.Message})";
				Console.WriteLine(ex);
			}
			finally
			{
				sw.Stop();
			}

			return info;
		}

		public async Task<AudioObjInfo> ExecuteTimestretch(Guid guid, string kernel = "timestretch_double",
			string version = "03", double factor = 0.8, int chunkSize = 16384, float overlap = 0.5f,
			bool copyGuid = true, bool allowTempSession = true)
		{
			Stopwatch sw = Stopwatch.StartNew();

			var info = await this.GetAudioInfo(guid);
			var task = this.internalClient.ExecuteTimestretchAsync(info.Id, kernel, version, factor, chunkSize, overlap, copyGuid, allowTempSession);

			try
			{
				info = await task;
				info.LastProcessingTime = sw.Elapsed.TotalSeconds;
			}
			catch (Exception ex)
			{
				info.ErrorMessage = $"'{ex.Message}' ({ex.InnerException?.Message})";
				Console.WriteLine(ex);
			}
			finally
			{
				sw.Stop();
			}

			return info;
		}


	}
}
