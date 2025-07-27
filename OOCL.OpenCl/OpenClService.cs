using OOCL.Core;
using OpenTK.Compute.OpenCL;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace OOCL.OpenCl
{
	public class OpenClService : IOpenClObj
	{
		// RollingFileLogger is already available as a DI service (OOCL.Core.RollingFileLogger)
		private RollingFileLogger logger;

		// INTERFACE
		public string Name
		{
			get
			{
				if (this.DEV == null || this.PLAT == null)
				{
					return string.Empty;
				}

				return $"{this.GetDeviceInfo(this.DEV, DeviceInfo.Name)} ({this.GetPlatformInfo(this.PLAT, PlatformInfo.Name)})";
			}
		}
		public string Type => "OpenCL-Service";
		public bool Online => this.CTX != null && this.DEV != null && this.PLAT != null && this.Index >= 0;
		public string Status => this.GetStatus();
		public string LastErrorMessage => this.lastError == CLResultCode.Success ? string.Empty : this.lastError.ToString();
		public IEnumerable<string> ErrorMessages { get; private set; } = [];
		// -----

		public string Repopath => Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory));


		public OpenClService(RollingFileLogger logger)
		{
			this.logger = logger;

			// Set logging true
			this.EnableLogging = true;
		}

		private Dictionary<CLDevice, CLPlatform> Devices => this.GetDevices();
		public int DeviceCount => this.Devices.Count;
		public IEnumerable<string> DeviceNames => this.Devices.Keys.Select(d => this.GetDeviceInfo(d, DeviceInfo.Name) ?? "Unknown Device");
		public IEnumerable<string> PlatformNames => this.Devices.Values.Select(p => this.GetPlatformInfo(p, PlatformInfo.Name) ?? "Unknown Platform");

		public int Index { get; set; } = -1;
		private CLDevice? DEV { get; set; } = null;
		private CLPlatform? PLAT { get; set; } = null;
		private CLContext? CTX { get; set; } = null;

		private CLResultCode _lastError;
		private CLResultCode lastError
		{
			get => this._lastError;
			set
			{
				this._lastError = value;
				if (value != CLResultCode.Success)
				{
					this.ErrorMessages = this.ErrorMessages.Append(value.ToString()).ToList();
					this.Log("Error: " + value.ToString(), "", 1);
				}
			}
		}

		// Event for UI updates
		private event Action? OnChange;

		// Log4net path
		private bool enableLogging = true;
		public bool EnableLogging
		{
			get => this.enableLogging;
			set
			{
				this.enableLogging = value;

				// Set every objects EnableLogging property
				if (this.MemoryRegister != null)
				{
					this.MemoryRegister.EnableLogging = value;
				}
				if (this.KernelCompiler != null)
				{
					this.KernelCompiler.EnableLogging = value;
				}
				if (this.KernelExecutioner != null)
				{
					this.KernelExecutioner.EnableLogging = value;
				}
			}
		}
		



		// Current information
		public Dictionary<string, object> CurrentInfo => this.GetCurrentInfoAsync().Result;
		public Dictionary<string, string> Names => this.GetNames();
		public List<ClMem> MemoryObjects => this.GetMemoryObjectsAsync().Result;
		public Dictionary<string, IntPtr> MemoryStats => this.GetMemoryStatsAsync().Result;
		public float MemoryUsagePercentage =>  this.GetMemoryUsagePercentageAsync().Result;


		// Objects
		public OpenClMemoryRegister? MemoryRegister { get; private set; }
		public OpenClKernelCompiler? KernelCompiler { get; private set; }
		public OpenClKernelExecutioner? KernelExecutioner { get; private set; }


		// Options
		public string PreferredDeviceName { get; set; } = string.Empty;

		// Status
		public string GetStatus()
		{
			string status = string.Empty;

			if (this.DEV == null || this.PLAT == null || !this.CTX.HasValue)
			{
				status = "Not initialized";
			}
			else
			{
				status = $"Initialized: {this.GetDeviceInfo(this.DEV, DeviceInfo.Name)} ({this.GetPlatformInfo(this.PLAT, PlatformInfo.Name)}) [{this.Index}]";
			}

			if (this.lastError != CLResultCode.Success)
			{
				status += $" | Last Error: {this.lastError}";
			}

			return status;
		}

		// Dispose
		public void Dispose(bool silent = false)
		{
			// Dispose context
			if (this.CTX != null)
			{
				CL.ReleaseContext(this.CTX.Value);
				this.PLAT = null;
				this.DEV = null;
				this.CTX = null;
			}

			// Dispose memory handling
			this.MemoryRegister?.Dispose();
			this.MemoryRegister = null; // Clear reference

			// Dispose kernel handling
			this.KernelExecutioner?.Dispose();
			this.KernelExecutioner = null; // Clear reference
			this.KernelCompiler?.Dispose();
			this.KernelCompiler = null; // Clear reference

			// Log
			if (this.EnableLogging)
			{
				this.Log("Disposed OpenCL context and resources.");
			}

            this.OnChange?.Invoke();
		}

		public void Log(string message = "", string inner = "", int indent = 0)
		{
			string msg = "[Service]: " + new string(' ', indent * 2) + message;

			if (!string.IsNullOrEmpty(inner))
			{
				msg += " (" + inner + ")";
			}

			// CW + log4net
			Console.WriteLine(msg);

			if (!this.EnableLogging)
			{
				return; // Logging is disabled
			}

			// Log to RollingFileLogger
			this.logger.Log(this.GetType(), msg, inner, indent).ConfigureAwait(false).GetAwaiter().GetResult();
		}




		// GET Devices & Platforms
		private CLPlatform[] GetPlatforms()
		{
			CLPlatform[] platforms = [];

			try
			{
				this.lastError = CL.GetPlatformIds(out platforms);
				if (this.lastError != CLResultCode.Success)
				{
					this.Log($"Error retrieving OpenCL platforms: {this.lastError}");
				}
			}
			catch (Exception ex)
			{
				this.Log($"Error retrieving OpenCL platforms: {ex.Message}");
			}

			return platforms;
		}

		public Dictionary<CLDevice, CLPlatform> GetDevices()
		{
			Dictionary<CLDevice, CLPlatform> devices = [];

			CLPlatform[] platforms = this.GetPlatforms();
			foreach (CLPlatform platform in platforms)
			{
				try
				{
					CLDevice[] platformDevices = [];
					this.lastError = CL.GetDeviceIds(platform, DeviceType.All, out platformDevices);
					if (this.lastError != CLResultCode.Success)
					{
						this.Log($"Error retrieving devices for platform {this.GetPlatformInfo<string>(platform)}: {this.lastError}");
						continue;
					}
					foreach (CLDevice device in platformDevices)
					{
						devices.Add(device, platform);
					}
				}
				catch (Exception ex)
				{
					this.Log($"Error retrieving devices for platform {this.GetPlatformInfo<string>(platform)}: {ex.Message}");
				}
			}
			return devices;
		}




		// Device & Platform info
		public CLDevice? GetDevice(int index = -1)
		{
			// Get all OpenCL devices & platforms
			Dictionary<CLDevice, CLPlatform> devicesPlatforms = this.Devices;
			
			// Check if index is valid
			if (index < 0 || index >= devicesPlatforms.Count)
			{
				this.Log($"Invalid device index: {index}. Valid range is 0 to {devicesPlatforms.Count - 1}.");

				return null;
			}

			// Get device by index
			return devicesPlatforms.Keys.ElementAt(index);
		}
		public CLPlatform? GetPlatform(int index = -1)
		{
			// Get all OpenCL devices & platforms
			Dictionary<CLDevice, CLPlatform> devicesPlatforms = this.Devices;
			
			// Check if index is valid
			if (index < 0 || index >= devicesPlatforms.Count)
			{
				this.Log($"Invalid platform index: {index}. Valid range is 0 to {devicesPlatforms.Count - 1}.");

				return null;
			}
			
			// Get platform by index
			return devicesPlatforms.Values.ElementAt(index);
		}

		public string GetDeviceType(CLDevice? device = null)
		{
			// Verify device
			device ??= this.DEV;
			if (device == null)
			{
				this.Log("No OpenCL device specified or currently initialized.");
				return "Unknown";
			}

			string devType = this.GetDeviceInfo(device, DeviceInfo.Type) ?? "Unknown";

			// TODO: Find out byte strings for each device type. Now it defaults to unknown
			string typeName = devType switch
			{
				"CL_DEVICE_TYPE_CPU" => "CPU",
				"CL_DEVICE_TYPE_GPU" => "GPU",
				"CL_DEVICE_TYPE_ACCELERATOR" => "Accelerator",
				"CL_DEVICE_TYPE_DEFAULT" => "Default",
				_ => "Unknown"
			};

			return typeName;
		}

		public string? GetDeviceInfo(CLDevice? device = null, DeviceInfo info = DeviceInfo.Name)
		{
			// Verify device
			device ??= this.DEV;
			if (device == null)
			{
				this.Log("No OpenCL device specified or currently initialized.");

				return null;
			}

			this.lastError = CL.GetDeviceInfo(device.Value, info, out byte[] infoCode);
			if (this.lastError != CLResultCode.Success || infoCode == null || infoCode.LongLength == 0)
			{
				this.Log($"Failed to get device info for '{info}': {this.lastError}. {(infoCode == null || infoCode.LongLength == 0 ? "No data returned." : "")}");

				return null;
			}

			return Encoding.UTF8.GetString(infoCode).Trim('\0');
		}

		public string? GetPlatformInfo(CLPlatform? platform = null, PlatformInfo info = PlatformInfo.Name)
		{
			// Verify platform
			platform ??= this.PLAT;
			if (platform == null)
			{
				this.Log("No OpenCL platform specified or currently initialized.");

				return null;
			}

			this.lastError = CL.GetPlatformInfo(platform.Value, info, out byte[] infoCode);
			if (this.lastError != CLResultCode.Success || infoCode == null || infoCode.LongLength == 0)
			{
				this.Log($"Failed to get platform info for '{info}': {this.lastError}. {(infoCode == null || infoCode.LongLength == 0 ? "No data returned." : "")}");

				return null;
			}
			
			return Encoding.UTF8.GetString(infoCode).Trim('\0');
		}

		public T? GetDeviceInfo<T>(CLDevice? device = null, DeviceInfo info = DeviceInfo.Name)
		{
			// Verify device
			device ??= this.DEV;
			if (device == null)
			{
				this.Log("No OpenCL device specified or currently initialized.");

				return default;
			}

			this.lastError = CL.GetDeviceInfo(device.Value, info, out byte[] infoCode);
			if (this.lastError != CLResultCode.Success || infoCode == null || infoCode.LongLength == 0) // infoCode == null hinzugefügt
			{
				this.Log($"Failed to get device info for '{info}': {this.lastError}. {(infoCode == null || infoCode.LongLength == 0 ? "No data returned." : "")}");

				return default;
			}

			// Try-catch dynamic conversion from byte[] to T
			try
			{
				Type targetType = typeof(T);
				dynamic? result = null; // result initialisieren

				// 1. Versuch: Statische FromBytes(byte[]) Methode
				MethodInfo? fromBytesMethod = targetType.GetMethod(
					"FromBytes",
					BindingFlags.Public | BindingFlags.Static,
					null,
					[typeof(byte[])],
					null
				);

				if (fromBytesMethod != null && fromBytesMethod.ReturnType.IsAssignableTo(targetType))
				{
					try
					{
						result = fromBytesMethod.Invoke(null, [infoCode]);
					}
					catch (TargetInvocationException tie)
					{
						this.Log($"Error calling FromBytes on '{targetType.Name}': {tie.InnerException?.Message ?? tie.Message}");

					}
					catch (Exception ex)
					{
						this.Log($"Error during FromBytes invocation for '{targetType.Name}': {ex.Message}");

					}
				}
				else if (fromBytesMethod == null)
				{
					this.Log($"No static public 'FromBytes(byte[])' method found on type '{targetType.Name}'.");
				}
				else if (fromBytesMethod != null)
				{
					this.Log($"Warning: FromBytes method found for '{targetType.Name}' but its return type '{fromBytesMethod.ReturnType.Name}' is not assignable to '{targetType.Name}'.");
				}

				if (result == null) // Wenn FromBytes nicht erfolgreich war oder nicht existiert
				{
					// 2. Versuch: Statische TryParse(byte[], out T) Methode
					MethodInfo? tryParseMethod = targetType.GetMethod(
						"TryParse",
						BindingFlags.Public | BindingFlags.Static,
						null,
						[typeof(byte[]), targetType.MakeByRefType()],
						null
					);

					if (tryParseMethod != null && tryParseMethod.ReturnType == typeof(bool))
					{
						object?[] parameters = [infoCode, null]; // Parameters for TryParse
						try
						{
							bool success = (bool) (tryParseMethod.Invoke(null, parameters) ?? false);
							if (success)
							{
								result = parameters[1]; // Das out-Parameter-Ergebnis
							}
							this.Log($"TryParse method on '{targetType.Name}' returned false.");
						}
						catch (TargetInvocationException tie)
						{
							this.Log($"Error calling TryParse (byte[]) on '{targetType.Name}': {tie.InnerException?.Message ?? tie.Message}");
						}
						catch (Exception ex)
						{
							this.Log($"Error during TryParse (byte[]) invocation for '{targetType.Name}': {ex.Message}");
						}
					}
					else if (tryParseMethod == null)
					{
						this.Log($"No static public 'TryParse(byte[], out T)' method found on type '{targetType.Name}'.");
					}

					if (result == null) // Wenn FromBytes und TryParse(byte[]) nicht erfolgreich waren
					{
						// 3. Versuch: Fallback: Konvertierung der Bytes zu string, dann Parse(string) oder TryParse(string)
						string strValue = Encoding.UTF8.GetString(infoCode).Trim('\0');

						// Spezielle Behandlung für Extensions
						if (info == DeviceInfo.Extensions)
						{
							strValue = string.Join(", ", strValue.Split('\0', StringSplitOptions.RemoveEmptyEntries));
						}

						if (string.IsNullOrEmpty(strValue))
						{
							this.Log("Converted byte array to empty or null string; cannot parse.");

							return default; // Keine Daten zum Parsen
						}

						// Versuch, eine statische Parse(string) oder TryParse(string, out T) Methode zu finden
						MethodInfo? parseMethod = targetType.GetMethods(BindingFlags.Public | BindingFlags.Static)
							.FirstOrDefault(m => m.Name == "Parse" && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(string));

						MethodInfo? tryParseStringMethod = targetType.GetMethod(
							"TryParse",
							BindingFlags.Public | BindingFlags.Static,
							null,
							[typeof(string), targetType.MakeByRefType()],
							null
						);

						if (tryParseStringMethod != null && tryParseStringMethod.ReturnType == typeof(bool))
						{
							object?[] parameters = [strValue, null];
							try
							{
								bool success = (bool) (tryParseStringMethod.Invoke(null, parameters) ?? false);
								if (success)
								{
									result = parameters[1];
								}
								this.Log($"TryParse (string) method on '{targetType.Name}' returned false for string: '{strValue}'.");
							}
							catch (TargetInvocationException tie)
							{
								this.Log($"Error calling TryParse (string) on '{targetType.Name}': {tie.InnerException?.Message ?? tie.Message}");
							}
							catch (Exception ex)
							{
								this.Log($"Error during TryParse (string) invocation for '{targetType.Name}': {ex.Message}");
							}
						}
						else if (parseMethod != null && parseMethod.GetParameters().Length == 1 && parseMethod.GetParameters()[0].ParameterType == typeof(string) && parseMethod.ReturnType.IsAssignableTo(targetType))
						{
							try
							{
								result = parseMethod.Invoke(null, [strValue]);
							}
							catch (TargetInvocationException tie)
							{
								this.Log($"Error calling Parse on '{targetType.Name}': {tie.InnerException?.Message ?? tie.Message}");
							}
							catch (Exception ex)
							{
								this.Log($"Error during Parse invocation for '{targetType.Name}': {ex.Message}");
							}
						}

						this.Log($"No suitable static public 'Parse(string)' or 'TryParse(string, out T)' method found on type '{targetType.Name}'.");
					}
				}

				return (T?) result;
			}
			catch (Exception ex)
			{
				this.Log($"Unhandled error in GetDeviceInfo<T> for '{info}' to type '{typeof(T).Name}': {ex.Message}");

				return default;
			}
		}

		public T? GetPlatformInfo<T>(CLPlatform? platform = null, PlatformInfo info = PlatformInfo.Name)
		{
			// Verify platform
			platform ??= this.PLAT;
			if (platform == null)
			{
				this.Log("No OpenCL platform specified or currently initialized.");

				return default;
			}

			this.lastError = CL.GetPlatformInfo(platform.Value, info, out byte[] infoCode);
			if (this.lastError != CLResultCode.Success || infoCode == null || infoCode.LongLength == 0)
			{
				if (this.EnableLogging)
				{
					this.Log($"Failed to get platform info for '{info}': {this.lastError}. {(infoCode == null || infoCode.LongLength == 0 ? "No data returned." : "")}");
				}
				return default;
			}

			try
			{
				Type targetType = typeof(T);
				dynamic? result = null;

				// 0. Spezielle Behandlung für string und byte[] als direkte Typen (Performance-Optimierung)
				if (targetType == typeof(string))
				{
					string stringResult = Encoding.UTF8.GetString(infoCode).Trim('\0');
					// Spezielle Behandlung für Extensions (Plattform-Extensions funktionieren ähnlich wie Geräte-Extensions)
					if (info == PlatformInfo.Extensions)
					{
						stringResult = string.Join(", ", stringResult.Split('\0', StringSplitOptions.RemoveEmptyEntries));
					}
					return (T) (object) stringResult;
				}
				if (targetType == typeof(byte[]))
				{
					return (T) (object) infoCode;
				}

				// 1. Versuch: Statische FromBytes(byte[]) Methode auf T
				MethodInfo? fromBytesMethod = targetType.GetMethod(
					"FromBytes",
					BindingFlags.Public | BindingFlags.Static,
					null,
					[typeof(byte[])],
					null
				);

				if (fromBytesMethod != null && fromBytesMethod.ReturnType.IsAssignableTo(targetType))
				{
					try
					{
						result = fromBytesMethod.Invoke(null, [infoCode]);
					}
					catch (TargetInvocationException tie)
					{
						if (this.EnableLogging)
						{
							this.Log($"Error calling FromBytes on '{targetType.Name}': {tie.InnerException?.Message ?? tie.Message}");
						}
					}
					catch (Exception ex)
					{
						if (this.EnableLogging)
						{
							this.Log($"Error during FromBytes invocation for '{targetType.Name}': {ex.Message}");
						}
					}
				}
				else if (this.EnableLogging && fromBytesMethod == null)
				{
					this.Log($"No static public 'FromBytes(byte[])' method found on type '{targetType.Name}'.");
				}
				else if (this.EnableLogging && fromBytesMethod != null)
				{
					this.Log($"Warning: FromBytes method found for '{targetType.Name}' but its return type '{fromBytesMethod.ReturnType.Name}' is not assignable to '{targetType.Name}'.");
				}

				if (result == null) // Wenn FromBytes nicht erfolgreich war oder nicht existiert
				{
					// 2. Versuch: Statische TryParse(byte[], out T) Methode auf T
					MethodInfo? tryParseMethod = targetType.GetMethod(
						"TryParse",
						BindingFlags.Public | BindingFlags.Static,
						null,
						[typeof(byte[]), targetType.MakeByRefType()],
						null
					);

					if (tryParseMethod != null && tryParseMethod.ReturnType == typeof(bool))
					{
						object?[] parameters = [infoCode, null]; // Parameters for TryParse
						try
						{
							bool success = (bool) (tryParseMethod.Invoke(null, parameters) ?? false);
							if (success)
							{
								result = parameters[1]; // Das out-Parameter-Ergebnis
							}
							else if (this.EnableLogging)
							{
								this.Log($"TryParse method on '{targetType.Name}' returned false.");
							}
						}
						catch (TargetInvocationException tie)
						{
							if (this.EnableLogging)
							{
								this.Log($"Error calling TryParse (byte[]) on '{targetType.Name}': {tie.InnerException?.Message ?? tie.Message}");
							}
						}
						catch (Exception ex)
						{
							if (this.EnableLogging)
							{
								this.Log($"Error during TryParse (byte[]) invocation for '{targetType.Name}': {ex.Message}");
							}
						}
					}
					else if (this.EnableLogging && tryParseMethod == null)
					{
						this.Log($"No static public 'TryParse(byte[], out T)' method found on type '{targetType.Name}'.");
					}

					if (result == null) // Wenn FromBytes und TryParse(byte[]) nicht erfolgreich waren
					{
						// 3. Versuch: Fallback: Konvertierung der Bytes zu string, dann Parse(string) oder TryParse(string, out T)
						string strValue = Encoding.UTF8.GetString(infoCode).Trim('\0');

						// Spezielle Behandlung für Extensions (falls noch nicht oben bei typeof(string) behandelt)
						if (info == PlatformInfo.Extensions)
						{
							strValue = string.Join(", ", strValue.Split('\0', StringSplitOptions.RemoveEmptyEntries));
						}

						if (string.IsNullOrEmpty(strValue))
						{
							if (this.EnableLogging)
							{
								this.Log("Converted byte array to empty or null string; cannot parse for platform info.");
							}
							return default; // Keine Daten zum Parsen
						}

						// Versuch, eine statische Parse(string) oder TryParse(string, out T) Methode zu finden
						MethodInfo? tryParseStringMethod = targetType.GetMethod(
							"TryParse",
							BindingFlags.Public | BindingFlags.Static,
							null,
							[typeof(string), targetType.MakeByRefType()],
							null
						);

						MethodInfo? parseMethod = targetType.GetMethods(BindingFlags.Public | BindingFlags.Static)
							.FirstOrDefault(m => m.Name == "Parse" && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(string));

						if (tryParseStringMethod != null && tryParseStringMethod.ReturnType == typeof(bool))
						{
							object?[] parameters = [strValue, null];
							try
							{
								bool success = (bool) (tryParseStringMethod.Invoke(null, parameters) ?? false);
								if (success)
								{
									result = parameters[1];
								}
								else if (this.EnableLogging)
								{
									this.Log($"TryParse (string) method on '{targetType.Name}' returned false for string: '{strValue}'.");
								}
							}
							catch (TargetInvocationException tie)
							{
								if (this.EnableLogging)
								{
									this.Log($"Error calling TryParse (string) on '{targetType.Name}': {tie.InnerException?.Message ?? tie.Message}");
								}
							}
							catch (Exception ex)
							{
								if (this.EnableLogging)
								{
									this.Log($"Error during TryParse (string) invocation for '{targetType.Name}': {ex.Message}");
								}
							}
						}
						else if (parseMethod != null && parseMethod.GetParameters().Length == 1 && parseMethod.GetParameters()[0].ParameterType == typeof(string) && parseMethod.ReturnType.IsAssignableTo(targetType))
						{
							try
							{
								result = parseMethod.Invoke(null, [strValue]);
							}
							catch (TargetInvocationException tie)
							{
								if (this.EnableLogging)
								{
									this.Log($"Error calling Parse on '{targetType.Name}': {tie.InnerException?.Message ?? tie.Message}");
								}
							}
							catch (Exception ex)
							{
								if (this.EnableLogging)
								{
									this.Log($"Error during Parse invocation for '{targetType.Name}': {ex.Message}");
								}
							}
						}
						else if (this.EnableLogging)
						{
							this.Log($"No suitable static public 'Parse(string)' or 'TryParse(string, out T)' method found on type '{targetType.Name}'.");

							try
							{
								if (strValue != null)
								{
									result = Convert.ChangeType(strValue, targetType, CultureInfo.InvariantCulture);
								}
							}
							catch (Exception ex)
							{
								if (this.EnableLogging)
								{
									this.Log($"Error during Convert.ChangeType fallback for '{targetType.Name}': {ex.Message}");
								}
							}
						}
					}
				}

				return (T?) result;
			}
			catch (Exception ex)
			{
				if (this.EnableLogging)
				{
					this.Log($"Unhandled error in GetPlatformInfo<T> for '{info}' to type '{typeof(T).Name}': {ex.Message}");
				}
				return default;
			}
		}

		public Dictionary<string, string> GetNames()
		{
			// Get all OpenCL devices & platforms
			Dictionary<CLDevice, CLPlatform> devicesPlatforms = this.Devices;

			// Create dictionary for device names and platform names
			Dictionary<string, string> names = [];

			// Iterate over devices
			foreach (CLDevice device in devicesPlatforms.Keys)
			{
				// Get device name
				string deviceName = this.GetDeviceInfo(device, DeviceInfo.Name) ?? "N/A";

				// Get platform name
				string platformName = this.GetPlatformInfo(devicesPlatforms[device], PlatformInfo.Name) ?? "N/A";

				// Add to dictionary
				names.Add(deviceName, platformName);
			}

			// Return names
			return names;
		}

		public Dictionary<string, object> GetFullDeviceInfo()
		{
			List<object> infoList = [];
			List<string> desc =
				[
					"Name", "Vendor", "Vendor id", "Address Bits", "Global memory size", "Local memory size",
					"Cache memory size",
					"Compute units", "Clock frequency", "Max. buffer size", "OpenCLC version", "Version",
					"Driver version"
				];

			if (this.DEV == null || this.PLAT == null)
			{
				this.Log("No OpenCL device or platform initialized.");
				return [];
			}

			infoList.Add(this.GetDeviceInfo<string>(this.DEV.Value, DeviceInfo.Name) ?? "N/A");
			infoList.Add(this.GetDeviceInfo<string>(this.DEV.Value, DeviceInfo.Vendor) ?? "N/A");
			infoList.Add(this.GetDeviceInfo<int>(this.DEV.Value, DeviceInfo.VendorId));
			infoList.Add(this.GetDeviceInfo<int>(this.DEV.Value, DeviceInfo.AddressBits));
			infoList.Add(this.GetDeviceInfo<long>(this.DEV.Value, DeviceInfo.GlobalMemorySize));
			infoList.Add(this.GetDeviceInfo<long>(this.DEV.Value, DeviceInfo.LocalMemorySize));
			infoList.Add(this.GetDeviceInfo<long>(this.DEV.Value, DeviceInfo.GlobalMemoryCacheSize));
			infoList.Add(this.GetDeviceInfo<int>(this.DEV.Value, DeviceInfo.MaximumComputeUnits));
			infoList.Add(this.GetDeviceInfo<long>(this.DEV.Value, DeviceInfo.MaximumClockFrequency));
			infoList.Add(this.GetDeviceInfo<long>(this.DEV.Value, DeviceInfo.MaximumConstantBufferSize));
			infoList.Add(this.GetDeviceInfo<Version>(this.DEV.Value, DeviceInfo.OpenClCVersion) ?? new());
			infoList.Add(this.GetDeviceInfo<Version>(this.DEV.Value, DeviceInfo.Version) ?? new());
			infoList.Add(this.GetDeviceInfo<Version>(this.DEV.Value, DeviceInfo.DriverVersion) ?? new());

			// Create dictionary with device info
			return desc.Zip(infoList, (key, value) => new KeyValuePair<string, object>(key, value)).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
		}

		public Dictionary<string, object> GetFullPlatformInfo()
		{
			List<object> infoList = [];
			List<string> desc = ["Name", "Vendor", "Version", "Profile", "Extensions"];
			
			if (this.PLAT == null)
			{
				this.Log("No OpenCL platform initialized.");
				return [];
			}
			
			infoList.Add(this.GetPlatformInfo<string>(this.PLAT.Value, PlatformInfo.Name) ?? "N/A");
			infoList.Add(this.GetPlatformInfo<string>(this.PLAT.Value, PlatformInfo.Vendor) ?? "N/A");
			infoList.Add(this.GetPlatformInfo<Version>(this.PLAT.Value, PlatformInfo.Version) ?? new());
			infoList.Add(this.GetPlatformInfo<string>(this.PLAT.Value, PlatformInfo.Profile) ?? "N/A");
			infoList.Add(this.GetPlatformInfo<string>(this.PLAT.Value, PlatformInfo.Extensions) ?? "N/A");
			
			// Create dictionary with platform info
			return desc.Zip(infoList, (key, value) => new KeyValuePair<string, object>(key, value)).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
		}


		// Initialize
		public void Initialize(int index = 0, bool silent = false)
		{
			this.Dispose(true);

			Dictionary<CLDevice, CLPlatform> devicesPlatforms = this.Devices;

			if (index < 0 || index >= devicesPlatforms.Count)
			{
				if (this.EnableLogging)
				{
					this.Log("Invalid index for OpenCL device selection");
				}

                this.OnChange?.Invoke();
				return;
			}

			this.Index = index;
			this.DEV = devicesPlatforms.Keys.ElementAt(index);
			this.PLAT = devicesPlatforms.Values.ElementAt(index);

			this.CTX = CL.CreateContext(0, [this.DEV.Value], 0, IntPtr.Zero, out CLResultCode error);
			if (error != CLResultCode.Success || this.CTX == null)
			{
				this.lastError = error;
				if (this.EnableLogging)
				{
					this.Log($"Failed to create OpenCL context: {this.lastError}");
				}

                this.OnChange?.Invoke();
				return;
			}
			// Assuming CLCommandQueue is created within OpenClMemoryRegister constructor
			this.MemoryRegister = new OpenClMemoryRegister(this.Repopath, this.CTX.Value, this.DEV.Value, this.PLAT.Value, this.logger);
			this.KernelCompiler = new OpenClKernelCompiler(this.Repopath, this.MemoryRegister, this.CTX.Value, this.DEV.Value, this.PLAT.Value, this.MemoryRegister.Queue, this.logger);
			this.KernelExecutioner = new OpenClKernelExecutioner(this.Repopath, this.MemoryRegister, this.CTX.Value, this.DEV.Value, this.PLAT.Value, this.MemoryRegister.Queue, this.KernelCompiler, this.logger);

			this.Index = index;

			if (this.EnableLogging)
			{
				this.Log($"Initialized OpenCL context for device {this.GetDeviceInfo(this.DEV, DeviceInfo.Name) ?? "N/A"} on platform {this.GetPlatformInfo(this.PLAT, PlatformInfo.Name) ?? "N/A"}");
			}

            this.OnChange?.Invoke();
		}

		public void Initialize(string deviceName, bool silent = false)
		{
			// Find index by name or preferred
			List<string> devicesNames = this.Names.Values.ToList();
			int index = -1;
			for (int i = 0; i < devicesNames.Count; i++)
			{
				if (devicesNames[i].Equals(deviceName, StringComparison.OrdinalIgnoreCase) || 
					(this.PreferredDeviceName != string.Empty && devicesNames[i].Equals(this.PreferredDeviceName, StringComparison.OrdinalIgnoreCase)))
				{
					index = i;
					break;
				}
			}

			if (index == -1)
			{
				if (this.EnableLogging)
				{
					this.Log($"Device '{deviceName}' not found or not initialized. Available devices: {string.Join(", ", devicesNames)}");
				}
				return;
			}

			// Initialize by index
			this.Initialize(index, silent);
		}


		// Accessible methods
		public async Task<Dictionary<string, object>> GetCurrentInfoAsync()
		{
			Dictionary<string, object> info = [];

			try
			{
				info = this.GetFullDeviceInfo().ToArray().Concat(this.GetFullPlatformInfo().ToArray())
					.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
			}
			catch (Exception ex)
			{
				this.Log($"Error retrieving current OpenCL info: {ex.Message}");
			}
			finally
			{
				await Task.Yield();
			}

			return info;
		}

		public async Task<List<ClMem>> GetMemoryObjectsAsync()
		{
			if (this.MemoryRegister == null)
			{
				if (this.EnableLogging)
				{
					this.Log("Memory register is not initialized.");
				}
				return [];
			}
			try
			{
				return await Task.Run(() => this.MemoryRegister.Memory.ToList());
			}
			catch (Exception ex)
			{
				this.Log($"Error retrieving memory objects: {ex.Message}");
				return [];
			}
		}

		public async Task<Dictionary<string, IntPtr>> GetMemoryStatsAsync()
		{
			if (this.MemoryRegister == null)
			{
				if (this.EnableLogging)
				{
					this.Log("Memory register is not initialized.");
				}
				return [];
			}
			try
			{
				// Get memory stats
				List<string> keys = ["Total", "Used", "Free"];
				List<IntPtr> values = [(nint) this.MemoryRegister.GetMemoryTotal(), (nint) this.MemoryRegister.GetMemoryUsed(), (nint) this.MemoryRegister.GetMemoryFree()];
				return keys.Zip(values, (key, value) => new KeyValuePair<string, IntPtr>(key, value)).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
			}
			catch (Exception ex)
			{
				this.Log($"Error retrieving memory stats: {ex.Message}");
				return [];
			}
			finally
			{
				await Task.Yield();
			}
		}

		public async Task<float> GetMemoryUsagePercentageAsync()
		{
			if (this.MemoryRegister == null)
			{
				if (this.EnableLogging)
				{
					this.Log("Memory register is not initialized.");
				}
				return 0f;
			}
			try
			{
				// Get memory usage percentage
				var usage = await this.GetMemoryStatsAsync();
				if (usage.TryGetValue("Total", out IntPtr total) && usage.TryGetValue("Used", out IntPtr used) && total != IntPtr.Zero)
				{
					return (float)used.ToInt64() / total.ToInt64();
				}
				else
				{
					if (this.EnableLogging)
					{
						this.Log("Memory stats are not available or invalid.");
					}
					return 0.0f;
				}
			}
			catch (Exception ex)
			{
				this.Log($"Error calculating memory usage percentage: {ex.Message}");
				return 0f;
			}
			finally
			{
				await Task.Yield();
			}
		}

		public async Task<long> GetDeviceScore(CLDevice? device = null)
			{
			// Verify device
			device ??= this.DEV;
			if (device == null)
			{
				if (this.EnableLogging)
				{
					this.Log("No OpenCL device specified or currently initialized.");
				}
				return 0;
			}
			try
			{
				int computeUnits = this.GetDeviceInfo<int>(device, DeviceInfo.MaximumComputeUnits);
				long clockFrequency = this.GetDeviceInfo<long>(device, DeviceInfo.MaximumClockFrequency);

				return computeUnits * clockFrequency;
			}
			catch (Exception ex)
			{
				this.Log($"Error calculating device score: {ex.Message}");
				return -1;
			}
			finally
			{
				await Task.Yield();
			}
		}

		public async Task<int?> GetStrongestDeviceIndexAsync(DeviceType deviceType = DeviceType.All)
		{
			var devices = this.Devices.Keys.ToList();
			if (devices.Count == 0)
			{
				if (this.EnableLogging)
				{
					this.Log("No OpenCL devices found.");
				}
				return null;
			}

			try
			{
				Dictionary<CLDevice, DeviceType> deviceTypes = devices.ToList()
					.ToDictionary(d => d, d => this.GetDeviceInfo<DeviceType>(d, DeviceInfo.Type));

				// Filter devices by type
				List<CLDevice> filteredDevices;
				if (deviceType == DeviceType.All)
				{
					filteredDevices = devices;
				}
				else
				{
					filteredDevices = devices.Where(d => deviceTypes[d] == deviceType).ToList();
				}

				List<long> scores = filteredDevices.Select(d => this.GetDeviceScore(d).Result).ToList();

				if (scores.Count == 0)
				{
					if (this.EnableLogging)
					{
						this.Log($"No devices found for type {deviceType}.");
					}
					return null;
				}

				// Find the index of the device with the highest score
				int strongestIndex = scores.IndexOf(scores.Max());

				return devices.IndexOf(filteredDevices[strongestIndex]);
			}
			catch (Exception ex)
			{
				this.Log($"Error finding strongest device: {ex.Message}");
				return null;
			}
			finally
			{
				await Task.Yield();
			}
		}



		public async Task<IntPtr> MoveImage(ImageObj obj)
		{
			if (this.MemoryRegister == null)
			{
				this.Log("Memory register is not initialized.");
				return IntPtr.Zero;
			}
			try
			{
				// -> Device
				if (obj.OnHost)
				{
					byte[] pixels = (await obj.GetBytes(false)).AsParallel().ToArray();

					var mem = this.MemoryRegister.PushData<byte>(pixels);
					if (mem == null)
					{
						this.Log("Failed to push image data to OpenCL memory.");
						return IntPtr.Zero;
					}

					long memIndexHandle = mem[0].Handle;
					if (memIndexHandle == 0)
					{
						this.Log("Failed to parse memory index handle.");
						return IntPtr.Zero;
					}

					// Set pointer to memory index handle (long)	
					obj.Pointer = memIndexHandle;
				}
				else if (obj.OnDevice)
				{
					byte[] bytes = this.MemoryRegister.PullData<byte>((nint)obj.Pointer);
					
					await obj.SetImage(bytes, false);
				}
			}
			catch (Exception ex)
			{
				this.Log($"Error moving image to OpenCL memory: {ex.Message}");
				return IntPtr.Zero;
			}
			finally
			{
				await Task.Yield();
			}

			return (nint)obj.Pointer;
		}

		public async Task<IntPtr> MoveAudio(AudioObj obj, int chunkSize = 16384, float overlap = 0.5f)
		{
			if (this.MemoryRegister == null)
			{
				this.Log("Memory register is not initialized.");
				return IntPtr.Zero;
			}
			try
			{
				List<float[]> chunks = [];

				// -> Device
				if (obj.OnHost)
				{
					chunks = (await obj.GetChunks(chunkSize, overlap, false)).AsParallel().ToList();
					if (chunks.Count <= 0)
					{
						this.Log("Failed to get audio chunks from AudioObj.");
						return IntPtr.Zero;
					}

					var mem = this.MemoryRegister.PushChunks<float>(chunks);
					if (mem == null)
					{
						this.Log("Failed to push audio chunks to OpenCL memory.");
						return IntPtr.Zero;
					}

					long memIndexHandle = mem[0].Handle;
					if (memIndexHandle == 0)
					{
						this.Log("Failed to parse memory index handle.");
						return IntPtr.Zero;
					}

					obj.Pointer = memIndexHandle;
				}
				else if (obj.OnDevice)
				{
					chunks = this.MemoryRegister.PullChunks<float>((nint)obj.Pointer);

					await obj.AggregateStretchedChunks(chunks, false);
				}
				else
				{
					this.Log("Error: AudioObj is neither on Host nor on Device.");
				}
			}
			catch (Exception ex)
			{
				this.Log($"Error moving audio to OpenCL memory: {ex.Message}");
				return IntPtr.Zero;
			}
			finally
			{
				await Task.Yield();
			}

			return (nint)obj.Pointer;
		}

		public async Task<IntPtr> ExecuteAudioKernel(AudioObj obj, string kernelName = "normalize", string version = "00", int chunkSize = 0, float overlap = 0.0f, Dictionary<string, object>? optionalArguments = null, bool log = false)
		{
			// Check executioner
			if (this.KernelExecutioner == null)
			{
				this.Log("Kernel executioner not initialized (Cannot execute audio kernel)");
				return IntPtr.Zero;
			}

			// Take time
			Stopwatch sw = Stopwatch.StartNew();

			// Optionally move audio to device
			bool moved = false;
			if (obj.OnHost)
			{
				await this.MoveAudio(obj, chunkSize, overlap);
				moved = true;
			}
			if (!obj.OnDevice)
			{
				if (this.EnableLogging)
				{
					this.Log("Audio object is not on device", "Pointer=" + obj.Pointer.ToString("X16"), 1);
				}
				return IntPtr.Zero;
			}

			// Execute kernel on device
			obj.Pointer = this.KernelExecutioner.ExecuteAudioKernel((nint)obj.Pointer, out double factor, obj.Length, kernelName, version, chunkSize, overlap, obj.Samplerate, obj.Bitdepth, obj.Channels, optionalArguments);
			if (obj.Pointer == IntPtr.Zero && log)
			{
				this.Log("Failed to execute audio kernel", "Pointer=" + obj.Pointer.ToString("X16"), 1);
			}

			// Reload kernel
			this.KernelCompiler?.LoadKernel(kernelName + version, "");

			// Log factor & set new bpm
			if (factor != 1.00f)
			{
				// IMPORTANT: Set obj Factor
				obj.StretchFactor = factor;
				obj.Bpm = (float) (obj.Bpm / factor);
				this.Log("Factor for audio kernel: " + factor, "Pointer=" + obj.Pointer.ToString("X16") + " BPM: " + obj.Bpm, 1);
			}

			// Move back optionally
			if (moved && obj.OnDevice && obj.Form.StartsWith("f"))
			{
				await this.MoveAudio(obj, chunkSize, overlap);
			}

			if (this.EnableLogging)
			{
				sw.Stop();
				this.Log("Executed audio kernel", "Pointer=" + obj.Pointer.ToString("X16") + ", Time: " + sw.ElapsedMilliseconds + "ms", 1);
			}

			return (nint)obj.Pointer;
		}

		public async Task<IntPtr> ExecuteImageKernel(ImageObj obj, string kernelBaseName = "mandelbrot", string kernelVersion = "00", object[]? variableArguments = null, bool log = false)
		{
			// Verify obj on device
			bool moved = false;
			if (obj.OnHost)
			{
				if (this.EnableLogging)
				{
					this.Log("Image was on host, pushing ...", obj.Width + " x " + obj.Height, 2);
				}

				// Get pixel bytes
				byte[] pixels = (await obj.GetBytes(false)).AsParallel().ToArray();
				if (pixels.LongLength == 0)
				{
					this.Log("Couldn't get byte[] from image object", "Aborting", 1);
					return IntPtr.Zero;
				}

				// Push pixels -> pointer
				var mem = this.MemoryRegister?.PushData<byte>(pixels);
				if (mem == null)
				{
					if (this.EnableLogging)
					{
						this.Log("Couldn't push pixels to device", pixels.LongLength.ToString("N0"), 1);
					}

					return IntPtr.Zero;
				}

				long memIndexHandle = mem[0].Handle;
				if (memIndexHandle == 0)
				{
					if (this.EnableLogging)
					{
						this.Log("Couldn't parse memory index handle", "Aborting", 1);
					}
					return IntPtr.Zero;
				}

				obj.Pointer = memIndexHandle;
				if (obj.OnHost || obj.Pointer == IntPtr.Zero)
				{
					if (this.EnableLogging)
					{
						this.Log("Couldn't get pointer after pushing pixels to device", pixels.LongLength.ToString("N0"), 1);
					}
					return IntPtr.Zero;
				}

				moved = true;
			}

			// Get parameters for call
			IntPtr pointer = (nint)obj.Pointer;
			int width = obj.Width;
			int height = obj.Height;
			int channels = obj.Channels;
			int bitdepth = obj.Bitdepth;

			// Call exec on image
			IntPtr outputPointer = await Task.Run(() =>
			{
				return this.KernelExecutioner?.ExecuteImageKernel(pointer, kernelBaseName, kernelVersion, width, height, channels, bitdepth, variableArguments, log) ?? IntPtr.Zero;
			});
			if (outputPointer == IntPtr.Zero)
			{
				if (this.EnableLogging)
				{
					this.Log("Couldn't get output pointer after kernel execution", "Aborting", 1);
				}

				return outputPointer;
			}

			// Set obj pointer
			obj.Pointer = outputPointer;

			// Optionally: Move back to host
			if (obj.OnDevice && moved)
			{
				// Pull pixel bytes
				byte[] pixels = this.MemoryRegister?.PullData<byte>((nint)obj.Pointer) ?? [];
				if (pixels == null || pixels.LongLength == 0)
				{
					if (this.EnableLogging)
					{
						this.Log("Couldn't pull pixels (byte[]) from device", "Aborting", 1);
					}
					return IntPtr.Zero;
				}

				// Aggregate image
				await obj.SetImage(pixels, false);
			}

			return outputPointer;
		}

		public async Task<IntPtr> PerformFFT(AudioObj obj, string version = "01", int chunkSize = 0, float overlap = 0.0f)
		{
			// Optionally move audio to device
			if (obj.OnHost)
			{
				await this.MoveAudio(obj, chunkSize, overlap);
			}
			if (!obj.OnDevice)
			{
				if (this.EnableLogging)
				{
					this.Log("Couldn't move audio object to device", "Pointer=" + obj.Pointer.ToString("X16"), 1);
				}

				return IntPtr.Zero;
			}

			// Perform FFT on device
			obj.Pointer = this.KernelExecutioner?.ExecuteFFT((nint)obj.Pointer, version, obj.Form.FirstOrDefault(), chunkSize, overlap, true) ?? obj.Pointer;

			if (obj.Pointer == IntPtr.Zero)
			{
				this.Log("Failed to perform FFT", "Pointer=" + obj.Pointer.ToString("X16"), 1);
			}
			else
			{
				if (this.EnableLogging)
				{
					this.Log("Performed FFT", "Pointer=" + obj.Pointer.ToString("X16"), 1);
				}
				obj.Form = obj.Form.StartsWith("f") ? "c" : "f";
			}

			return (nint)obj.Pointer;
		}

		public async Task<AudioObj> TimeStretch(AudioObj obj, string kernelName = "timestretch_double", string version = "03", double factor = 1.000d, int chunkSize = 16384, float overlap = 0.5f)
		{
			if (this.KernelExecutioner == null)
			{
				this.Log("Kernel executioner is not initialized.");
				return obj;
			}

			kernelName = kernelName + version;

			try
			{
				// Optionally move obj to device
				bool moved = false;
				if (obj.OnHost)
				{
					IntPtr pointer = await this.MoveAudio(obj, chunkSize, overlap);
					if (pointer == IntPtr.Zero)
					{
						this.Log("Failed to move audio to device memory.");
						return obj;
					}
					moved = true;
				}

				// Get optional args
				Dictionary<string, object> optionalArgs;
				if (kernelName.ToLower().Contains("double"))
				{
					// Double kernel
					optionalArgs = new()
						{
							{ "factor", (double) factor }
						};
				}
				else
				{
					optionalArgs = new()
						{
							{ "factor", (float) factor }
						};
				}

				// Execute time stretch kernel
				var ptr  = await this.ExecuteAudioKernel(obj, kernelName, "", chunkSize, overlap, optionalArgs, true);
				if (ptr == IntPtr.Zero)
				{
					this.Log("Failed to execute time stretch kernel.", "Pointer=" + ptr.ToString("X16"));
					return obj;
				}

				// Optionally move obj back to host
				if (moved && obj.OnDevice)
				{
					IntPtr resultPointer = await this.MoveAudio(obj, chunkSize, overlap);
					if (resultPointer != IntPtr.Zero)
					{
						this.Log("Failed to move audio back to host memory.");
						return obj;
					}
				}
			}
			catch (Exception ex)
			{
				this.Log($"Error during time stretch: {ex.Message}");
			}
			finally
			{
				await Task.Yield();
			}

			return obj;
		}
	}
}
