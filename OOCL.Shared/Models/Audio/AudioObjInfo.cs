using OOCL.Core;
using OOCL.Shared;
using System.Text.Json.Serialization;

namespace OOCL.Shared
{
    public class AudioObjInfo : MediaModelBase
	{
        public int Samplerate { get; private set; } = 0;
        public int Channels { get; private set; } = 0;
        public int Bitdepth { get; private set; } = 0;
        public int Lenght { get; private set; } = 0;
        public int ChunkSize { get; private set; } = 0;
        public int OverlapSize { get; private set; } = 0;
        public string Form { get; private set; } = string.Empty;
        public double StretchFactor { get; private set; } = 1.0;
        public bool Playing { get; private set; } = false;
        public string PlaybackState => this.Playing ? "Playing" : "Stopped";
        public int Volume { get; private set; } = 100;
        public double Duration { get; private set; } = 0.0;
        public double CurrentTime { get; private set; } = 0.0;


		public AudioObjInfo()
        {
			// Default constructor for serialization
		}

		[JsonConstructor]
		public AudioObjInfo(AudioObj? obj) : base(obj)
		{
            if (obj == null)
            {
                return;
            }

            this.Samplerate = obj.Samplerate;
            this.Channels = obj.Channels;
            this.Bitdepth = obj.Bitdepth;
            this.Lenght = obj.Length;
            this.ChunkSize = obj.ChunkSize;
            this.OverlapSize = obj.OverlapSize;
            this.Form = obj.Form;
            this.StretchFactor = obj.StretchFactor;
            this.Playing = obj.Playing;
            this.Volume = obj.Volume;
            this.Duration = obj.Duration;
            this.CurrentTime = obj.CurrentTime;

		}

        
    }
}
