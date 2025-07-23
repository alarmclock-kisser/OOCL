using OOCL.Core;
using OOCL.Shared;
using System.Text.Json.Serialization;

namespace OOCL.Shared
{
    public class AudioObjInfo : MediaModelBase
	{
        public int Samplerate { get; set; } = 0;
        public int Channels { get; set; } = 0;
        public int Bitdepth { get; set; } = 0;
        public int Lenght { get; set; } = 0;
        public int ChunkSize { get; set; } = 0;
        public int OverlapSize { get; set; } = 0;
        public string Form { get; set; } = string.Empty;
        public double StretchFactor { get; set; } = 1.0;
        public bool Playing { get; set; } = false;
        public string PlaybackState => this.Playing ? "Playing" : "Stopped";
        public int Volume { get; set; } = 100;
        public double Duration { get; set; } = 0.0;
        public double CurrentTime { get; set; } = 0.0;

		public double LastLoadingTime { get; set; } = 0.0;
		public double LastProcessingTime { get; set; } = 0.0;
        public string ErrorMessage { get; set; } = string.Empty;

        public float Bpm { get; set; } = 0.0f;

		public AudioObjInfo()
        {
			// Default constructor for serialization
		}

		[JsonConstructor]
		public AudioObjInfo(IMediaObj? obj) : base(obj)
		{
            if (obj == null)
            {
                return;
            }

            var audioObj = obj as AudioObj;
            if (audioObj == null)
            {
                return;
			}

			this.Samplerate = audioObj.Samplerate;
            this.Channels = audioObj.Channels;
            this.Bitdepth = audioObj.Bitdepth;
            this.Lenght = audioObj.Length;
            this.ChunkSize = audioObj.ChunkSize;
            this.OverlapSize = audioObj.OverlapSize;
            this.Form = audioObj.Form;
            this.StretchFactor = audioObj.StretchFactor;
            this.Playing = audioObj.Playing;
            this.Volume = audioObj.Volume;
            this.Duration = audioObj.Duration;
            this.CurrentTime = audioObj.CurrentTime;

		}

		public override String ToString()
        {
            string info = $"AudioObjInfo: {this.Name} ({this.Id})\n" +
                          $"Samplerate: {this.Samplerate}, Channels: {this.Channels}, Bitdepth: {this.Bitdepth}\n" +
                          $"Length: {this.Lenght}, ChunkSize: {this.ChunkSize}, OverlapSize: {this.OverlapSize}\n" +
                          $"Form: {this.Form}, StretchFactor: {this.StretchFactor}\n" +
                          $"Playing: {this.Playing}, Volume: {this.Volume}\n" +
                          $"Duration: {this.Duration}, CurrentTime: {this.CurrentTime}\n" +
                          $"ErrorMessage: {this.ErrorMessage}";

            return info;
		}

        
    }
}
