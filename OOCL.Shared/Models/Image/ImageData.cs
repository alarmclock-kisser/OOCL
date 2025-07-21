using OOCL.Core;
using System.Text.Json.Serialization;

namespace OOCL.Shared
{

	// This data class carries the image data in a Base64 format

	public class ImageData : MediaModelBase
	{
		public int Width { get; private set; } = 0;
		public int Height { get; private set; } = 0;
		public string Base64Format { get; set; } = "png";
		public string Base64Image { get; private set; } = string.Empty;


		public ImageData()
		{
			// Default constructor for serialization
		}

		[JsonConstructor]
		public ImageData(ImageObj? obj = null) : base(obj)
		{
			if (obj == null)
			{
				return;
			}

			this.Width = obj.Width;
			this.Height = obj.Height;
			this.Base64Image = obj.AsBase64Image("png").Result;

		}
	}
}
