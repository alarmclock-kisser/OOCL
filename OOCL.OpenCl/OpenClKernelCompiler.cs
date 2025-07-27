using OOCL.Core;
using OpenTK.Compute.OpenCL;
using OpenTK.Mathematics;
using System.Text;

namespace OOCL.OpenCl
{
	public class OpenClKernelCompiler : IDisposable, IOpenClObj
	{
		// INTERFACE
		public int Index => 0;
		public string Name => $"OpenCL Kernel Compiler";
		public string Type => "OpenCL-Compiler";
		public bool Online => this.QUE.Handle != IntPtr.Zero && this.MemoryRegister != null;
		public string Status => this.Online && this.lastError == CLResultCode.Success ? "OK" : $"Error: '{this.LastErrorMessage}'";
		public string LastErrorMessage => this.lastError == CLResultCode.Success ? string.Empty : this.lastError.ToString();
		public IEnumerable<string> ErrorMessages { get; private set; } = [];	
		// -----

		private string Repopath;
		private OpenClMemoryRegister MemoryRegister;
		private CLContext CTX;
		private CLDevice DEV;
		private CLPlatform PLAT;
		private CLCommandQueue QUE;

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
		private bool _precompileAndCache;

		public bool PrecompileAndCache
		{
			get => this._precompileAndCache;
			set
			{
				this._precompileAndCache = value;
				if (value)
				{
					this.Log("Precompiling and caching kernels...");
					this.PrecompileAllKernels(true);
				}
				else
				{
					this.Log("Precompiling and caching disabled.");
				}
			}
		}

		public int PointerInputCount => this.GetArgumentPointerCount();
		public string PointerInputType => this.GetKernelPointerInputType().Name;
		public string PointerOutputType => this.GetKernelPointerOutputType().Name;



		// ----- ----- ----- ATTRIBUTES ----- ----- ----- \\
		private CLKernel? kernel = null;
		public string KernelFile = string.Empty;
		public CLKernel? Kernel => this.kernel;



		private Dictionary<CLKernel, string> kernelCache = [];
		public IEnumerable<string> CachedKernels => this.kernelCache.Values;


		// ----- ----- ----- LAMBDA ----- ----- ----- \\
		private Dictionary<string, string> Files => this.GetKernelFiles();
		public IEnumerable<string> KernelFiles => this.Files.Keys;
		public IEnumerable<string> KernelNames => this.Files.Values;

		private bool tryAnalog = false;
		public bool TryAnalog 
		{
			get => this.tryAnalog;
			set
			{
				this.tryAnalog = value;
				if (value)
				{
					this.Arguments = this.GetArguments(true);
					this.Log("Using analog kernel argument parsing.");
				}
				else
				{
					this.Arguments = this.GetArguments();
					this.Log("Using standard kernel argument parsing.");
				}
			}
		}
		private Dictionary<string, Type> GetArguments(bool analog = false) => analog ? this.GetKernelArgumentsAnalog() : this.GetKernelArguments();
		internal Dictionary<string, Type> Arguments
		{
			get => this.GetArguments(this.TryAnalog);
			set
			{
				if (value == null || value.Count == 0)
				{
					this.Log("Kernel arguments are empty or null.");
					return;
				}

				this.Arguments = value;
				this.Log("Kernel arguments set: " + string.Join(", ", value.Keys));
			}
		}
		public IEnumerable<string> ArgumentNames => this.Arguments.Keys;
		public IEnumerable<string> ArgumentTypes => this.Arguments.Values.Select(t => t.Name);

		// Logging
		public bool EnableLogging { get; set; } = true;
		private RollingFileLogger logger;


		// ----- ----- ----- CONSTRUCTORS ----- ----- ----- \\
		public OpenClKernelCompiler(string repopath, OpenClMemoryRegister memoryRegister, CLContext ctx, CLDevice dev, CLPlatform plat, CLCommandQueue que, RollingFileLogger logger)
		{
			// Set attributes
			this.Repopath = repopath;
			this.logger = logger;

			this.MemoryRegister = memoryRegister;
			this.CTX = ctx;
			this.DEV = dev;
			this.PLAT = plat;
			this.QUE = que;

			this.PrecompileAllKernels(this.PrecompileAndCache);
		}




		// ----- ----- ----- METHODS ----- ----- ----- \\





		// ----- ----- ----- PUBLIC METHODS ----- ----- ----- \\
		// Log
		public void Log(string message = "", string inner = "", int indent = 0)
		{
			string msg = "[Kernel]: " + new string(' ', indent * 2) + message;

			if (!string.IsNullOrEmpty(inner))
			{
				msg += " (" + inner + ")";
			}

			// Invoke optionally
			Console.WriteLine(msg);

			if (!this.EnableLogging)
			{
				return; // Logging is disabled
			}

this.logger.Log(this.GetType(), msg, inner, indent).GetAwaiter().GetResult();
		}

		// Dispose
		public void Dispose()
		{
			// Dispose logic here
			this.kernel = null;
			this.KernelFile = string.Empty;
			this.kernelCache.Clear();
		}

		// Files
		private Dictionary<string, string> GetKernelFiles(string subdir = "Kernels")
		{
			string dir = Path.Combine(this.Repopath, subdir);

			// Build dir if it doesn't exist
			if (!Directory.Exists(dir))
			{
				Directory.CreateDirectory(dir);
			}

			// Get all .cl files in the directory
			string[] files = Directory.GetFiles(dir, "*.cl", SearchOption.AllDirectories);

			// Check if any files were found
			if (files.LongLength <= 0)
			{
				this.Log("No kernel files found in directory: " + dir);
				return [];
			}

			// Verify each file
			Dictionary<string, string> verifiedFiles = [];
			foreach (string file in files)
			{
				string? verifiedFile = this.VerifyKernelFile(file);
				if (verifiedFile != null)
				{
					string? name = this.GetKernelName(verifiedFile);
					verifiedFiles.Add(verifiedFile, name ?? "N/A");
				}
			}

			// Return
			return verifiedFiles;
		}

		public string? VerifyKernelFile(string filePath)
		{
			// Check if file exists & is .cl
			if (!File.Exists(filePath))
			{
				this.Log("Kernel file not found: " + filePath + "(Root-Path: '" + this.Repopath + "')");
				return null;
			}

			if (Path.GetExtension(filePath) != ".cl")
			{
				this.Log("Kernel file is not a .cl file: " + filePath);
				return null;
			}

			// Check if file is empty
			string[] lines = File.ReadAllLines(filePath);
			if (lines.Length == 0)
			{
				this.Log("Kernel file is empty: " + filePath);
				return null;
			}

			// Check if file contains kernel function
			if (!lines.Any(line => line.Contains("__kernel")))
			{
				this.Log("Kernel function not found in file: " + filePath);
				return null;
			}

			return Path.GetFullPath(filePath);
		}

		public string? GetKernelName(string filePath)
		{
			// Verify file
			string? verifiedFilePath = this.VerifyKernelFile(filePath);
			if (verifiedFilePath == null)
			{
				return null;
			}

			// Try to extract function name from kernel code text
			string code = File.ReadAllText(filePath);

			// Find index of first "__kernel void "
			int index = code.IndexOf("__kernel void ");
			if (index == -1)
			{
				this.Log("Kernel function not found in file: " + filePath);
				return null;
			}

			// Find index of first "(" after "__kernel void "
			int startIndex = index + "__kernel void ".Length;
			int endIndex = code.IndexOf("(", startIndex);
			if (endIndex == -1)
			{
				this.Log("Kernel function not found in file: " + filePath);
				return null;
			}

			// Extract function name
			string functionName = code.Substring(startIndex, endIndex - startIndex).Trim();
			if (functionName.Contains(" ") || functionName.Contains("\t") ||
				functionName.Contains("\n") || functionName.Contains("\r"))
			{
				this.Log("Kernel function name is invalid: " + functionName);
			}

			// Check if function name is empty
			if (string.IsNullOrEmpty(functionName))
			{
				this.Log("Kernel function name is empty: " + filePath);
				return null;
			}

			// Compare to file name without ext
			string fileName = Path.GetFileNameWithoutExtension(filePath);
			if (string.Compare(functionName, fileName, StringComparison.OrdinalIgnoreCase) != 0)
			{
				this.Log("Kernel function name does not match file name: " + filePath, "", 2);
			}

			return functionName;
		}

		// Compile
		private CLKernel? CompileFile(string filePath)
		{
			// Verify file
			string? verifiedFilePath = this.VerifyKernelFile(filePath);
			if (verifiedFilePath == null)
			{
				return null;
			}

			// Get kernel name
			string? kernelName = this.GetKernelName(verifiedFilePath);
			if (kernelName == null)
			{
				return null;
			}

			// Read kernel code
			string code = File.ReadAllText(verifiedFilePath);

			// Create program
			CLProgram program = CL.CreateProgramWithSource(this.CTX, code, out CLResultCode error);
			if (error != CLResultCode.Success)
			{
				this.lastError = error;
				this.Log("Error creating program from source: " + error.ToString());
				return null;
			}

			// Create callback
			CL.ClEventCallback callback = new((program, userData) =>
			{
				// Check build log
				//
			});

			// When building the kernel
			string buildOptions = "-cl-std=CL1.2 -cl-fast-relaxed-math";
			CL.BuildProgram(program, 1, [this.DEV], buildOptions, 0, IntPtr.Zero);

			// Build program
			error = CL.BuildProgram(program, [this.DEV], buildOptions, callback);
			if (error != CLResultCode.Success)
			{
				this.lastError = error;
				this.Log("Error building program: " + error.ToString());

				// Get build log
				error = CL.GetProgramBuildInfo(program, this.DEV, ProgramBuildInfo.Log, out byte[] buildLog);
				if (error != CLResultCode.Success)
				{
					this.lastError = error;
					this.Log("Error getting build log: " + error.ToString());
				}
				else
				{
					string log = Encoding.UTF8.GetString(buildLog);
					this.Log("Build log: " + log, "", 1);
				}

				error = CL.ReleaseProgram(program);
				if (error != CLResultCode.Success)
				{
					this.lastError = error;
					this.Log("Error releasing program: " + error.ToString());
				}

				return null;
			}

			// Create kernel
			CLKernel kernel = CL.CreateKernel(program, kernelName, out error);
			if (error != CLResultCode.Success)
			{
				this.lastError = error;
				this.Log("Error creating kernel: " + error.ToString());

				// Get build log
				error = CL.GetProgramBuildInfo(program, this.DEV, ProgramBuildInfo.Log, out byte[] buildLog);
				if (error != CLResultCode.Success)
				{
					this.lastError = error;
					this.Log("Error getting build log: " + error.ToString());
				}
				else
				{
					string log = Encoding.UTF8.GetString(buildLog);
					this.Log("Build log: " + log, "", 1);
				}

				CL.ReleaseProgram(program);
				return null;
			}

			// Return kernel
			return kernel;
		}

		private Dictionary<string, Type> GetKernelArguments(CLKernel? kernel = null, string filePath = "")
		{
			Dictionary<string, Type> arguments = [];

			// Verify kernel
			kernel ??= this.kernel;
			if (kernel == null)
			{
				// Try get kernel by file path
				kernel = this.CompileFile(filePath);
				if (kernel == null)
				{
					this.Log("Kernel is null");
					return arguments;
				}
			}

			// Get kernel info
			CLResultCode error = CL.GetKernelInfo(kernel.Value, KernelInfo.NumberOfArguments, out byte[] argCountBytes);
			if (error != CLResultCode.Success)
			{
				this.lastError = error;
				this.Log("Error getting kernel info: " + error.ToString());
				return arguments;
			}

			// Get number of arguments
			int argCount = BitConverter.ToInt32(argCountBytes, 0);

			// Loop through arguments
			for (int i = 0; i < argCount; i++)
			{
				// Get argument info type name
				error = CL.GetKernelArgInfo(kernel.Value, (uint) i, KernelArgInfo.TypeName, out byte[] argTypeBytes);
				if (error != CLResultCode.Success)
				{
					this.lastError = error;
					this.Log("Error getting kernel argument info: " + error.ToString());
					continue;
				}

				// Get argument info arg name
				error = CL.GetKernelArgInfo(kernel.Value, (uint) i, KernelArgInfo.Name, out byte[] argNameBytes);
				if (error != CLResultCode.Success)
				{
					this.lastError = error;
					this.Log("Error getting kernel argument info: " + error.ToString());
					continue;
				}

				// Get argument type & name
				string argName = Encoding.UTF8.GetString(argNameBytes).TrimEnd('\0');
				string typeName = Encoding.UTF8.GetString(argTypeBytes).TrimEnd('\0');
				Type? type = null;

				// Switch for typeName
				if (typeName.EndsWith("*"))
				{
					typeName = typeName.Replace("*", "").ToLower();
					switch (typeName)
					{
						case "int":
							type = typeof(int*);
							break;
						case "float":
							type = typeof(float*);
							break;
						case "long":
							type = typeof(long*);
							break;
						case "uchar":
							type = typeof(byte*);
							break;
						case "vector2":
							type = typeof(Vector2*);
							break;
						default:
							this.Log("Unknown pointer type: " + typeName, "", 2);
							break;
					}
				}
				else
				{
					switch (typeName)
					{
						case "int":
							type = typeof(int);
							break;
						case "float":
							type = typeof(float);
							break;
						case "double":
							type = typeof(double);
							break;
						case "char":
							type = typeof(char);
							break;
						case "uchar":
							type = typeof(byte);
							break;
						case "short":
							type = typeof(short);
							break;
						case "ushort":
							type = typeof(ushort);
							break;
						case "long":
							type = typeof(long);
							break;
						case "ulong":
							type = typeof(ulong);
							break;
						case "vector2":
							type = typeof(Vector2);
							break;
						default:
							this.Log("Unknown argument type: " + typeName, "", 2);
							break;
					}
				}

				// Add to dictionary
				arguments.Add(argName, type ?? typeof(object));
			}

			// Return arguments
			return arguments;
		}

		private Dictionary<string, Type> GetKernelArgumentsAnalog(string? filepath = null)
		{
			Dictionary<string, Type> arguments = [];
			if (string.IsNullOrEmpty(filepath))
			{
				filepath = this.KernelFile;
			}

			// Read kernel code
			filepath = this.VerifyKernelFile(filepath ?? "");
			if (filepath == null)
			{
				this.Log("Kernel file not found or invalid: " + filepath);
				return arguments;
			}

			string code = File.ReadAllText(filepath);
			if (string.IsNullOrEmpty(code))
			{
				this.Log("Kernel code is empty: " + filepath);
				return arguments;
			}

			// Find kernel function
			int index = code.IndexOf("__kernel void ");
			if (index == -1)
			{
				this.Log("Kernel function not found in file: " + filepath);
				return arguments;
			}
			int startIndex = index + "__kernel void ".Length;
			int endIndex = code.IndexOf("(", startIndex);
			if (endIndex == -1)
			{
				this.Log("Kernel function not found in file: " + filepath);
				return arguments;
			}

			string functionName = code.Substring(startIndex, endIndex - startIndex).Trim();
			if (string.IsNullOrEmpty(functionName))
			{
				this.Log("Kernel function name is empty: " + filepath);
				return arguments;
			}

			if (functionName.Contains(" ") || functionName.Contains("\t") ||
				functionName.Contains("\n") || functionName.Contains("\r"))
			{
				this.Log("Kernel function name is invalid: " + functionName, "", 2);
			}

			// Get arguments string
			int argsStartIndex = code.IndexOf("(", endIndex) + 1;
			int argsEndIndex = code.IndexOf(")", argsStartIndex);
			if (argsEndIndex == -1)
			{
				this.Log("Kernel arguments not found in file: " + filepath);
				return arguments;
			}
			string argsString = code.Substring(argsStartIndex, argsEndIndex - argsStartIndex).Trim();
			if (string.IsNullOrEmpty(argsString))
			{
				this.Log("Kernel arguments are empty: " + filepath);
				return arguments;
			}

			string[] args = argsString.Split(',');

			foreach (string arg in args)
			{
				string[] parts = arg.Trim().Split(' ');
				if (parts.Length < 2)
				{
					this.Log("Kernel argument is invalid: " + arg, "", 2);
					continue;
				}
				string typeName = parts[^2].Trim();
				string argName = parts[^1].Trim().TrimEnd(';', ')', '\n', '\r', '\t');
				Type? type = null;
				if (typeName.EndsWith("*"))
				{
					typeName = typeName.Replace("*", "");
					switch (typeName)
					{
						case "int":
							type = typeof(int*);
							break;
						case "float":
							type = typeof(float*);
							break;
						case "long":
							type = typeof(long*);
							break;
						case "uchar":
							type = typeof(byte*);
							break;
						case "Vector2":
							type = typeof(Vector2*);
							break;
						default:
							this.Log("Unknown pointer type: " + typeName, "", 2);
							break;
					}
				}
				else
				{
					switch (typeName)
					{
						case "int":
							type = typeof(int);
							break;
						case "float":
							type = typeof(float);
							break;
						case "double":
							type = typeof(double);
							break;
						case "char":
							type = typeof(char);
							break;
						case "uchar":
							type = typeof(byte);
							break;
						case "short":
							type = typeof(short);
							break;
						case "ushort":
							type = typeof(ushort);
							break;
						case "long":
							type = typeof(long);
							break;
						case "ulong":
							type = typeof(ulong);
							break;
						case "Vector2":
							type = typeof(Vector2);
							break;
						default:
							this.Log("Unknown argument type: " + typeName, "", 2);
							break;
					}
				}
				if (type != null)
				{
					arguments.Add(argName, type ?? typeof(object));
				}
			}

			return arguments;
		}

		private int GetArgumentPointerCount()
		{
			// Get kernel argument types
			Type[] argTypes = this.Arguments.Values.ToArray();

			// Count pointer arguments
			int count = 0;
			foreach (Type type in argTypes)
			{
				if (type.Name.EndsWith("*"))
				{
					count++;
				}
			}

			return count;
		}

		private Type GetKernelPointerInputType()
		{
			// Get kernel argument types
			Type[] argTypes = this.Arguments.Values.ToArray();

			// Find first pointer type
			foreach (Type type in argTypes)
			{
				if (type.Name.EndsWith("*"))
				{
					return type;
				}
			}

			// If no pointer type found, return object
			return typeof(object);
		}

		private Type GetKernelPointerOutputType()
		{
			// Get kernel argument types
			Type[] argTypes = this.Arguments.Values.ToArray();
			// Find last pointer type
			for (int i = argTypes.Length - 1; i >= 0; i--)
			{
				if (argTypes[i].Name.EndsWith("*"))
				{
					return argTypes[i];
				}
			}

			// If no pointer type found, return object
			return typeof(object);
		}




		// UI
		private Dictionary<CLKernel, string> PrecompileAllKernels(bool cache)
		{
			// Get all kernel files
			string[] kernelFiles = this.Files.Keys.ToArray();

			// Precompile all kernels
			Dictionary<CLKernel, string> precompiledKernels = [];
			foreach (string kernelFile in kernelFiles)
			{
				// Compile kernel
				CLKernel? kernel = this.CompileFile(kernelFile);
				if (kernel != null)
				{
					precompiledKernels.Add(kernel.Value, kernelFile);
				}
				else
				{
					this.Log("Error compiling kernel: " + kernelFile, "", 2);
				}
			}

			this.UnloadKernel();

			// Cache
			if (cache)
			{
				this.kernelCache = precompiledKernels;
			}

			return precompiledKernels;
		}

		public string GetLatestKernelFile(string searchName = "")
		{
			string[] files = this.Files.Keys.ToArray();

			// Get all files that contain searchName
			string[] filteredFiles = files.Where(file => file.Contains(searchName, StringComparison.OrdinalIgnoreCase)).ToArray();
			string latestFile = filteredFiles.Select(file => new FileInfo(file))
				.OrderByDescending(file => file.LastWriteTime)
				.FirstOrDefault()?.FullName ?? "";

			// Return latest file
			if (string.IsNullOrEmpty(latestFile))
			{
				this.Log("No kernel files found with name: " + searchName);
				return "";
			}

			return latestFile;
		}



		// Load
		private CLKernel? Load(string kernelName = "", string filePath = "")
		{
			// Get kernel file path
			if (!string.IsNullOrEmpty(filePath))
			{
				kernelName = Path.GetFileNameWithoutExtension(filePath);
			}
			else
			{
				filePath = Directory.GetFiles(Path.Combine(this.Repopath, "Kernels"), kernelName + "*.cl", SearchOption.AllDirectories).Where(f => Path.GetFileNameWithoutExtension(f).Length == kernelName.Length).FirstOrDefault() ?? "";
			}

			// Compile kernel if not cached
			if (this.kernel != null && this.KernelFile == filePath)
			{
				this.Log("Kernel already loaded: " + kernelName, "", 1);
				return this.kernel;
			}

			CLKernel? kernel = this.kernel = this.CompileFile(filePath);
			this.KernelFile = filePath;

			// Check if kernel is null
			if (this.kernel == null)
			{
				this.Log("Kernel is null");
				return null;
			}
			else
			{
				// String of args like "(byte*)'pixels', (int)'width', (int)'height'"
				string argNamesString = string.Join(", ", this.Arguments.Keys.Select((arg, i) => $"({this.Arguments.Values.ElementAt(i).Name}) '{arg}'"));
				this.Log("Kernel loaded: '" + kernelName + "'", "", 1);
				// this.Log("Kernel arguments: [" + argNamesString + "]", "", 1);
			}

			// TryAdd to cached
			this.kernelCache.TryAdd(this.kernel.Value, filePath);

			return kernel;
		}
		public string LoadKernel(string kernelName = "", string filePath = "")
		{
			// Load kernel
			CLKernel? kernel = this.Load(kernelName, filePath);
			if (kernel == null)
			{
				this.Log("Failed to load kernel: " + kernelName, "", 2);
				return string.Empty;
			}

			// Return kernel name
			return this.GetKernelName(filePath) ?? kernelName ?? string.Empty;
		}

		public bool UnloadKernel()
		{
			// Release kernel
			if (this.kernel != null)
			{
				CLResultCode error = CL.ReleaseKernel(this.kernel.Value);
				this.kernel = null;
				this.KernelFile = string.Empty;
				if (error != CLResultCode.Success)
				{
					this.lastError = error;
					this.Log("Error releasing kernel: " + error.ToString());
					return false;
				}
			}
			else
			{
				this.Log("No kernel to unload.");
				return true;
			}

			return this.kernel == null;
		}

		public object[] GetArgumentDefaultValues()
		{
			// Get kernel arguments
			Dictionary<string, Type> arguments = this.Arguments;
			
			// Create array of argument values
			object[] argValues = new object[arguments.Count];
			
			// Loop through arguments and set values
			int i = 0;
			foreach (var arg in arguments)
			{
				Type type = arg.Value;
				if (type.IsPointer)
				{
					argValues[i] = IntPtr.Zero;
				}
				else if (type == typeof(int))
				{
					argValues[i] = 0;
				}
				else if (type == typeof(float))
				{
					argValues[i] = 0f;
				}
				else if (type == typeof(double))
				{
					argValues[i] = 0d;
				}
				else if (type == typeof(byte))
				{
					argValues[i] = (byte)0;
				}
				else if (type == typeof(Vector2))
				{
					argValues[i] = Vector2.Zero;
				}
				else
				{
					argValues[i] = 0;
				}
				i++;
			}
			return argValues;
		}
	}
}
