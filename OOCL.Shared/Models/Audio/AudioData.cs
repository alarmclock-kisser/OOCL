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
		public int Width { get; set; } = 0;
		public int Height { get; set; } = 0;
		public string Base64Format { get; set; } = "png";
		public string Base64Image { get; set; } = string.Empty;

		public AudioData() : base()
		{
			// Default constructor for serialization
		}

		[JsonConstructor]
		public AudioData(IMediaObj? obj = null) : base(obj)
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

			this.Width = audioObj.WaveformWidth;
			this.Height = audioObj.WaveformHeight;
			this.Base64Image = audioObj.AsBase64Image(this.Base64Format).Result;
		}
	}
}
