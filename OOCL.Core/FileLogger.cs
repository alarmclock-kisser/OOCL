using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OOCL.Core
{
	public class FileLogger
	{
		public Guid Id { get; set; } = Guid.NewGuid();
		public string ObjectName { get; set; } = string.Empty;
		public string LogPath { get; set; } = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "OOCL.Core", "_Logs"));
		public string FileName => "log_" + (string.IsNullOrEmpty(this.ObjectName) ? this.Id.ToString() : this.ObjectName) + ".TXT";


		public FileLogger(string objectName = "")
		{
			this.ObjectName = objectName;
			if (!Directory.Exists(this.LogPath))
			{
				Directory.CreateDirectory(this.LogPath);
			}

			// Create TXT file (overwrite if exists)
			string filePath = Path.Combine(this.LogPath, this.FileName);
			if (File.Exists(filePath))
			{
				File.Delete(filePath);
			}
			using (File.Create(filePath))
			{
				// Nothing
			}

			// Write initial log entry
			using (StreamWriter writer = new(filePath, true))
			{
				writer.WriteLine($" ~~~ ~~~ ~~~ Log created for '{this.ObjectName}' at [{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")}] ~~~ ~~~ ~~~ \n\n");
			}
		}

		public void Log(string message = "")
		{
			string filePath = Path.Combine(this.LogPath, this.FileName);
			using (StreamWriter writer = new(filePath, true))
			{
				writer.WriteLine($"\n[{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")}] {message}");
			}
		}
	}
}
