using log4net;
using OpenTK.Compute.OpenCL;
using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace OOCL.OpenCl
{
	public class OpenClMemoryRegister : IDisposable, IOpenClObj
	{
		// INTERFACE
		public int Index => 0;
		public string Name => $"OpenCL Memory Register";
		public string Type => "OpenCL-Register";
		public bool Online => this.Queue.Handle != IntPtr.Zero;
		public string Status => this.Online && this.lastError == CLResultCode.Success ? "OK" : $"Error: '{this.LastErrorMessage}'";
		public string LastErrorMessage => this.lastError == CLResultCode.Success ? string.Empty : this.lastError.ToString();
		public IEnumerable<string> ErrorMessages { get; private set; } = [];
		// -----

		private string Repopath;
		private CLContext CTX;
		private CLDevice DEV;
		private CLPlatform PLAT;

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
		public IEnumerable<ClMem> Memory => this.memory.Values;

		public string TotalMemoryBytes => this.GetMemoryTotalPrecise().ToString();

		private long GetMemoryTotalPrecise()
		{
			CLResultCode error = CL.GetDeviceInfo(this.DEV, DeviceInfo.GlobalMemorySize, out byte[] code);
			if (error != CLResultCode.Success)
			{
				this.lastError = error;
				this.Log("Failed to get device memory info", error.ToString(), 1);
				return -1;
			}

			long totalMemory = BitConverter.ToInt64(code, 0);
			return totalMemory;
		}

		public string UsedMemoryBytes => this.GetMemoryUsedPrecise().ToString();

		private long GetMemoryUsedPrecise()
		{
			long totalSize = 0;
			lock (this._memoryLock)
			{
				if (this.memory.Count == 0)
				{
					return 0;
				}

				// Get total size of all buffers
				totalSize = this.memory.Values.Sum(mem => mem.GetSize());
			}

			return totalSize;
		}

		public string FreeMemoryBytes => this.GetMemoryFreePrecise().ToString();

		private long GetMemoryFreePrecise()
		{
			// Get total memory and usage
			long totalMemory = this.GetMemoryTotalPrecise();
			long usedMemory = this.GetMemoryUsedPrecise();

			if (totalMemory < 0 || usedMemory < 0)
			{
				return -1;
			}

			long freeMemory = totalMemory - usedMemory;

			return freeMemory;
		}

		public float UsagePercentage
		{
			get => this.GetMemoryTotalPrecise() > 0
				? (float) this.GetMemoryUsedPrecise() / this.GetMemoryTotalPrecise() * 100f
				: 0f;
		}

		public string UsagePercentageString => this.UsagePercentage.ToString("F5");

		// Log4net path
		public bool EnableLogging { get; set; } = true;
		public string logPath => this.Repopath + "/_Logs/" + (this.GetType().Name ?? this.Type) + ".log";
		private readonly ILog logger = LogManager.GetLogger(typeof(OpenClMemoryRegister));


		// ----- ----- ----- ATTRIBUTES ----- ----- ----- \\
		private CLCommandQueue QUE;
		public CLCommandQueue Queue => this.QUE;

		private readonly object _memoryLock = new();
		private ConcurrentDictionary<Guid, ClMem> memory = [];




		// ----- ----- ----- CONSTRUCTORS ----- ----- ----- \\
		public OpenClMemoryRegister(string repopath, CLContext context, CLDevice device, CLPlatform platform)
		{
			this.Repopath = repopath;
			this.CTX = context;
			this.DEV = device;
			this.PLAT = platform;

			// Init. queue
			this.QUE = CL.CreateCommandQueueWithProperties(this.CTX, this.DEV, 0, out CLResultCode error);
			if (error != CLResultCode.Success)
			{
				this.lastError = error;
				this.Log("Failed to create CL-CommandQueue.");
			}
		}




		// ----- ----- ----- METHODS ----- ----- ----- \\
		public void Log(string message = "", string inner = "", int indent = 0)
		{
			string msg = "[Memory]: " + new string(' ', indent * 2) + message;

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

			this.logger.Info(msg);
		}

		// Dispose
		public void Dispose()
		{
			// Dispose every memory buffer
			foreach (ClMem mem in this.memory.Values)
			{
				List<CLBuffer> buffers = mem.GetBuffers();
				foreach (CLBuffer buffer in buffers)
				{
					CLResultCode err = CL.ReleaseMemoryObject(buffer);
					if (err != CLResultCode.Success)
					{
						this.Log("Failed to release buffer", buffer.Handle.ToString("X16"), 1);
					}
				}
			}

			lock (this._memoryLock)
			{
				this.memory.Clear();
			}
		}


		// Free buffer
		public long FreeBuffer(IntPtr pointer, bool readable = false)
		{
			ClMem? mem = this.GetBuffer(pointer);
			if (mem == null)
			{
				// If no buffer found, return 0
				this.Log("No buffer found to free", pointer.ToString("X16"));
				return 0;
			}

			long freedSizeBytes = mem.GetSize(!readable);

			List<CLBuffer> buffers = mem.GetBuffers();
			foreach (CLBuffer buffer in buffers)
			{
				// Free the buffer
				CLResultCode error = CL.ReleaseMemoryObject(buffer);
				if (error != CLResultCode.Success)
				{
					this.lastError = error;
					this.Log("Failed to release buffer", buffer.Handle.ToString("X16"), 1);
				}
			}

			// Remove from memory list
			this.memory.TryRemove(mem.Id, out _);

			// Make readable if requested
			if (readable)
			{
				freedSizeBytes /= 1024 * 1024; // Convert to MB
			}

			return freedSizeBytes;
		}

		// Buffer info
		public Type? GetBufferType(IntPtr pointer)
		{
			ClMem? mem = this.GetBuffer(pointer);
			if (mem == null || mem.GetCount() <= 0)
			{
				this.Log("No memory found for pointer", pointer.ToString("X16"));
				return null;
			}

			// Return the type of the first buffer
			return System.Type.GetType(mem.Type) ?? typeof(void);
		}

		public ClMem? GetBuffer(IntPtr pointer)
		{
			// Sperre den Zugriff auf die Memory-Liste während der Iteration
			lock (this._memoryLock)
			{
				return this.memory.Values.FirstOrDefault(mem => mem.IndexHandle == pointer.ToString("X16") || mem.IndexHandle == pointer.ToString(""));
			}
		}

		// Single buffer
		public ClMem? PushData<T>(T[] data) where T : unmanaged
		{
			// Check data
			if (data.LongLength < 1)
			{
				return null;
			}

			// Get IntPtr length
			IntPtr length = new(data.LongLength);

			// Create buffer
			CLBuffer buffer = CL.CreateBuffer<T>(this.CTX, MemoryFlags.CopyHostPtr | MemoryFlags.ReadWrite, data, out CLResultCode error);
			if (error != CLResultCode.Success)
			{
				this.lastError = error;
				this.Log("Error creating CL-Buffer", error.ToString());
				return null;
			}

			// Add to list
			ClMem mem = new(buffer, length, typeof(T));

			// Lock memory list to avoid concurrent access issues
			lock (this._memoryLock)
			{
				// Add to memory list
				this.memory.TryAdd(mem.Id, mem);
			}

			return mem;
		}

		public T[] PullData<T>(IntPtr pointer, bool keep = false) where T : unmanaged
		{
			// Get buffer & length
			ClMem? mem = this.GetBuffer(pointer);
			if (mem == null || mem.GetCount() <= 0)
			{
				return [];
			}

			// New array with length
			long length = mem.GetLengths().FirstOrDefault();
			T[] data = new T[length];

			// Read buffer
			CLResultCode error = CL.EnqueueReadBuffer(
				this.QUE,
				mem[0],
				true,
				0,
				data,
				null,
				out CLEvent @event
			);

			// Check error
			if (error  != CLResultCode.Success)
			{
				this.lastError = error;
				this.Log("Failed to read buffer", error.ToString(), 1);
				return [];
			}

			// If not keeping, free buffer
			if (!keep)
			{
				this.FreeBuffer(pointer);
			}

			// Return data
			return data;
		}

		public ClMem? AllocateSingle<T>(IntPtr size) where T : unmanaged
		{
			// Check size
			if (size.ToInt64() < 1)
			{
				return null;
			}

			// Create empty array of type and size
			T[] data = new T[size.ToInt64()];
			data = data.Select(x => default(T)).ToArray();

			// Create buffer
			CLBuffer buffer = CL.CreateBuffer<T>(this.CTX, MemoryFlags.CopyHostPtr | MemoryFlags.ReadWrite, data, out CLResultCode error);
			if (error != CLResultCode.Success)
			{
				this.lastError = error;
				this.Log("Error creating CL-Buffer", error.ToString());
				return null;
			}

			// Add to list
			ClMem mem = new(buffer, size, typeof(T));

			// Lock memory list to avoid concurrent access issues
			lock (this._memoryLock)
			{
				// Add to memory list
				this.memory.TryAdd(mem.Id, mem);
			}

			// Return mem obj
			return mem;
		}

		// Array buffers
		public ClMem? PushChunks<T>(List<T[]> chunks) where T : unmanaged
		{
			// Check chunks
			if (chunks.Count < 1 || chunks.Any(chunk => chunk.LongLength < 1))
			{
				return null;
			}

			// Get IntPtr[] lengths
			IntPtr[] lengths = chunks.Select(chunk => new IntPtr(chunk.LongLength)).ToArray();

			// Create buffers for each chunk
			CLBuffer[] buffers = new CLBuffer[chunks.Count];
			for (int i = 0; i < chunks.Count; i++)
			{
				buffers[i] = CL.CreateBuffer(this.CTX, MemoryFlags.CopyHostPtr | MemoryFlags.ReadWrite, chunks[i], out CLResultCode error);
				if (error != CLResultCode.Success)
				{
					this.lastError = error;
					this.Log("Error creating CL-Buffer for chunk " + i);
					return null;
				}
			}

			// Add to list
			ClMem mem = new(buffers, lengths, typeof(T));

			// Lock memory list to avoid concurrent access issues
			lock (this._memoryLock)
			{
				// Add to memory list
				this.memory.TryAdd(mem.Id, mem);
			}

			// Return mem obj
			return mem;
		}

		public List<T[]> PullChunks<T>(IntPtr indexPointer, bool keep = false) where T : unmanaged
		{
			// Get clmem by index pointer
			ClMem? mem = this.GetBuffer(indexPointer);
			if (mem == null || mem.GetCount() < 1)
			{
				this.Log("No memory found for index pointer", indexPointer.ToString("X16"));
				return [];
			}

			// Chunk list & lengths
			List<T[]> chunks = [];
			IntPtr[] lengths = mem.GetLengths().Select(l => (nint)l).ToArray();

			// Read every buffer
			for (int i = 0; i < mem.GetCount(); i++)
			{
				T[] chunk = new T[lengths[i].ToInt64()];
				CLResultCode error = CL.EnqueueReadBuffer(
					this.QUE,
					mem[i],
					true,
					0,
					chunk,
					null,
					out CLEvent @event
				);

				if (error != CLResultCode.Success)
				{
					this.lastError = error;
					this.Log("Failed to read buffer for chunk " + i, error.ToString(), 1);
					return [];
				}

				chunks.Add(chunk);
			}

			// Optionally free buffer
			if (!keep)
			{
				// Free the memory if not keeping
				this.FreeBuffer(indexPointer);
			}

			// Return chunks
			return chunks;
		}

		public ClMem? AllocateGroup<T>(IntPtr count, IntPtr size) where T : unmanaged
		{
			// Check count and size
			if (count < 1 || size.ToInt64() < 1)
			{
				return null;
			}

			// Create array of IntPtr for handles
			CLBuffer[] buffers = new CLBuffer[count];
			IntPtr[] lengths = new IntPtr[count];
			Type type = typeof(T);

			// Allocate each buffer
			for (int i = 0; i < count; i++)
			{
				buffers[i] = CL.CreateBuffer<T>(this.CTX, MemoryFlags.CopyHostPtr | MemoryFlags.ReadWrite, new T[size.ToInt64()], out CLResultCode error);
				if (error != CLResultCode.Success)
				{
					this.lastError = error;
					this.Log("Error creating CL-Buffer for group " + i, error.ToString(), 1);
					return null;
				}

				lengths[i] = size;
			}

			ClMem mem = new(buffers, lengths, type);

			// Lock memory list to avoid concurrent access issues
			lock (this._memoryLock)
			{
				this.memory.TryAdd(mem.Id, mem);
			}

			return mem;
		}






		// ----- ----- ----- ACCESSIBLE METHODS ----- ----- ----- \\
		public int GetMemoryTotal(bool asBytes = false)
		{
			long maxMemory = 0;

			try
			{
				// Get maximum available memory on device
				CLResultCode error = CL.GetDeviceInfo(this.DEV, DeviceInfo.GlobalMemorySize, out byte[] code);
				if (error != CLResultCode.Success)
				{
					this.lastError = error;
					this.Log("Failed to get device memory info", error.ToString(), 1);
					return -1;
				}

				maxMemory = BitConverter.ToInt64(code, 0);

				if (!asBytes)
				{
					maxMemory /= 1024 * 1024; // Convert to MB
				}

				return (int)maxMemory;
			}
			catch (Exception ex)
			{
				this.Log("Error getting total memory", ex.Message, 1);
				return -1;
			}
		}

		public int GetMemoryUsed(bool asBytes = false)
		{
			long totalSize = 0;
			lock (this._memoryLock)
			{
				if (this.memory.Count == 0)
				{
					return 0;
				}

				// Get total size of all buffers
				totalSize = this.memory.Values.Sum(mem => mem.GetSize());
			}
			
			// Convert to MB if readable
			if (!asBytes)
			{
				totalSize /= 1024 * 1024;
			}
			return (int)totalSize;
		}

		public int GetMemoryFree(bool asBytes = false)
		{
			// Get total memory and usage
			long totalMemory = this.GetMemoryTotal(asBytes);
			long usedMemory = this.GetMemoryUsed(asBytes);
			
			if (totalMemory < 0 || usedMemory < 0)
			{
				return -1;
			}

			long freeMemory = totalMemory - usedMemory;
			if (!asBytes)
			{
				freeMemory /= 1024 * 1024;
			}

			return (int)freeMemory;
		}


	}



	public class ClMem : IOpenClObj
	{
		// INTERFACE
		public Guid Id { get; } = Guid.NewGuid();
		public int Index => 0;
		public bool Online => true;
		public string Status => this.Buffers.LongLength == this.lengths.LongLength ? "OK" : "Error: Lengths mismatch";
		public IEnumerable<string> ErrorMessages => [];
		public string Name => $"OpenCL Memory {this.Id.ToString()}";
		// -----


		private DateTime createdAt = DateTime.Now;
		public string Timestamp => this.createdAt.ToString("yyyy-MM-dd HH:mm:ss.fff");
		public string TickAge => (DateTime.Now.Ticks - this.createdAt.Ticks).ToString();
		
		private CLBuffer[] Buffers { get; set; } = [];
		public CLBuffer this[int index] => index >= 0 && index < this.Buffers.Length ? this.Buffers[index] : this.Buffers[0];

		private IntPtr[] lengths { get; set; } = [];
		public IEnumerable<string> Lengths => this.lengths.Select(length => length.ToString());

		private System.Type elementType { get; set; } = typeof(void);
		public string Type => this.elementType.Name;

		public bool AsHex { get; set; } = false;
		private string formatting => this.AsHex ? "X16" : "";


		public bool IsSingle => this.Buffers.Length == 1;
		public bool IsArray => this.Buffers.Length > 1;

		private IntPtr count => (nint) this.Buffers.LongLength;
		public string Count => this.count.ToString();

		public string TotalLength => this.GetTotalLength().ToString();


		public int TypeSize => Marshal.SizeOf(this.elementType);
		private IntPtr size => (nint) this.lengths.Sum(length => length.ToInt64() * this.TypeSize);
		public string Size => this.size.ToString();
		
		private IntPtr[] pointers => this.Buffers.Select(buffer => buffer.Handle).ToArray();
		public IEnumerable<string> Pointers => this.pointers.Select(p => p.ToString(this.formatting));


		private IntPtr indexHandle => this.Buffers.FirstOrDefault().Handle;
		public string IndexHandle => this.indexHandle.ToString(this.formatting);
		
		private IntPtr indexLength => this.lengths.FirstOrDefault();
		public string IndexLength => this.indexLength.ToString();



		public ClMem(CLBuffer[] buffers, IntPtr[] lengths, Type? elementType = null)
		{
			this.Buffers = buffers;
			this.lengths = lengths;
			this.elementType = elementType ?? typeof(void);
		}

		public ClMem(CLBuffer buffer, IntPtr length, Type? elementType = null)
		{
			this.Buffers = [buffer];
			this.lengths = [length];
			this.elementType = elementType ?? typeof(void);
		}



		public int GetSize(bool asBytes = false)
		{
			long size = this.size.ToInt64();
			if (!asBytes)
			{
				size /= 1024 * 1024; // Convert to MB
			}

			return (int)size;
		}

		public int GetCount()
		{
			return (int)this.count.ToInt64();
		}

		public long GetTotalLength()
		{
			return this.lengths.Sum(length => length.ToInt64());
		}

		public List<long> GetLengths()
		{
			return this.lengths.Select(length => length.ToInt64()).ToList();
		}

		public List<CLBuffer> GetBuffers()
		{
			return this.Buffers.ToList();
		}

	}
}
