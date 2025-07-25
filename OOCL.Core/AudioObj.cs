using OOCL.Core.CommonStaticMethods;
using NAudio.Wave;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Linq;

namespace OOCL.Core
{
	public class AudioObj : IDisposable, IMediaObj
	{
		// ----- ----- ----- ATTRIBUTES ----- ----- ----- \\
		public Guid Id { get; } = Guid.Empty;
		public string Filepath { get; set; }
		public string Name { get; set; }
		public float[] Data { get; set; } = [];
		public int Samplerate { get; set; } = -1;
		public int Bitdepth { get; set; } = -1;
		public int Channels { get; set; } = -1;
		public int Length { get; set; } = -1;

		public long Pointer { get; set; } = 0;
		public string PointerHex => this.Pointer == 0 ? "0" : this.Pointer.ToString("X");
		public string DataType => "float";
		public string DataStructure { get; set; } = "[]";
		public int ChunkSize { get; set; } = 0;
		public int OverlapSize { get; set; } = 0;
		public string Form { get; set; } = "f";
		public double StretchFactor { get; set; } = 1.0;

		public string Meta => this.GetMetaString();
		public string Base64Image => this.AsBase64Image().Result;

		public float Bpm { get; set; } = 0.0f;

		public WaveOutEvent Player { get; set; } = new WaveOutEvent();
		public bool Playing => this.Player.PlaybackState == PlaybackState.Playing;
		public int Volume { get; set; } = 100;
		private long SizeInBytes = 0;
		public float SizeMb => this.SizeInBytes / (1024 * 1024);
		public double Duration => (this.Samplerate > 0 && this.Channels > 0) ? (double) this.Length / (this.Samplerate * this.Channels) : 0;

		// ----- ----- ----- PROPERTIES ----- ----- ----- \\
		private long position
		{
			get
			{
				return this.Player == null || this.Player.PlaybackState != PlaybackState.Playing
					? 0
					: this.Player.GetPosition() / (this.Channels * (this.Bitdepth / 8));
			}
		}

		public double CurrentTime
		{
			get
			{
				return this.Samplerate <= 0 ? 0 : (double) this.position / this.Samplerate;
			}
		}

		public bool OnHost => this.Data.Length > 0 && this.Pointer == 0;
		public bool OnDevice => this.Data.Length == 0 && this.Pointer != 0;

		public Image<Rgba32> WaveformImage { get; set; } = new Image<Rgba32>(100, 100);
		public Image<Rgba32> WaveformImageSimple { get; set; } = new Image<Rgba32>(100, 100);
		public Size WaveformSize { get; set; } = new Size(1000, 200);
		public int WaveformWidth
		{
			get => this.WaveformSize.Width;
			set => this.WaveformSize = new Size(value, this.WaveformSize.Height);
		}

		public int WaveformHeight
		{
			get => this.WaveformSize.Height;
			set => this.WaveformSize = new Size(this.WaveformSize.Width, value);
		}

		private System.Timers.Timer WaveformUpdateTimer
		{
			get
			{
				return this.SetupTimer(this.WaveformUpdateFrequency);
			}
			set
			{
				if (value != null)
				{
					value.Interval = 1000.0 / this.WaveformUpdateFrequency;
				}
			}
		}
		public int WaveformUpdateFrequency { get; set; } = 100;



		// ----- ----- ----- CONSTRUCTOR ----- ----- ----- \\
		public AudioObj(string filepath)
		{
			this.Id = Guid.NewGuid();
			this.Filepath = filepath;
			this.Name = Path.GetFileNameWithoutExtension(filepath);
			this.LoadAudioFile().Wait();
		}

		public AudioObj(IEnumerable<float> data, int samplerate = 44100, int channels = 1, int bitdepth = 16, string name = "Empty_")
		{
			this.Id = Guid.NewGuid();
			this.Data = data.ToArray();
			this.Name = name;
			this.Filepath = "N/A";
			this.Samplerate = samplerate;
			this.Channels = channels;
			this.Bitdepth = bitdepth;
			this.Length = this.Data.Length;
		}

		public System.Timers.Timer SetupTimer(int hz = 100)
		{
			hz = Math.Clamp(hz, 1, 144);

			var timer = new System.Timers.Timer(1000.0 / hz);

			// Register tick event
			timer.Elapsed += (sender, e) =>
			{
				if (this.Data != null && this.Data.Length > 0)
				{
					this.WaveformImage = this.GetWaveformImageAsync(this.Data, this.WaveformSize.Width, this.WaveformSize.Height).Result;
					this.WaveformImageSimple = this.GetWaveformImageAsync(this.Data, 1000, 200).Result;
				}
			};

			timer.AutoReset = true;
			timer.Enabled = true;

			return timer;
		}

		public async Task LoadAudioFile()
		{
			if (string.IsNullOrEmpty(this.Filepath))
			{
				throw new FileNotFoundException("File path is empty");
			}

			try
			{
				using AudioFileReader reader = new(this.Filepath);
				this.Samplerate = reader.WaveFormat.SampleRate;
				this.Bitdepth = reader.WaveFormat.BitsPerSample;
				this.Channels = reader.WaveFormat.Channels;
				this.Length = (int)(reader.Length / 4);

				long numSamples = reader.Length / (reader.WaveFormat.BitsPerSample / 8);
				this.Data = new float[numSamples];

				int read = await Task.Run(() =>
				{
					lock (reader)
					{
						return reader.Read(this.Data, 0, (int) numSamples);
					}
				});
				if (read != numSamples)
				{
					lock(this.Data)
					{
						float[] resizedData = new float[read];
						Array.Copy(this.Data, resizedData, read);
						this.Data = resizedData;
					}
				}

				this.SizeInBytes = this.Data.Length * (this.Bitdepth / 8) * this.Channels;
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"Error loading audio file '{this.Filepath}': {ex.Message} ({ex.InnerException?.Message ?? " - "})");

				this.Dispose();
			}

			this.ReadBpmTag();
		}

		private float ReadBpmTag(string tag = "TBPM", bool set = true)
		{
			// Read bpm metadata if available
			float bpm = 0.0f;
			float roughBpm = 0.0f;

			try
			{
				if (!string.IsNullOrEmpty(this.Filepath) && File.Exists(this.Filepath))
				{
					using (var file = TagLib.File.Create(this.Filepath))
					{
						if (file.Tag.BeatsPerMinute > 0)
						{
							roughBpm = (float) file.Tag.BeatsPerMinute;
						}
						if (file.TagTypes.HasFlag(TagLib.TagTypes.Id3v2))
						{
							var id3v2Tag = (TagLib.Id3v2.Tag) file.GetTag(TagLib.TagTypes.Id3v2);

							var tagTextFrame = TagLib.Id3v2.TextInformationFrame.Get(id3v2Tag, tag, false);

							if (tagTextFrame != null && tagTextFrame.Text.Any())
							{
								string bpmString = tagTextFrame.Text.FirstOrDefault() ?? "0,0";
								if (!string.IsNullOrEmpty(bpmString))
								{
									bpmString = bpmString.Replace(',', '.');

									if (float.TryParse(bpmString, NumberStyles.Any, CultureInfo.InvariantCulture, out float parsedBpm))
									{
										bpm = parsedBpm;
									}
								}
							}
							else
							{
								bpm = 0.0f;
							}
						}
					}
				}
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"Fehler beim Lesen des Tags {tag.ToUpper()}: {ex.Message} ({ex.InnerException?.Message ?? " - "})");
			}

			// Take rough bpm if <= 0.0f
			if (bpm <= 0.0f && roughBpm > 0.0f)
			{
				Console.WriteLine($"No value found for '{tag.ToUpper()}', taking rough BPM value from legacy tag.");
				bpm = roughBpm;
			}

			if (set)
			{
				this.Bpm = bpm;
			}

			return bpm;
		}

		public async Task<string> AsBase64Image(string format = "bmp")
		{
			if (this.WaveformImage == null)
			{
				return string.Empty;
			}

			try
			{
				using (var imgClone = this.WaveformImage.CloneAs<Rgba32>())
				using (var ms = new MemoryStream())
				{
					IImageEncoder encoder = format.ToLower() switch
					{
						"png" => new SixLabors.ImageSharp.Formats.Png.PngEncoder(),
						"jpeg" or "jpg" => new SixLabors.ImageSharp.Formats.Jpeg.JpegEncoder(),
						"gif" => new SixLabors.ImageSharp.Formats.Gif.GifEncoder(),
						_ => new SixLabors.ImageSharp.Formats.Bmp.BmpEncoder()
					};

					await imgClone.SaveAsync(ms, encoder);
					return Convert.ToBase64String(ms.ToArray());
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Base64 conversion error: {ex}");
				return string.Empty;
			}
		}

		public async Task<IEnumerable<byte>> GetBytes(int maxWorkers = -2)
		{
			if (this.Data == null || this.Data.Length == 0)
			{
				return [];
			}

			// Negative maxWorkers means subtract from total (to have free workers left)
			maxWorkers = CommonStatics.AdjustWorkersCount(maxWorkers);

			int bytesPerSample = this.Bitdepth / 8;
			byte[] result = new byte[this.Data.Length * bytesPerSample];

			await Task.Run(() =>
			{
				var options = new ParallelOptions
				{
					MaxDegreeOfParallelism = maxWorkers
				};

				Parallel.For(0, this.Data.Length, options, i =>
				{
					float sample = this.Data[i];

					switch (this.Bitdepth)
					{
						case 8:
							result[i] = (byte) (sample * 127f);
							break;

						case 16:
							short sample16 = (short) (sample * short.MaxValue);
							Span<byte> target16 = result.AsSpan(i * 2, 2);
							BitConverter.TryWriteBytes(target16, sample16);
							break;

						case 24:
							int sample24 = (int) (sample * 8_388_607f); // 2^23 - 1
							Span<byte> target24 = result.AsSpan(i * 3, 3);
							target24[0] = (byte) sample24;
							target24[1] = (byte) (sample24 >> 8);
							target24[2] = (byte) (sample24 >> 16);
							break;

						case 32:
							Span<byte> target32 = result.AsSpan(i * 4, 4);
							BitConverter.TryWriteBytes(target32, sample);
							break;
					}
				});
			});

			return result;
		}

		public async Task<IEnumerable<float[]>> GetChunks(int size = 2048, float overlap = 0.5f, bool keepData = false, int maxWorkers = -2)
		{
			// Input Validation (sync part for fast fail)
			if (this.Data == null || this.Data.Length == 0)
			{
				return [];
			}

			if (size <= 0 || overlap < 0 || overlap >= 1)
			{
				return [];
			}

			// Calculate chunk metrics (sync)
			this.ChunkSize = size;
			this.OverlapSize = (int) (size * overlap);
			int step = size - this.OverlapSize;
			int numChunks = (this.Data.Length - size) / step + 1;

			// Prepare result array
			float[][] chunks = new float[numChunks][];

			await Task.Run(() =>
			{
				// Parallel processing with optimal worker count
				Parallel.For(0, numChunks, new ParallelOptions
				{
					MaxDegreeOfParallelism = CommonStatics.AdjustWorkersCount(maxWorkers)
				}, i =>
				{
					int sourceOffset = i * step;
					float[] chunk = new float[size];
					Buffer.BlockCopy( // Faster than Array.Copy for float[]
						src: this.Data,
						srcOffset: sourceOffset * sizeof(float),
						dst: chunk,
						dstOffset: 0,
						count: size * sizeof(float));
					chunks[i] = chunk;
				});
			});

			// Cleanup if requested
			if (!keepData)
			{
				this.Data = [];
			}

			return chunks;
		}

		public async Task AggregateStretchedChunks(IEnumerable<float[]> chunks, bool keepPointer = false, int maxWorkers = -2)
		{
			if (chunks == null || chunks.LongCount()== 0)
			{
				return;
			}

			// Pre-calculate all values that don't change
			double stretchFactor = this.StretchFactor;
			int chunkSize = this.ChunkSize;
			int overlapSize = this.OverlapSize;
			int originalHopSize = chunkSize - overlapSize;
			int stretchedHopSize = (int) Math.Round(originalHopSize * stretchFactor);
			int outputLength = (chunks.Count() - 1) * stretchedHopSize + chunkSize;

			// Create window function (cosine window)
			double[] window = await Task.Run(() =>
				Enumerable.Range(0, chunkSize)
						  .Select(i => 0.5 * (1.0 - Math.Cos(2.0 * Math.PI * i / (chunkSize - 1))))
						  .ToArray()  // Korrekte Methode ohne Punkt
			).ConfigureAwait(false);

			// Initialize accumulators in parallel
			double[] outputAccumulator = new double[outputLength];
			double[] weightSum = new double[outputLength];

			await Task.Run(() =>
			{
				var parallelOptions = new ParallelOptions
				{
					MaxDegreeOfParallelism = CommonStatics.AdjustWorkersCount(maxWorkers)
				};

				// Phase 1: Process chunks in parallel
				Parallel.For(0, chunks.LongCount(), parallelOptions, chunkIndex =>
				{
					var chunk = chunks.ElementAt((int)chunkIndex);
					int offset = (int)chunkIndex * stretchedHopSize;

					for (int j = 0; j < Math.Min(chunkSize, chunk.Length); j++)
					{
						int idx = offset + j;
						if (idx >= outputLength)
						{
							break;
						}

						double windowedSample = chunk[j] * window[j];

						// Using Interlocked for thread-safe accumulation
						Interlocked.Exchange(ref outputAccumulator[idx], outputAccumulator[idx] + windowedSample);
						Interlocked.Exchange(ref weightSum[idx], weightSum[idx] + window[j]);
					}
				});

				// Phase 2: Normalize results
				float[] finalOutput = new float[outputLength];
				Parallel.For(0, outputLength, parallelOptions, i =>
				{
					finalOutput[i] = weightSum[i] > 1e-6
						? (float) (outputAccumulator[i] / weightSum[i])
						: 0.0f;
				});

				// Final assignment (thread-safe)
				this.Data = finalOutput;
				this.Length = finalOutput.Length;
				this.Pointer = keepPointer ? this.Pointer : IntPtr.Zero;
			}).ConfigureAwait(false);

			this.SizeInBytes = this.Data.Length * (this.Bitdepth / 8) * this.Channels;
		}

		public async Task Play(CancellationToken cancellationToken,
						  Action? onPlaybackStopped = null,
						  float? initialVolume = null)
		{
			initialVolume ??= this.Volume / 100f;

			// Stop any existing playback and cleanup
			this.WaveformUpdateTimer.Stop();
			this.Player?.Stop();
			this.Player?.Dispose();

			if (this.Data == null || this.Data.Length == 0)
			{
				return;
			}

			try
			{
				// Initialize player with cancellation support
				this.Player = new WaveOutEvent
				{
					Volume = initialVolume ?? 1.0f,
					DesiredLatency = 100 // Lower latency for better responsiveness
				};

				// Async audio data preparation with cancellation
				byte[] bytes = (await Task.Run(() => this.GetBytes(), cancellationToken)
									   .ConfigureAwait(false)).AsParallel().ToArray();

				var waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(this.Samplerate, this.Channels);
				var memoryStream = new MemoryStream(bytes);
				var audioStream = new RawSourceWaveStream(memoryStream, waveFormat);

				// Setup playback stopped handler
				this.Player.PlaybackStopped += (sender, args) =>
				{
					try
					{
						onPlaybackStopped?.Invoke();
					}
					finally
					{
						audioStream.Dispose();
						memoryStream.Dispose();
						this.Player?.Dispose();
					}
				};

				// Register cancellation callback
				using (cancellationToken.Register(() =>
				{
					this.Player?.Stop();
					this.WaveformUpdateTimer.Stop();
				}))
				{
					// Start playback in background
					this.Player.Init(audioStream);
					this.WaveformUpdateTimer.Start();

					// Non-blocking play (fire-and-forget with error handling)
					_ = Task.Run(() =>
					{
						try
						{
							this.Player.Play();
							while (this.Player.PlaybackState == PlaybackState.Playing)
							{
								cancellationToken.ThrowIfCancellationRequested();
								Thread.Sleep(50); // Reduce CPU usage
							}
						}
						catch (OperationCanceledException)
						{
							// Cleanup handled by cancellation callback
						}
						catch (Exception ex)
						{
							Debug.WriteLine($"Playback error: {ex.Message}");
						}
					}, cancellationToken);
				}

				// Return immediately (non-blocking)
			}
			catch (OperationCanceledException)
			{
				Debug.WriteLine("Playback preparation was canceled");
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"Playback initialization failed: {ex.Message}");
				this.Player?.Dispose();
				throw;
			}
		}

		public void Stop()
		{
			this.WaveformUpdateTimer.Stop();
			this.Player.Stop();
		}

		public async Task Reload()
		{
			this.Pointer = 0;
			this.Form = "f";
			this.ChunkSize = 0;
			this.OverlapSize = 0;
			await this.LoadAudioFile();
		}

		public async Task Normalize(float maxAmplitude = 1.0f)
		{
			if (this.Data == null || this.Data.Length == 0)
			{
				return;
			}

			// Phase 1: Find global maximum (parallel + async)
			float globalMax = await Task.Run(() =>
			{
				float max = 0f;
				Parallel.For(0, this.Data.Length,
					() => 0f,
					(i, _, localMax) => Math.Max(Math.Abs(this.Data[i]), localMax),
					localMax => { lock (this) { max = Math.Max(max, localMax); } }
				);
				return max;
			}).ConfigureAwait(false);

			if (globalMax == 0f)
			{
				return;
			}

			// Phase 2: Apply scaling (parallel + async)
			float scale = maxAmplitude / globalMax;
			await Task.Run(() =>
			{
				Parallel.For(0, this.Data.Length, i =>
				{
					this.Data[i] *= scale;
				});
			}).ConfigureAwait(false);
		}

		public async Task<Image<Rgba32>> GetWaveformImageAsync(float[]? data, int width = 720, int height = 480,
			int samplesPerPixel = 128, float amplifier = 1.0f, long offset = 0,
			Color? graphColor = null, Color? backgroundColor = null, bool smoothEdges = true, int workerCount = -2)
		{
			// Normalize image dimensions
			width = Math.Max(100, width);
			height = Math.Max(100, height);

			// Normalize colors & get rgba values
			graphColor ??= Color.BlueViolet;
			backgroundColor ??= Color.White;

			// New result image + color fill
			Image<Rgba32> image = new(width, height);
			await Task.Run(() =>
			{
				image.Mutate(ctx => ctx.BackgroundColor(backgroundColor.Value));
			});

			// Verify data
			data ??= this.Data;
			if (data == null || data.LongLength <= 0)
			{
				return image;
			}

			// Adjust offset if necessary
			if (data.Length <= offset)
			{
				offset = 0;
			}

			workerCount = CommonStatics.AdjustWorkersCount(workerCount);

			// Calculate the number of samples to process -> take array from data
			long totalSamples = Math.Min(data.Length - offset, width * samplesPerPixel);
			if (totalSamples <= 0)
			{
				return image;
			}

			float[] samples = new float[totalSamples];
			Array.Copy(data, offset, samples, 0, totalSamples);

			// Split into concurrent chunks for each worker (even count, last chunk fills up with zeros)
			int chunkSize = (int) Math.Ceiling((double) totalSamples / workerCount);
			ConcurrentDictionary<int, float[]> chunks = await Task.Run(() =>
			{
				ConcurrentDictionary<int, float[]> chunkDict = new();
				Parallel.For(0, workerCount, i =>
				{
					int start = i * chunkSize;
					int end = Math.Min(start + chunkSize, (int) totalSamples);
					if (start < totalSamples)
					{
						float[] chunk = new float[chunkSize];
						Array.Copy(samples, start, chunk, 0, end - start);
						chunkDict[i] = chunk;
					}
				});
				return chunkDict;
			});

			// Draw each chunk at the corresponding position with amplification per worker on bitmap
			Parallel.ForEach(chunks, parallelOptions: new ParallelOptions { MaxDegreeOfParallelism = workerCount }, kvp =>
			{
				int workerIndex = kvp.Key;
				float[] chunk = kvp.Value;
				int xStart = workerIndex * (width / workerCount);
				int xEnd = (workerIndex + 1) * (width / workerCount);
				if (xEnd > width)
				{
					xEnd = width;
				}
				for (int x = xStart; x < xEnd; x++)
				{
					int sampleIndex = (x - xStart) * samplesPerPixel;
					if (sampleIndex < chunk.Length)
					{
						float sampleValue = chunk[sampleIndex] * amplifier;
						int yPos = (int) ((sampleValue + 1.0f) / 2.0f * height);
						yPos = Math.Clamp(yPos, 0, height - 1);

						// Draw vertical line for this sample
						for (int y = 0; y < height; y++)
						{
							if (y == yPos)
							{
								image[x, y] = graphColor.Value;
							}
							else
							{
								image[x, y] = backgroundColor.Value;
							}

							// Optionally: Apply anti-aliasing for smoother edges
							if (smoothEdges && y > 0 && y < height - 1)
							{
								float edgeFactor = (float) (1.0 - Math.Abs(y - yPos) / (height / 2.0));
								if (edgeFactor > 0.1f)
								{
									image[x, y] = new Rgba32(
										(byte) (graphColor.Value.ToPixel<Rgba32>().R * edgeFactor + backgroundColor.Value.ToPixel<Rgba32>().R * (1 - edgeFactor)),
										(byte) (graphColor.Value.ToPixel<Rgba32>().G * edgeFactor + backgroundColor.Value.ToPixel<Rgba32>().G * (1 - edgeFactor)),
										(byte) (graphColor.Value.ToPixel<Rgba32>().B * edgeFactor + backgroundColor.Value.ToPixel<Rgba32>().B * (1 - edgeFactor)),
										(byte) (graphColor.Value.ToPixel<Rgba32>().A * edgeFactor + backgroundColor.Value.ToPixel<Rgba32>().A * (1 - edgeFactor))
									);
								}
							}
						}
					}
				}
			});

			// Wait for all tasks to complete
			await Task.Yield();

			// Return the generated waveform image
			return image;
		}

		public async Task<Image<Rgba32>> GetWaveformImageSimpleAsync(float[]? data, int width = 720, int height = 480,
			int samplesPerPixel = 128, float amplifier = 1.0f, long offset = 0,
			Color? graphColor = null, Color? backgroundColor = null, bool smoothEdges = true)
		{
			// Normalize image dimensions
			width = Math.Max(100, width);
			height = Math.Max(100, height);

			// Normalize colors & get rgba values
			graphColor ??= Color.BlueViolet;
			backgroundColor ??= Color.White;

			// New result image + color fill
			Image<Rgba32> image = new(width, height);
			await Task.Run(() =>
			{
				image.Mutate(ctx => ctx.BackgroundColor(backgroundColor.Value));
			});

			// Verify data
			data ??= this.Data;
			if (data == null || data.LongLength <= 0)
			{
				return image;
			}

			// Adjust offset if necessary
			if (data.Length <= offset)
			{
				offset = 0;
			}

			// Calculate the number of samples to process -> take array from data
			long totalSamples = Math.Min(data.Length - offset, width * samplesPerPixel);
			if (totalSamples <= 0)
			{
				return image;
			}

			float[] samples = new float[totalSamples];
			Array.Copy(data, offset, samples, 0, totalSamples);

			// Draw the waveform on the image async anfd in parallel
			await Task.Run(() =>
			{
				Parallel.For(0, width, x =>
				{
					int sampleIndex = (int) ((float) x / width * totalSamples);
					if (sampleIndex < samples.Length)
					{
						float sampleValue = samples[sampleIndex] * amplifier;
						int yPos = (int) ((sampleValue + 1.0f) / 2.0f * height);
						yPos = Math.Clamp(yPos, 0, height - 1);

						// Draw vertical line for this sample
						for (int y = 0; y < height; y++)
						{
							if (y == yPos)
							{
								image[x, y] = graphColor.Value;
							}
							else
							{
								image[x, y] = backgroundColor.Value;
							}

							// Optionally: Apply anti-aliasing for smoother edges
							if (smoothEdges && y > 0 && y < height - 1)
							{
								float edgeFactor = (float) (1.0 - Math.Abs(y - yPos) / (height / 2.0));
								if (edgeFactor > 0.1f)
								{
									image[x, y] = new Rgba32(
										(byte) (graphColor.Value.ToPixel<Rgba32>().R * edgeFactor + backgroundColor.Value.ToPixel<Rgba32>().R * (1 - edgeFactor)),
										(byte) (graphColor.Value.ToPixel<Rgba32>().G * edgeFactor + backgroundColor.Value.ToPixel<Rgba32>().G * (1 - edgeFactor)),
										(byte) (graphColor.Value.ToPixel<Rgba32>().B * edgeFactor + backgroundColor.Value.ToPixel<Rgba32>().B * (1 - edgeFactor)),
										(byte) (graphColor.Value.ToPixel<Rgba32>().A * edgeFactor + backgroundColor.Value.ToPixel<Rgba32>().A * (1 - edgeFactor))
									);
								}
							}
						}
					}
				});
			});

			// Wait for all tasks to complete
			await Task.Yield();

			// Return the generated waveform image
			return image;
		}

		public async Task<string?> Export(string outPath = "")
		{
			string baseFileName = $"{this.Name} [{this.Bpm:F1}]";

			// Validate and prepare output directory
			outPath = (await this.PrepareOutputPath(outPath, baseFileName)) ?? Path.GetTempPath();
			if (string.IsNullOrEmpty(outPath))
			{
				return null;
			}

			try
			{
				// Process audio data in parallel
				byte[] bytes = (await Task.Run(() => this.GetBytes())
									  .ConfigureAwait(false)).AsParallel().ToArray();

				var waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(
					this.Samplerate,
					this.Channels);

				await using (var memoryStream = new MemoryStream(bytes))
				await using (var audioStream = new RawSourceWaveStream(memoryStream, waveFormat))
				await using (var fileStream = new FileStream(
					outPath,
					FileMode.Create,
					FileAccess.Write,
					FileShare.None,
					bufferSize: 4096,
					useAsync: true))
				{
					await Task.Run(() =>
					{
						WaveFileWriter.WriteWavFileToStream(fileStream, audioStream);
					});
				}

				// Add BPM metadata if needed
				if (this.Bpm > 0.0f)
				{
					await this.AddBpmTag(outPath, this.Bpm)
						.ConfigureAwait(false);
				}

				return outPath;
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"Audio export failed: {ex.Message}");
				return null;
			}
		}

		private async Task<string?> PrepareOutputPath(string outPath, string baseFileName)
		{
			// Check directory existence asynchronously
			if (!string.IsNullOrEmpty(outPath))
			{
				var dirExists = await Task.Run(() => Directory.Exists(outPath))
										.ConfigureAwait(false);
				if (!dirExists)
				{
					outPath = Path.GetDirectoryName(outPath) ?? string.Empty;
				}
			}

			// Fallback to temp directory if needed
			if (string.IsNullOrEmpty(outPath) ||
				!await Task.Run(() => Directory.Exists(outPath))
						  .ConfigureAwait(false))
			{
				outPath = Path.Combine(
					Path.GetTempPath(),
					$"{this.Name}_[{this.Bpm:F2}].wav");
			}

			// Build final file path
			if (Path.HasExtension(outPath))
			{
				return outPath;
			}

			return Path.Combine(outPath, $"{baseFileName}.wav");
		}

		private async Task AddBpmTag(string filePath, float bpm)
		{
			try
			{
				await Task.Run(() =>
				{
					using var file = TagLib.File.Create(filePath);
					file.Tag.BeatsPerMinute = (uint) (bpm * 100);
					file.Save();
				}).ConfigureAwait(false);
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"BPM tag writing failed: {ex.Message}");
			}
		}

		public void Dispose()
		{
			this.Player?.Dispose();
			this.Data = [];
			this.Pointer = 0;
			GC.SuppressFinalize(this);
		}

		public string GetMetaString()
		{
			return $"{this.Samplerate} Hz, {this.Bitdepth} bits, {this.Channels} ch., {(this.Length / (this.Bitdepth / 8) / 1024):N0} KSamples, BPM: {this.Bpm}, Form: '{this.Form}' at <{this.Pointer.ToString("X16")}>";
		}
	}
}
