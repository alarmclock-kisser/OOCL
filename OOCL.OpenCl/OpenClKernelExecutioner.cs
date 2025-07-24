using OpenTK.Compute.OpenCL;
using OpenTK.Mathematics;
using System.Diagnostics;

namespace OOCL.OpenCl
{
	public class OpenClKernelExecutioner
	{
		// INTERFACE
		public string Name => "OpenCL Kernel Executioner";
		public string Type => "OpenCL-Executioner";
		public bool Online => this.QUE.Handle != IntPtr.Zero && this.MemoryRegister != null && this.KernelCompiler != null;
		public string Status => this.Online && this.lastError == CLResultCode.Success ? "OK" : $"Error: '{this.LastErrorMessage}'";
		public string LastErrorMessage => this.lastError == CLResultCode.Success ? string.Empty : this.lastError.ToString();
		public IEnumerable<string> ErrorMessages { get; private set; } = [];
		// -----

		// ----- ----- -----  ATTRIBUTES  ----- ----- ----- \\
		private string Repopath;
		private OpenClMemoryRegister MemoryRegister;
		private CLContext CTX;
		private CLDevice DEV;
		private CLPlatform PLAT;
		private CLCommandQueue QUE;
		private OpenClKernelCompiler KernelCompiler;


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

		private CLKernel? Kernel => this.KernelCompiler?.Kernel;
		public string KernelFile => this.KernelCompiler?.KernelFile ?? string.Empty;




		// ----- ----- -----  CONSTRUCTOR ----- ----- ----- \\
		public OpenClKernelExecutioner(string repopath, OpenClMemoryRegister memR, CLContext context, CLDevice device, CLPlatform platform, CLCommandQueue queue, OpenClKernelCompiler compiler)
		{
			this.Repopath = repopath;
			this.MemoryRegister = memR;
			this.CTX = context;
			this.DEV = device;
			this.PLAT = platform;
			this.QUE = queue;
			this.KernelCompiler = compiler;
		}






		// ----- ----- -----  METHODS  ----- ----- ----- \\
		public void Log(string message = "", string inner = "", int indent = 0)
		{
			string msg = "[Exec]: " + new string(' ', indent * 2) + message;

			if (!string.IsNullOrEmpty(inner))
			{
				msg += " (" + inner + ")";
			}

			// Invoke optionally
			Console.WriteLine(msg);
		}


		public void Dispose()
		{
			// Dispose logic here
			
		}





		// EXEC
		public IntPtr ExecuteFFT(IntPtr pointer, string version = "01", char form = 'f', int chunkSize = 16384, float overlap = 0.5f, bool free = true, bool log = false)
		{
			int overlapSize = (int) (overlap * chunkSize);

			string kernelsPath = Path.Combine(this.Repopath, "Kernels", "Audio");
			string file = "";
			if (form == 'f')
			{
				file = Path.Combine(kernelsPath, $"fft{version}.cl");
			}
			else if (form == 'c')
			{
				file = Path.Combine(kernelsPath, $"ifft{version}.cl");
			}

			// STOPWATCH START
			Stopwatch sw = Stopwatch.StartNew();

			// Load kernel from file, else abort
			this.KernelCompiler.LoadKernel("", file);
			if (this.Kernel == null)
			{
				return pointer;
			}

			// Get input buffers
			ClMem? inputBuffers = this.MemoryRegister.GetBuffer(pointer);
			if (inputBuffers == null || inputBuffers.GetCount() <= 0)
			{
				if (log)
				{
					this.Log("Input buffer not found or invalid length: " + pointer.ToString("X16"), "", 2);
				}
				return pointer;
			}

			// Get output buffers
			ClMem? outputBuffers = null;
			if (form == 'f')
			{
				outputBuffers = this.MemoryRegister.AllocateGroup<Vector2>(inputBuffers.GetCount(), (nint)inputBuffers.GetLengths().FirstOrDefault());
			}
			else if (form == 'c')
			{
				outputBuffers = this.MemoryRegister.AllocateGroup<float>(inputBuffers.GetCount(), (nint)inputBuffers.GetLengths().FirstOrDefault());
			}
			if (outputBuffers == null || outputBuffers.GetCount() <= 0 || outputBuffers.GetLengths().Any(l => l < 1))
			{
				if (log)
				{
					this.Log("Couldn't allocate valid output buffers / lengths", "", 2);
				}
				return pointer;
			}


			// Set static args
			CLResultCode error = this.SetKernelArgSafe(2, (int) inputBuffers.GetLengths().FirstOrDefault());
			if (error != CLResultCode.Success)
			{
				this.lastError = error;
				if (log)
				{
					this.Log("Failed to set kernel argument for chunk size: " + error, "", 2);
				}
				return pointer;
			}
			error = this.SetKernelArgSafe(3, overlapSize);
			if (error != CLResultCode.Success)
			{
				this.lastError = error;
				if (log)
				{
					this.Log("Failed to set kernel argument for overlap size: " + error, "", 2);
				}
				return pointer;
			}

			// Calculate optimal work group size
			uint maxWorkGroupSize = this.GetMaxWorkGroupSize();
			uint globalWorkSize = 1;
			uint localWorkSize = 1;


			// Loop through input buffers
			int count = inputBuffers.GetCount();
			for (int i = 0; i < count; i++)
			{
				error = this.SetKernelArgSafe(0, inputBuffers[i]);
				if (error != CLResultCode.Success)
				{
					this.lastError = error;
					if (log)
					{
						this.Log($"Failed to set kernel argument for input buffer {i}: {error}", "", 2);
					}
					return pointer;
				}
				error = this.SetKernelArgSafe(1, outputBuffers[i]);
				if (error != CLResultCode.Success)
				{
					this.lastError = error;
					if (log)
					{
						this.Log($"Failed to set kernel argument for output buffer {i}: {error}", "", 2);
					}
					return pointer;
				}

				// Execute kernel
				error = CL.EnqueueNDRangeKernel(this.QUE, this.Kernel.Value, 1, null, [(UIntPtr) globalWorkSize], [(UIntPtr) localWorkSize], 0, null, out CLEvent evt);
				if (error != CLResultCode.Success)
				{
					this.lastError = error;
					if (log)
					{
						this.Log($"Failed to enqueue kernel for buffer {i}: " + error, "", 2);
					}

					return pointer;
				}

				// Wait for completion
				error = CL.WaitForEvents(1, [evt]);
				if (error != CLResultCode.Success)
				{
					this.lastError = error;
					if (log)
					{
						this.Log($"Wait failed for buffer {i}: " + error, "", 2);
					}
				}

				// Release event
				CL.ReleaseEvent(evt);
			}

			// STOPWATCH END
			sw.Stop();

			// LOG SUCCESS
			if (log)
			{
				if (form == 'f')
				{
					this.Log("Ran FFT successfully on " + inputBuffers.Count + " buffers within " + sw.ElapsedMilliseconds + " ms", "Form now: " + 'c' + ", Chunk: " + chunkSize + ", Overlap: " + overlapSize, 1);
				}
				else if (form == 'c')
				{
					this.Log("Ran IFFT successfully on " + inputBuffers.Count + " buffers within " + sw.ElapsedMilliseconds + " ms", "Form now: " + 'f' + ", Chunk: " + chunkSize + ", Overlap: " + overlapSize, 1);
				}
			}

			if (outputBuffers != null && free)
			{
				this.MemoryRegister.FreeBuffer(pointer);
			}

			if (outputBuffers == null)
			{
				return pointer;
			}

			return outputBuffers[0].Handle;
		}

		public IntPtr ExecuteAudioKernel(IntPtr objPointer, out double factor, long length = 0, string kernelName = "normalize", string version = "00", int chunkSize = 1024, float overlap = 0.5f, int samplerate = 44100, int bitdepth = 24, int channels = 2, Dictionary<string, object>? optionalArguments = null, bool log = false)
		{
			factor = 1.000d; // Default factor

			// Get kernel path
			string kernelPath = this.KernelCompiler.KernelFiles.FirstOrDefault(f => f.ToLower().Contains((kernelName + version).ToLower())) ?? "";
			if (string.IsNullOrEmpty(kernelPath))
			{
				this.Log("Kernel file not found: " + kernelName + version, "", 2);
				return IntPtr.Zero;
			}

			// Load kernel if not loaded
			if (this.Kernel == null || this.KernelFile != kernelPath)
			{
				this.KernelCompiler.LoadKernel("", kernelPath);
				if (this.Kernel == null || this.KernelFile == null || !this.KernelFile.Contains("\\Audio\\"))
				{
					if (log)
					{
						this.Log("Kernel not loaded or invalid kernel file: " + kernelName, "", 2);
					}

					return IntPtr.Zero;
				}
			}

			// Get input buffers
			ClMem? inputMem = this.MemoryRegister.GetBuffer(objPointer);
			if (inputMem == null || inputMem.GetCount() <= 0 || inputMem.GetLengths().Any(l => l < 1))
			{
				if (log)
				{
					this.Log("Input buffer not found or invalid length: " + objPointer.ToString("X16"), "", 2);
				}

				return IntPtr.Zero;
			}

			// Get variable arguments
			object[] variableArguments = this.KernelCompiler.GetArgumentDefaultValues();

			// Check if FFT is needed
			bool didFft = false;
			if (this.KernelCompiler.PointerInputType.Contains("Vector2") && inputMem.Type == typeof(float).Name)
			{
				if (optionalArguments != null && optionalArguments.ContainsKey("factor"))
				{
					// Set factor to optional argument if provided (contains "stretch") can be float or double depending on the kernel
					if (optionalArguments["factor"] is double dFactor)
					{
						factor = dFactor;
					}
					else if (optionalArguments["factor"] is float fFactor)
					{
						factor = fFactor;
					}
					else
					{
						this.Log("Invalid factor type in optional arguments: " + optionalArguments["factor"].GetType().Name, "", 2);
						return IntPtr.Zero;
					}
				}
				else
				{
					factor = 1.000d;
				}

				IntPtr fftPointer = this.ExecuteFFT(objPointer, "01", 'f', chunkSize, overlap, true, log);
				if (fftPointer == IntPtr.Zero)
				{
					return IntPtr.Zero;
				}

				objPointer = fftPointer;
				didFft = true;

				// Load kernel if not loaded
				if (this.Kernel == null || this.KernelFile != kernelPath)
				{
					this.KernelCompiler.LoadKernel("", kernelPath);
					if (this.Kernel == null || this.KernelFile == null || !this.KernelFile.Contains("\\Audio\\"))
					{
						if (log)
						{
							this.Log("Kernel not loaded or invalid kernel file: " + kernelName, "", 2);
						}

						return IntPtr.Zero;
					}
				}
			}

			// Get input buffers
			inputMem = this.MemoryRegister.GetBuffer(objPointer);
			if (inputMem == null || inputMem.GetCount() <= 0 || inputMem.GetLengths().Any(l => l < 1))
			{
				if (log)
				{
					this.Log("Input buffer not found or invalid length: " + objPointer.ToString("X16"), "", 2);
				}
				return IntPtr.Zero;
			}

			// Get output buffers
			ClMem? outputMem = null;
			if (this.KernelCompiler.PointerInputType == typeof(float*).Name)
			{
				outputMem = this.MemoryRegister.AllocateGroup<float>(inputMem.GetCount(), (nint)inputMem.GetLengths().FirstOrDefault());
			}
			else if (this.KernelCompiler.PointerOutputType == typeof(Vector2*).Name)
			{
				outputMem = this.MemoryRegister.AllocateGroup<Vector2>(inputMem.GetCount(), (nint)inputMem.GetLengths().FirstOrDefault());
			}
			else
			{
				if (log)
				{
					this.Log("Unsupported input buffer type: " + inputMem.Type, "", 2);
				}

				return IntPtr.Zero;
			}

			if (outputMem == null || outputMem.GetCount() == 0 || outputMem.GetLengths().Any(l => l < 1))
			{
				if (log)
				{
					this.Log("Couldn't allocate valid output buffers / lengths", "", 2);
				}

				return IntPtr.Zero;
			}

			// Loop through input buffers
			int count = inputMem.GetCount();
			for (int i = 0; i < count; i++)
			{
				// Get buffers
				CLBuffer inputBuffer = inputMem[i];
				CLBuffer outputBuffer = outputMem[i];

				// Merge arguments
				List<object> arguments = this.MergeArgumentsAudio(variableArguments, inputBuffer, outputBuffer, length, chunkSize, overlap, samplerate, bitdepth, channels, optionalArguments, false);
				if (arguments == null || arguments.Count == 0)
				{
					if (log)
					{
						this.Log("Failed to merge arguments for buffer " + i, "", 2);
					}

					return IntPtr.Zero;
				}

				// Set kernel arguments
				CLResultCode error = CLResultCode.Success;
				for (uint j = 0; j < arguments.Count; j++)
				{
					error = this.SetKernelArgSafe(j, arguments[(int) j]);
					if (error != CLResultCode.Success)
					{
						this.lastError = error;
						if (log)
						{
							this.Log($"Failed to set kernel argument {j} for buffer {i}: " + error, "", 2);
						}

						return IntPtr.Zero;
					}
				}

				// Get work dimensions
				uint maxWorkGroupSize = this.GetMaxWorkGroupSize();
				uint globalWorkSize = (uint) inputMem.GetLengths().ElementAt(i);
				uint localWorkSize = Math.Min(maxWorkGroupSize, globalWorkSize);
				if (localWorkSize == 0)
				{
					localWorkSize = 1; // Fallback to 1 if no valid local size
				}
				if (globalWorkSize < localWorkSize)
				{
					globalWorkSize = localWorkSize; // Ensure global size is at least local size
				}

				// Execute kernel
				error = CL.EnqueueNDRangeKernel(this.QUE, this.Kernel.Value, 1, null, [(UIntPtr) globalWorkSize], [(UIntPtr) localWorkSize], 0, null, out CLEvent evt);
				if (error != CLResultCode.Success)
				{
					this.lastError = error;
					if (log)
					{
						this.Log($"Failed to enqueue kernel for buffer {i}: " + error, "", 2);
					}
					return IntPtr.Zero;
				}

				// Wait for completion
				error = CL.WaitForEvents(1, [evt]);
				if (error != CLResultCode.Success)
				{
					this.lastError = error;
					if (log)
					{
						this.Log($"Wait failed for buffer {i}: " + error, "", 2);
					}
				}

				// Release event
				error = CL.ReleaseEvent(evt);
				if (error != CLResultCode.Success)
				{
					this.lastError = error;
					if (log)
					{
						this.Log($"Failed to release event for buffer {i}: " + error, "", 2);
					}
				}
			}

			// Free input buffer if necessary
			if (outputMem[0].Handle != IntPtr.Zero)
			{
				long freed = this.MemoryRegister.FreeBuffer(objPointer, true);
				if (freed > 0)
				{
					if (log)
					{
						this.Log("Freed input buffer: " + objPointer.ToString("X16") + ", Freed " + freed + " Mbytes", "", 1);
					}
				}
			}

			// Optionally execute IFFT if FFT was done
			IntPtr outputPointer = outputMem[0].Handle;
			if (didFft && outputMem.Type == typeof(Vector2).Name)
			{
				IntPtr ifftPointer = this.ExecuteFFT(outputMem[0].Handle, "01", 'c', chunkSize, overlap, true, log);
				if (ifftPointer == IntPtr.Zero)
				{
					return IntPtr.Zero;
				}

				outputPointer = ifftPointer; // Update output pointer to IFFT result
			}

			// Log success
			if (log)
			{
				this.Log($"Executed kernel '{kernelName}' successfully on {inputMem.Count} buffers with chunk size {chunkSize} and overlap {overlap}", "", 1);
			}

			// Return output buffer handle if available, else return original pointer
			return outputPointer != IntPtr.Zero ? outputPointer : objPointer;
		}

		public IntPtr ExecuteImageKernel(IntPtr pointer = 0, string kernelName = "mandelbrot", string version = "01", int width = 0, int height = 0, int channels = 4, int bitdepth = 8, object[]? variableArguments = null, bool logSuccess = false)
		{
			// Start stopwatch
			List<long> times = [];
			List<string> timeNames = ["load: ", "mem: ", "args: ", "exec: ", "total: "];
			Stopwatch sw = Stopwatch.StartNew();

			// Get kernel path
			string kernelPath = this.KernelCompiler.KernelFiles.FirstOrDefault(f => f.ToLower().Contains((kernelName + version).ToLower())) ?? "";

			// Load kernel if not loaded
			if (this.Kernel == null || this.KernelFile != kernelPath)
			{
				this.KernelCompiler.LoadKernel("", kernelPath);
				if (this.Kernel == null || this.KernelFile == null || !this.KernelFile.Contains("\\Image\\"))
				{
					this.Log("Could not load Kernel '" + kernelName + "'", $"ExecuteKernelIPGeneric({string.Join(", ", variableArguments ?? [])})");
					return pointer;
				}
			}

			// Take time
			times.Add(sw.ElapsedMilliseconds - times.Sum());

			// Get input buffer & length
			ClMem? inputMem = this.MemoryRegister.GetBuffer(pointer);
			if (inputMem == null)
			{
				this.Log("Input buffer not found or invalid length: " + pointer.ToString("X16"), "", 2);
				return pointer;
			}

			// Get kernel arguments & work dimensions
			List<string> argNames = this.KernelCompiler.ArgumentNames.ToList();

			// Dimensions
			int pixelsTotal = (int) inputMem.GetLengths().FirstOrDefault() / 4; // Anzahl der Pixel
			int workWidth = width > 0 ? width : pixelsTotal; // Falls kein width gegeben, 1D
			int workHeight = height > 0 ? height : 1;        // Falls kein height, 1D

			// Work dimensions
			uint workDim = (width > 0 && height > 0) ? 2u : 1u;
			UIntPtr[] globalWorkSize = workDim == 2
				? [(UIntPtr) workWidth, (UIntPtr) workHeight]
				: [(UIntPtr) pixelsTotal];

			// Create output buffer
			IntPtr outputPointer = IntPtr.Zero;
			if (this.KernelCompiler.PointerInputCount == 0)
			{
				if (logSuccess)
				{
					this.Log("No output buffer needed", "No output buffer", 1);
				}

				return pointer;
			}
			else if (this.KernelCompiler.PointerInputCount == 1)
			{
				if (logSuccess)
				{
					this.Log("Single pointer kernel detected", "Single pointer kernel", 1);
				}
			}
			else if (this.KernelCompiler.PointerInputCount >= 2)
			{
				ClMem? outputMem = this.MemoryRegister.AllocateSingle<byte>((nint)inputMem.GetLengths().FirstOrDefault());
				if (outputMem == null)
				{
					if (logSuccess)
					{
						this.Log("this.LastResultCode allocating output buffer", "", 2);
					}

					return pointer;
				}
				outputPointer = outputMem[0].Handle;
			}

			// Take time
			times.Add(sw.ElapsedMilliseconds - times.Sum());

			// Merge arguments
			List<object> arguments = this.MergeArgumentsImage(variableArguments ?? [], pointer, outputPointer, width, height, channels, bitdepth, false);

			// Set kernel arguments
			for (int i = 0; i < arguments.Count; i++)
			{
				// Set argument
				CLResultCode error2 = this.SetKernelArgSafe((uint) i, arguments[i]);
				if (error2 != CLResultCode.Success)
				{
					this.lastError = error2;
					this.Log("this.LastResultCode setting kernel argument " + i + ": " + error2.ToString(), arguments[i].ToString() ?? "");
					return pointer;
				}
			}

			// Take time
			times.Add(sw.ElapsedMilliseconds - times.Sum());

			// Log arguments
			if (logSuccess)
			{
				this.Log("Kernel arguments set: " + string.Join(", ", argNames.Select((a, i) => a + ": " + arguments[i].ToString())), "'" + kernelName + "'", 2);
			}

			// Exec
			CLResultCode error = CL.EnqueueNDRangeKernel(
				this.QUE,
				this.Kernel.Value,
				workDim,          // 1D oder 2D
				null,             // Kein Offset
				globalWorkSize,   // Work-Größe in Pixeln
				null,             // Lokale Work-Size (automatisch)
				0, null, out CLEvent evt
			);
			if (error != CLResultCode.Success)
			{
				this.lastError = error;
				this.Log("Error executing kernel: " + error.ToString(), "", 2);
				return pointer;
			}

			// Wait for kernel to finish
			error = CL.WaitForEvents(1, [evt]);
			if (error != CLResultCode.Success)
			{
				this.lastError = error;
				this.Log("Error waiting for kernel to finish: " + error.ToString(), "", 2);
				return pointer;
			}

			// Release event
			error = CL.ReleaseEvent(evt);
			if (error != CLResultCode.Success)
			{
				this.lastError = error;
				this.Log("Error releasing event: " + error.ToString(), "", 2);
				return pointer;
			}

			// Take time
			times.Add(sw.ElapsedMilliseconds - times.Sum());
			times.Add(times.Sum());
			sw.Stop();

			// Free input buffer
			long freed;
			if (outputPointer == IntPtr.Zero)
			{
				freed = 0;
			}
			else
			{
				freed = this.MemoryRegister.FreeBuffer(pointer, true);
			}

			// Log success with timeNames
			if (logSuccess)
			{
				this.Log("Kernel executed successfully! Times: " + string.Join(", ", times.Select((t, i) => timeNames[i] + t + "ms")) + "(freed input: " + freed + "MB)", "'" + kernelName + "'", 1);
			}

			// Return valued pointer
			return outputPointer != IntPtr.Zero ? outputPointer : pointer;
		}



		// Helpers
		public List<object> MergeArgumentsAudio(object[] variableArguments, CLBuffer inputBuffer, CLBuffer outputBuffer, long length, int chunkSize, float overlap, int samplerate, int bitdepth, int channels, Dictionary<string, object>? optionalArgs = null, bool log = false)
		{
			List<object> arguments = [];

			// Make overlap to size
			int overlapSize = (int) (overlap * chunkSize);

			// Get argument definitions
			List<string> argNames = this.KernelCompiler.ArgumentNames.ToList();
			// List of Type from the KernelCompiler.ArgumentTypes as Type from string by select 
			List<Type> argTypes = this.KernelCompiler.ArgumentTypes.Select(t => System.Type.GetType(t) ?? typeof(object)).ToList();	
			Dictionary<string, Type> definitions = argNames.Select((name, index) => new KeyValuePair<string, Type>(name, index < argTypes.Count ? argTypes[index] : typeof(object))).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
			if (definitions == null || definitions.Count == 0)
			{
				this.Log("No argument definitions found", "", 2);
				return arguments;
			}

			// Merge args
			int found = 0;
			for (int i = 0; i < definitions.Count; i++)
			{
				string key = definitions.Keys.ElementAt(i);
				Type type = definitions[key];
				if (type.Name.Contains("*") && key.Contains("in"))
				{
					if (log)
					{
						this.Log($"Adding input buffer for key '{key}'", "", 2);
					}
					arguments.Add(inputBuffer);
					found++;
				}
				else if (type.Name.Contains("*") && key.Contains("out"))
				{
					if (log)
					{
						this.Log($"Adding output buffer for key '{key}'", "", 2);
					}
					arguments.Add(outputBuffer);
					found++;
				}
				else if ((type == typeof(long) || type == typeof(int)) && key.Contains("len"))
				{
					if (log)
					{
						this.Log($"Adding length for key '{key}': {(chunkSize > 0 ? chunkSize : length)}", "", 2);
					}
					arguments.Add(chunkSize > 0 ? chunkSize : length);
					found++;
				}
				else if (type == typeof(int) && key.Contains("chunk"))
				{
					if (log)
					{
						this.Log($"Adding chunk size for key '{key}': {chunkSize}", "", 2);
					}
					arguments.Add(chunkSize);
					found++;
				}
				else if (type == typeof(int) && key.Contains("overlap"))
				{
					if (log)
					{
						this.Log($"Adding overlap size for key '{key}': {overlapSize}", "", 2);
					}
					arguments.Add(overlapSize);
					found++;
				}
				else if (type == typeof(int) && key == "samplerate")
				{
					if (log)
					{
						this.Log($"Adding samplerate for key '{key}': {samplerate}", "", 2);
					}
					arguments.Add(samplerate);
					found++;
				}
				else if (type == typeof(int) && key == "bit")
				{
					if (log)
					{
						this.Log($"Adding bitdepth for key '{key}': {bitdepth}", "", 2);
					}
					arguments.Add(bitdepth);
					found++;
				}
				else if (type == typeof(int) && key == "channel")
				{
					if (log)
					{
						this.Log($"Adding channels for key '{key}': {channels}", "", 2);
					}
					arguments.Add(channels);
					found++;
				}
				else
				{
					if (found < variableArguments.Length)
					{
						if (log)
						{
							this.Log($"Adding variable argument for key '{key}': {variableArguments[found]}", "", 2);
						}
						arguments.Add(variableArguments[found]);
						found++;
					}
					else
					{
						if (log)
						{
							this.Log($"Missing variable argument for key '{key}'", "", 2);
						}

						return arguments; // Return early if a required argument is missing
					}
				}
			}

			// Integrate / replace with optional arguments
			if (optionalArgs != null && optionalArgs.Count > 0)
			{
				foreach (var kvp in optionalArgs)
				{
					string key = kvp.Key.ToLowerInvariant();
					object value = kvp.Value;

					// Find matching argument by name
					int index = definitions.Keys.ToList().FindIndex(k => k.ToLower().Contains(key.ToLower()));
					if (index >= 0 && index < arguments.Count)
					{
						if (log)
						{
							this.Log($"Replacing argument '{definitions.Keys.ElementAt(index)}' with optional value: {value}", "", 2);
						}
						arguments[index] = value; // Replace existing argument
					}
					else
					{
						if (log)
						{
							this.Log($"Adding new optional argument '{key}': {value}", "", 2);
						}
						arguments.Add(value); // Add new optional argument
					}
				}
			}

			return arguments;
		}

		public List<object> MergeArgumentsImage(object[] arguments, IntPtr inputPointer = 0, IntPtr outputPointer = 0, int width = 0, int height = 0, int channels = 4, int bitdepth = 8, bool log = false)
		{
			List<object> result = [];

			// Get kernel arguments
			List<string> argNames = this.KernelCompiler.ArgumentNames.ToList();
			List<Type> argTypes = this.KernelCompiler.ArgumentTypes.Select(t => System.Type.GetType(t) ?? typeof(object)).ToList();
			Dictionary<string, Type> definitions = argNames.Select((name, index) => new KeyValuePair<string, Type>(name, index < argTypes.Count ? argTypes[index] : typeof(object))).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
			if (definitions == null || definitions.Count == 0)
			{
				this.Log("No argument definitions found", "", 2);
				return arguments.ToList();
			};

			if (definitions.Count == 0)
			{
				this.Log("Kernel arguments not found", "", 2);
				this.KernelCompiler.TryAnalog = true;
				argNames = this.KernelCompiler.ArgumentNames.ToList();
				argTypes = this.KernelCompiler.ArgumentTypes.Select(t => System.Type.GetType(t) ?? typeof(object)).ToList();
				definitions = argNames.Select((name, index) => new KeyValuePair<string, Type>(name, index < argTypes.Count ? argTypes[index] : typeof(object))).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
				if (definitions == null || definitions.Count == 0)
				{
					this.Log("No argument definitions found", "", 2);
					return arguments.ToList();
				};
			}

			int bpp = bitdepth * channels;

			// Match arguments to kernel arguments
			bool inputFound = false;
			for (int i = 0; i < definitions.Count; i++)
			{
				string argName = definitions.ElementAt(i).Key;
				Type argType = definitions.ElementAt(i).Value;

				// If argument is pointer -> add pointer
				if (argType.Name.EndsWith("*"))
				{
					// Get pointer value
					IntPtr argPointer = 0;
					if (!inputFound)
					{
						argPointer = arguments[i] is IntPtr ? (IntPtr) arguments[i] : inputPointer;
						inputFound = true;
					}
					else
					{
						argPointer = arguments[i] is IntPtr ? (IntPtr) arguments[i] : outputPointer;
					}

					// Get buffer
					ClMem? argBuffer = this.MemoryRegister.GetBuffer(argPointer);
					if (argBuffer == null || (nint)argBuffer.GetLengths().FirstOrDefault() == IntPtr.Zero)
					{
						this.Log("Argument buffer not found or invalid length: " + argPointer.ToString("X16"), argBuffer?.IndexLength.ToString() ?? "None", 2);
						return [];
					}
					CLBuffer buffer = argBuffer[0];

					// Add pointer to result
					result.Add(buffer);

					// Log buffer found
					if (log)
					{
						// Log buffer found
						this.Log("Kernel argument buffer found: " + argPointer.ToString("X16"), "Index: " + i, 3);
					}
				}
				else if (argType == typeof(int))
				{
					// If name is "width" or "height" -> add width or height
					if (argName.ToLower() == "width")
					{
						result.Add(width <= 0 ? arguments[i] : width);

						// Log width found
						if (log)
						{
							this.Log("Kernel argument width found: " + width.ToString(), "Index: " + i, 3);
						}
					}
					else if (argName.ToLower() == "height")
					{
						result.Add(height <= 0 ? arguments[i] : height);

						// Log height found
						if (log)
						{
							this.Log("Kernel argument height found: " + height.ToString(), "Index: " + i, 3);
						}
					}
					else if (argName.ToLower() == "channels")
					{
						result.Add(channels <= 0 ? arguments[i] : channels);

						// Log channels found
						if (log)
						{
							this.Log("Kernel argument channels found: " + channels.ToString(), "Index: " + i, 3);
						}
					}
					else if (argName.ToLower() == "bitdepth")
					{
						result.Add(bitdepth <= 0 ? arguments[i] : bitdepth);

						// Log channels found
						if (log)
						{
							this.Log("Kernel argument bitdepth found: " + bitdepth.ToString(), "Index: " + i, 3);
						}
					}
					else if (argName.ToLower() == "bpp")
					{
						result.Add(bpp <= 0 ? arguments[i] : bpp);

						// Log channels found
						if (log)
						{
							this.Log("Kernel argument bpp found: " + bpp.ToString(), "Index: " + i, 3);
						}
					}
					else
					{
						result.Add((int) arguments[Math.Min(arguments.Length - 1, i)]);
					}
				}
				else if (argType == typeof(float))
				{
					// Sicher konvertieren
					result.Add(Convert.ToSingle(arguments[i]));
				}
				else if (argType == typeof(double))
				{
					result.Add(Convert.ToDouble(arguments[i]));
				}
				else if (argType == typeof(long))
				{
					result.Add((long) arguments[i]);
				}
			}

			// Log arguments
			if (log)
			{
				this.Log("Kernel arguments: " + string.Join(", ", result.Select(a => a.ToString())), "'" + Path.GetFileName(this.KernelFile) + "'", 2);
			}

			return result;
		}


		private CLResultCode SetKernelArgSafe(uint index, object value)
		{
			// Check kernel
			if (this.Kernel == null)
			{
				this.Log("Kernel is null");
				return CLResultCode.InvalidKernelDefinition;
			}

			switch (value)
			{
				case CLBuffer buffer:
					return CL.SetKernelArg(this.Kernel.Value, index, buffer);

				case int i:
					return CL.SetKernelArg(this.Kernel.Value, index, i);

				case long l:
					return CL.SetKernelArg(this.Kernel.Value, index, l);

				case float f:
					return CL.SetKernelArg(this.Kernel.Value, index, f);

				case double d:
					return CL.SetKernelArg(this.Kernel.Value, index, d);

				case byte b:
					return CL.SetKernelArg(this.Kernel.Value, index, b);

				case IntPtr ptr:
					return CL.SetKernelArg(this.Kernel.Value, index, ptr);

				// Spezialfall für lokalen Speicher (Größe als uint)
				case uint u:
					return CL.SetKernelArg(this.Kernel.Value, index, new IntPtr(u));

				// Fall für Vector2
				case Vector2 v:
					// Vector2 ist ein Struct, daher muss es als Array übergeben werden
					return CL.SetKernelArg(this.Kernel.Value, index, v);

				default:
					throw new ArgumentException($"Unsupported argument type: {value?.GetType().Name ?? "null"}");
			}
		}

		private uint GetMaxWorkGroupSize()
		{
			const uint FALLBACK_SIZE = 64;
			const string FUNCTION_NAME = "GetMaxWorkGroupSize";

			if (!this.Kernel.HasValue)
			{
				this.Log("Kernel not initialized", FUNCTION_NAME, 2);
				return FALLBACK_SIZE;
			}

			try
			{
				// 1. Zuerst die benötigte Puffergröße ermitteln
				CLResultCode result = CL.GetKernelWorkGroupInfo(
					this.Kernel.Value,
					this.DEV,
					KernelWorkGroupInfo.WorkGroupSize,
					UIntPtr.Zero,
					null,
					out nuint requiredSize);

				if (result != CLResultCode.Success || requiredSize == 0)
				{
					this.lastError = result;
					this.Log($"Failed to get required size: {result}", FUNCTION_NAME, 2);
					return FALLBACK_SIZE;
				}

				// 2. Puffer mit korrekter Größe erstellen
				byte[] paramValue = new byte[requiredSize];

				// 3. Tatsächliche Abfrage durchführen
				result = CL.GetKernelWorkGroupInfo(
					this.Kernel.Value,
					this.DEV,
					KernelWorkGroupInfo.WorkGroupSize,
					new UIntPtr(requiredSize),
					paramValue,
					out _);

				if (result != CLResultCode.Success)
				{
					this.lastError = result;
					this.Log($"Failed to get work group size: {result}", FUNCTION_NAME, 2);
					return FALLBACK_SIZE;
				}

				// 4. Ergebnis konvertieren (abhängig von der Plattform)
				uint maxSize;
				if (requiredSize == sizeof(uint))
				{
					maxSize = BitConverter.ToUInt32(paramValue, 0);
				}
				else if (requiredSize == sizeof(ulong))
				{
					maxSize = (uint) BitConverter.ToUInt64(paramValue, 0);
				}
				else
				{
					this.Log($"Unexpected return size: {requiredSize}", FUNCTION_NAME, 2);
					return FALLBACK_SIZE;
				}

				// 5. Gültigen Wert sicherstellen
				if (maxSize == 0)
				{
					this.Log("Device reported max work group size of 0", FUNCTION_NAME, 2);
					return FALLBACK_SIZE;
				}

				return maxSize;
			}
			catch (Exception ex)
			{
				this.Log($"this.LastResultCode in {FUNCTION_NAME}: {ex.Message}", ex.StackTrace ?? "", 3);
				return FALLBACK_SIZE;
			}
		}



		// ----- ----- ----- ACCESSIBLE METHODS ----- ----- ----- \\
		
	}
}