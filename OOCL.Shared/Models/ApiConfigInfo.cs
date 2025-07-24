using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OOCL.Shared
{
	public class ApiConfigInfo
	{
		public string ServerName { get; set; } = "N/A";
		public string ServerProtocol { get; set; } = "none";
		public int ServerPort { get; set; } = 0;
		public string ServerUrl { get; set; } = "https://localhost";
		public string FQDN { get; set; } = "localhost";
		public string FQDN_fallback { get; set; } = "localhost";
		public string ServerVersion { get; set; } = "0.0.0";
		public string ServerDescription { get; set; } = "OOCL API";
		public int InitializeDeviceId { get; set; } = -1;
		public string DefaultDeviceName { get; set; } = "CPU";

		[System.Text.Json.Serialization.JsonConstructor]
		public ApiConfigInfo(
			string serverName,
			string serverProtocol,
			int serverPort,
			string serverUrl,
			string fqdn,
			string fqdnFallback,
			string serverVersion,
			string serverDescription,
			int initializeDeviceId,
			string defaultDeviceName)
		{
			this.ServerName = serverName;
			this.ServerProtocol = serverProtocol;
			this.ServerPort = serverPort;
			this.ServerUrl = serverUrl;
			this.FQDN = fqdn;
			this.FQDN_fallback = fqdnFallback;
			this.ServerVersion = serverVersion;
			this.ServerDescription = serverDescription;
			this.InitializeDeviceId = initializeDeviceId;
			this.DefaultDeviceName = defaultDeviceName;
		}

		public ApiConfigInfo() { }
	}
}
