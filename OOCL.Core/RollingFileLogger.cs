using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OOCL.Core
{
	public class RollingFileLogger
	{
		public string RootPath => Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".."));


		public int MaxEntries { get; set; } = 16384;
		public string FileExtension { get; set; } = ".LOG";

		public string LoggingDirectory { get; set; } = "_OOCL.Logs";
		private Dictionary<string, List<string>> projectsAndClasses;
		public IEnumerable<string> LogFiles = [];

		public char IndentChar { get; set; } = ' ';
		public int IndentWidth { get; set; } = 2;
		public string Suffix { get; set; } = "\n";
		private string timeStampFormat = "yyyy-MM-dd HH:mm:ss.fff";
		public string TimeStampFormat
		{
			get => timeStampFormat;
			set
			{
				this.timeStampFormat = this.GetFormattedTime(value);
			}
		}



		public RollingFileLogger(bool clearPreviousLogFIles = false)
		{
			if (!Directory.Exists(this.LoggingDirectory))
			{
				Directory.CreateDirectory(this.LoggingDirectory);
			}

			this.projectsAndClasses = this.ReflectProjectsAndClasses();
			this.LogFiles = this.GetLogFiles(clearPreviousLogFIles);
		}



		public string GetFormattedTime(string format)
		{
			try
			{
				// Try-catch to verify format
				return DateTime.Now.ToString(string.IsNullOrWhiteSpace(format) ? "HH:mm:ss.fff" : format);
			}
			catch (FormatException)
			{
				// Fallback to a default
				return DateTime.Now.ToString("HH:mm:ss.fff");
			}
		}

		public Dictionary<string, List<string>> ReflectProjectsAndClasses()
		{
			var projectsAndClasses = new Dictionary<string, List<string>>();
			var assembly = System.Reflection.Assembly.GetExecutingAssembly();

			var types = assembly.GetTypes()
				.Where(t => t.IsClass && !t.IsAbstract && !t.IsInterface)
				.Select(t => new { t.Namespace, t.Name });

			foreach (var type in types)
			{
				if (!string.IsNullOrEmpty(type.Namespace))
				{
					if (!projectsAndClasses.ContainsKey(type.Namespace))
					{
						projectsAndClasses[type.Namespace] = new List<string>();
					}
					projectsAndClasses[type.Namespace].Add(type.Name);
				}
			}

			return projectsAndClasses;
		}

		private List<string> GetLogFiles(bool clearPrevious = false)
		{
			if (clearPrevious)
			{
				var files = Directory.GetFiles(this.LoggingDirectory, ("*" + this.FileExtension)) ;
				foreach (var file in files)
				{
					File.Delete(file);
				}
			}

			// Get (or create) the log files with <AssemblyName>.<ClassName>.LOG format
			var logFiles = new List<string>();
			foreach (var project in this.projectsAndClasses)
			{
				foreach (var className in project.Value)
				{
					var fileName = $"{project.Key}.{className}{this.FileExtension}";
					var fullPath = Path.Combine(this.LoggingDirectory, fileName);
					logFiles.Add(fullPath);
					if (!File.Exists(fullPath))
					{
						File.Create(fullPath).Dispose(); // Ensure the file is created
					}
				}
			}

			return logFiles;
		}

		public async Task Log(Type type, string message = "", string inner = "", int indent = 0)
		{
			var className = type.Name;
			var projectName = type.Namespace ?? "UnknownProject";
			if (!this.projectsAndClasses.ContainsKey(projectName))
			{
				this.projectsAndClasses[projectName] = [];
			}
			if (!this.projectsAndClasses[projectName].Contains(className))
			{
				this.projectsAndClasses[projectName].Add(className);
			}
			var logFilePath = Path.Combine(this.LoggingDirectory, $"{projectName}.{className}{this.FileExtension}");
			var timeStamp = this.GetFormattedTime(this.TimeStampFormat);
			var indentString = new string(this.IndentChar, indent * this.IndentWidth);
			var logMessage = $"[{timeStamp}]: {projectName}.{className} : :  {indentString} '{message.TrimEnd(['.', '!', '?'])}'. {(string.IsNullOrEmpty(inner) ? "" : $"('{inner.TrimEnd(['.', '!', '?'])}'.)")}";
			await File.AppendAllTextAsync(logFilePath, logMessage + Environment.NewLine);

			// Ensure the log file does not exceed MaxLines
			if (this.MaxEntries > 0)
			{
				await this.CheckTruncateOldest(logFilePath).ConfigureAwait(false);
			}
		}

		public async Task CheckTruncateOldest(string logFile)
		{
			if (!File.Exists(logFile))
			{
				// Truncate every file (recursive call)
				var tasks = this.LogFiles.Select(this.CheckTruncateOldest);
				await Task.WhenAll(tasks).ConfigureAwait(false);
				return;
			}

			// Gets entries starting with '[' (timestamp) + count
			var lines = File.ReadLines(logFile);
			var entryLines = lines.Where(l => l.StartsWith('[')).ToList();

			if (entryLines.Count <= this.MaxEntries)
			{
				// Not exceeding maxEntries
				return;
			}

			// Skip the oldest entries when re-writing the file
			var keepLines = entryLines.Skip(entryLines.Count - this.MaxEntries);
			await File.WriteAllLinesAsync(logFile, keepLines);
		}

	}
}
