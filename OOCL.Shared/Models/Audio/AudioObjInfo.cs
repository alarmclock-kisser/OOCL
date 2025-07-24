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
        public float Bpm { get; set; } = 0.0f;
		public int ChunkSize { get; set; } = 0;
        public int OverlapSize { get; set; } = 0;
        public string Form { get; set; } = "empty";
        public double StretchFactor { get; set; } = 1.0;
        public bool Playing { get; set; } = false;
        public string PlaybackState => this.Playing ? "Playing" : "Stopped";
        public int Volume { get; set; } = 100;
        public double Duration { get; set; } = 0.0;
        public double CurrentTime { get; set; } = 0.0;

		public double LastLoadingTime { get; set; } = 0.0;
		public double LastProcessingTime { get; set; } = 0.0;
        public string ErrorMessage { get; set; } = string.Empty;


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
            this.Bpm = audioObj.Bpm;
			this.ChunkSize = audioObj.ChunkSize;
            this.OverlapSize = audioObj.OverlapSize;
            this.Form = audioObj.Form;
            this.StretchFactor = audioObj.StretchFactor;
            this.Playing = audioObj.Playing;
            this.Volume = audioObj.Volume;
            this.Duration = audioObj.Duration;
            this.CurrentTime = audioObj.CurrentTime;

		}

		public override string ToString()
		{
            return $"'{this.Name}' <{(this.Extension)}>" + "\n" +
                $"({this.Id})" + "\n\n" +
                $"{this.Samplerate} Hz, {this.Channels} ch., {this.Bitdepth} bits, {this.Lenght} samples" + "\n" +
                $"{this.Bpm.ToString("F6")} BPM" + "\n" +
				$"{this.Form}-Form, chunk size: {this.ChunkSize} ({this.OverlapSize} overlap), stretch factor: {this.StretchFactor.ToString("F6")}" + "\n\n" +
                $"{this.PlaybackState}: {this.CurrentTime.ToString("F2")} sec. / {this.Duration.ToString("F2")} sec. (Vol.: {this.Volume}%)" + "\n\n" +
                $"{this.SizeMb} MB as {this.DataType}{this.DataStructure} on {(this.OnHost ? "Host" : "OpenCL")}" + "\n" +
                $"<{this.PointerHex}>" + "\n\n" +
                $"Initially loaded within {this.LastLoadingTime.ToString("F3")} sec." + "\n" +
                $"Last processing within {this.LastProcessingTime.ToString("F3")} sec." + "\n" +
                $"{(string.IsNullOrEmpty(this.ErrorMessage) ? "No errors." : $"Error: {this.ErrorMessage}")}";
		}


	}
}
