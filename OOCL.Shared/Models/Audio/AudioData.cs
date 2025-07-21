using OOCL.Core;
using OOCL.Shared;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace OOCL.Shared
{

	// This data class carries the audio waveform image data  in a Base64 format

	public class AudioData : MediaModelBase
	{
		public int Width { get; private set; } = 0;
		public int Height { get; private set; } = 0;
		public string Base64Format { get; set; } = "png";
		public string Base64Image { get; private set; } = string.Empty;

		public AudioData() : base()
		{
			// Default constructor for serialization
		}

		[JsonConstructor]
		public AudioData(AudioObj? obj = null) : base(obj)
		{
			if (obj == null)
			{
				return;
			}

			this.Width = obj.WaveformWidth;
			this.Height = obj.WaveformHeight;
			this.Base64Image = obj.AsBase64Image(this.Base64Format).Result;
		}
	}
}
