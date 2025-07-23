using OOCL.Core;
using System.Text.Json.Serialization;

namespace OOCL.Shared
{
    public class ImageObjInfo : MediaModelBase
	{
		public int Width { get; set; } = 0;
		public int Height { get; set; } = 0;
		public int Channels { get; set; } = 0;
		public int Bitdepth { get; set; } = 0;

		public double LastLoadingTime { get; set; } = 0.0;
		public double LastProcessingTime { get; set; } = 0.0;
		public string ErrorMessage { get; set; } = string.Empty;


		public ImageObjInfo() : base()
		{
			// Default constructor for serialization
		}

		[JsonConstructor]
		public ImageObjInfo(IMediaObj? obj) : base(obj)
		{
            if (obj == null)
            {
                return;
            }

			var imageObj = obj as ImageObj;
			if (imageObj == null)
			{
				return;
			}

			this.Width = imageObj.Width;
			this.Height = imageObj.Height;
			this.Channels = imageObj.Channels;
			this.Bitdepth = imageObj.Bitdepth;

		}

		public override string ToString()
		{
			return $"ImageObjInfo: {this.Name} ({this.Width}x{this.Height}, {this.Channels} channels, {this.Bitdepth} bit)";
		}
	}
}
