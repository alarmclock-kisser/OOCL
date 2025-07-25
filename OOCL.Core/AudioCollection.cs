using NAudio.Wave;
using System.Diagnostics;

namespace OOCL.Core
{
	public class AudioCollection : IDisposable
	{
		// Event Definitions for UI communication
		public event EventHandler<string>? LogMessage;
		public event EventHandler<IReadOnlyList<AudioObj>>? TracksChanged;
		public event EventHandler<AudioObj?>? CurrentTrackChanged;
		public event EventHandler<PlaybackState>? PlaybackStateChanged;
		public event EventHandler<double>? PlaybackPositionChanged;

		// Audio Data
		private readonly List<AudioObj> _tracks = [];
		private AudioObj? _currentTrack;
		private CancellationTokenSource _playbackCancellationTokenSource = new();

		// Properties
		public string Repopath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..");
		public IReadOnlyList<AudioObj> Tracks => this._tracks.AsReadOnly();

		public AudioObj? CurrentTrack
		{
			get => this._currentTrack;
			private set
			{
				if (this._currentTrack != value)
				{
					this._currentTrack = value;
                    this.CurrentTrackChanged?.Invoke(this, this._currentTrack);
				}
			}
		}

		public AudioObj? this[Guid guid] => this._tracks.FirstOrDefault(t => t.Id == guid);


		// Options
		public int DefaultPlaybackVolume { get; set; } = 100;
		public int AnimationDelay { get; set; } = 1000 / 30;
		public bool SaveMemory { get; set; } = false;
		public string ImportPath { get; set; } = string.Empty;
		public string ExportPath { get; set; } = string.Empty;

		// Ctor with options
		public AudioCollection(bool saveMemory = false, int defaultPlaybackVolume = 66, int fps = 30)
		{
			this.SaveMemory = saveMemory;
			this.DefaultPlaybackVolume = defaultPlaybackVolume;
			this.AnimationDelay = 1000 / fps;
			if (this.SaveMemory)
			{
				this.LogMessage?.Invoke(this, "Memory saving enabled. All tracks will be disposed on new track addition.");
			}
		}


		// --- Public Methods ---
		public async Task<AudioObj> AddTrack(string filePath)
		{
			if (this.SaveMemory)
			{
				// Dispose every track to free memory
				lock (this._tracks)
				{
					foreach (var t in this._tracks)
					{
						t.Dispose();
					}
					
					this._tracks.Clear();
				}
				
				GC.Collect();
				GC.WaitForPendingFinalizers();
			}

			if (!File.Exists(filePath))
			{
				throw new FileNotFoundException("Audio file not found", filePath);
			}

			AudioObj track = new AudioObj(filePath)
			{
				Volume = this.DefaultPlaybackVolume,
				WaveformUpdateFrequency = this.AnimationDelay,
			};
			await track.LoadAudioFile().ConfigureAwait(false);

			lock (this._tracks)
			{
				this._tracks.Add(track);
				this.CurrentTrack = track;
			}

            this.TracksChanged?.Invoke(this, this.Tracks);
            this.LogMessage?.Invoke(this, $"Added track: {track.Name}");

			return track;
		}

		public AudioObj CreateEmptyTrack(long length, int samplerate = 44100, int channels = 1, int bitdepth = 16)
		{
			float[] data = new float[length];
			Array.Fill(data, 0.0f);

			var obj = new AudioObj(data, samplerate, channels, bitdepth);

			lock (this._tracks)
			{
				this._tracks.Add(obj);
				this.CurrentTrack = obj;
			}

            this.TracksChanged?.Invoke(this, this.Tracks);
            this.LogMessage?.Invoke(this, $"Created empty track: {obj.Name}");
			return obj;
		}

		public async Task<AudioObj> CreateWaveform(string wave = "sin", int lengthSec = 1,
			int samplerate = 44100, int channels = 1, int bitdepth = 16)
		{
			if (lengthSec <= 0 || samplerate <= 0 || channels <= 0 || bitdepth <= 0)
			{
				throw new ArgumentException("Invalid parameters for waveform creation.");
			}

			long length = (long) lengthSec * samplerate * channels;
			float[] data = new float[length];

			await Task.Run(() => this.GenerateWaveform(data, wave, samplerate, channels)).ConfigureAwait(false);

			var obj = new AudioObj(data, samplerate, channels, bitdepth)
			{
				Filepath = $"Generated_{wave}_{lengthSec}s_{samplerate}Hz_{channels}ch_{bitdepth}bit.wav"
			};

			lock (this._tracks)
			{
				this._tracks.Add(obj);
				this.CurrentTrack = obj;
			}

            this.TracksChanged?.Invoke(this, this.Tracks);
            this.LogMessage?.Invoke(this, $"Created waveform: {obj.Filepath}");
			Debug.WriteLine($"Sample range: {data.Min()} to {data.Max()}");

			return obj;
		}

		private void GenerateWaveform(float[] data, string wave, int samplerate, int channels)
		{
			double frequency = 440.0;
			double increment = (2 * Math.PI * frequency) / samplerate;
			Random rand = new();
			float amplitude = 0.8f;

			Parallel.For(0, data.Length / channels, i =>
			{
				float sample = wave.ToLower() switch
				{
					"sin" => amplitude * (float) Math.Sin(i * increment),
					"square" => amplitude * ((i % (samplerate / frequency) < (samplerate / frequency) / 2) ? 1.0f : -1.0f),
					"saw" => amplitude * (float) ((i % (samplerate / frequency)) / (samplerate / frequency) * 2 - 1),
					"noise" => amplitude * (float) (rand.NextDouble() * 2 - 1),
					_ => throw new ArgumentException("Unsupported waveform type.")
				};

				for (int c = 0; c < channels; c++)
				{
					data[i * channels + c] = sample;
				}
			});
		}

		public async Task LoadResourcesAudios()
		{
			string[] audioFiles = await Task.Run(() =>
				Directory.GetFiles(Path.Combine(this.Repopath, "Resources"), "*.*", SearchOption.AllDirectories)
					.Where(f => f.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) ||
							   f.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
					.ToArray()
			).ConfigureAwait(false);

			if (audioFiles.Length == 0)
			{
                this.LogMessage?.Invoke(this, "No audio files found in Resources directory.");
				return;
			}

			var loadTasks = audioFiles.Select(file =>
				this.AddTrack(file).ContinueWith(t =>
				{
					if (t.IsFaulted)
					{
                        this.LogMessage?.Invoke(this, $"Error loading audio file '{file}': {t.Exception?.InnerException?.Message}");
					}
				})
			);

			await Task.WhenAll(loadTasks).ConfigureAwait(false);
		}

		public void RemoveTrackAt(int index)
		{
			AudioObj? toRemove = null;

			lock (this._tracks)
			{
				if (index < 0 || index >= this._tracks.Count)
				{
					return;
				}

				toRemove = this._tracks[index];
				this._tracks.RemoveAt(index);

				if (this._tracks.Count == 0)
				{
					this.CurrentTrack = null;
				}
				else if (this.CurrentTrack == null || index == this._tracks.Count)
				{
					this.CurrentTrack = this._tracks[Math.Min(index, this._tracks.Count - 1)];
				}
			}

			toRemove?.Dispose();
            this.TracksChanged?.Invoke(this, this.Tracks);
            this.LogMessage?.Invoke(this, $"Removed track at index {index}");
		}

		public async Task TogglePlayback(float volume = 1.0f)
		{
			if (this.CurrentTrack == null || this.CurrentTrack.Data.Length == 0)
			{
				return;
			}

			if (this.CurrentTrack.Playing)
			{
				await this.StopPlayback().ConfigureAwait(false);
			}
			else
			{
				await this.StartPlayback(volume).ConfigureAwait(false);
			}
		}

		private async Task StartPlayback(float volume)
		{
			this._playbackCancellationTokenSource = new CancellationTokenSource();

			await this.CurrentTrack!.Play(this._playbackCancellationTokenSource.Token, () =>
			{
                this.PlaybackStateChanged?.Invoke(this, PlaybackState.Stopped);
                this.PlaybackPositionChanged?.Invoke(this, 0);
			}, volume).ConfigureAwait(false);

            this.PlaybackStateChanged?.Invoke(this, PlaybackState.Playing);

			_ = Task.Run(async () =>
			{
				while (this.CurrentTrack.Playing && !this._playbackCancellationTokenSource.IsCancellationRequested)
				{
                    this.PlaybackPositionChanged?.Invoke(this, this.CurrentTrack.CurrentTime);
					await Task.Delay(30, this._playbackCancellationTokenSource.Token).ConfigureAwait(false);
				}
			}, this._playbackCancellationTokenSource.Token);
		}

		private async Task StopPlayback()
		{
			this._playbackCancellationTokenSource.Cancel();
			this.CurrentTrack?.Stop();
            this.PlaybackStateChanged?.Invoke(this, PlaybackState.Stopped);
			await Task.Yield();
		}

		public async Task<int> StopAll()
		{
			int stopped = 0;

			try
			{
				await Task.Run(() =>
				{
					lock (this._tracks)
					{
						foreach (var track in this._tracks)
						{
							if (track.Playing)
							{
								track.Stop();
								stopped++;
							}
						}
					}
				}).ConfigureAwait(false);
			}
			catch (Exception ex)
			{
                this.LogMessage?.Invoke(this, $"Error stopping all tracks: {ex.Message}");
				stopped = -1;
			}

			return stopped;
		}

		public void Dispose()
		{
			this._playbackCancellationTokenSource.Cancel();
			lock (this._tracks)
			{
				foreach (var track in this._tracks)
				{
					track.Dispose();
				}
				this._tracks.Clear();
			}
			GC.SuppressFinalize(this);
		}
	}
}