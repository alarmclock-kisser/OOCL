using OOCL.OpenCl;
using OOCL.Shared.Models;
using System;
using System.Text.Json.Serialization;

namespace OOCL.Shared
{
	public class OpenClServiceInfo : OpenClModelBase
	{
		public int DeviceId { get; set; } = -1;
		public string DeviceName { get; set; } = string.Empty;
		public string PlatformName { get; set; } = string.Empty;
		public bool Initialized { get; set; } = false;



		public OpenClServiceInfo() : base()
		{
			// Default constructor for serialization
		}

		[JsonConstructor]
		public OpenClServiceInfo(IOpenClObj? obj) : base(obj)
		{
            if (obj == null)
            {
				return;
            }

			var service = obj as OpenClService;
			if (service == null)
			{
				return;
			}

			this.Initialized = service.MemoryRegister != null && service.KernelCompiler != null && service.KernelExecutioner != null;
			if (!this.Initialized)
			{
				return;
			}

			this.DeviceId = service.Index;
			this.DeviceName = service.GetDeviceInfo() ?? "N/A";
			this.PlatformName = service.GetPlatformInfo() ?? "N/A";
		}
	}
}
