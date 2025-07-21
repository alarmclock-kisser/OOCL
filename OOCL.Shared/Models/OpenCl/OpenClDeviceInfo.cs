using OOCL.OpenCl;
using OOCL.Shared.Models;
using System.Text.Json.Serialization;

namespace OOCL.Shared
{
	public class OpenClDeviceInfo : OpenClModelBase
	{
		public int DeviceId { get; set; } = -1;
		public string DeviceName { get; set; } = string.Empty;
		public string DeviceType { get; set; } = string.Empty;
		public string PlatformName { get; set; } = string.Empty;



		public OpenClDeviceInfo() : base()
		{
			// Empty default ctor
		}

		[JsonConstructor]
		public OpenClDeviceInfo(IOpenClObj? obj, int index = -1) : base(obj)
		{
            this.DeviceId = index;

            if (obj == null)
            {
                return;
            }

			var service = obj as OpenClService;
			if (service == null)
			{
				return;
			}

			if (index < 0)
            {
                index = service.Index;
                this.DeviceId = index;
            }

			var device = service.GetDevice(index);
			this.DeviceName = service.GetDeviceInfo(device) ?? "N/A";

			var platform = service.GetPlatform(index);
			this.PlatformName = service.GetPlatformInfo(platform) ?? "N/A";

			this.DeviceType = service.GetDeviceType(device);
		}
	}
}
