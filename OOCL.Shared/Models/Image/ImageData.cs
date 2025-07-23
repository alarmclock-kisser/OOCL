using OOCL.Core;
using System.Text.Json.Serialization;

namespace OOCL.Shared
{

	// This data class carries the image data in a Base64 format

	public class ImageData : MediaModelBase
	{
		public int Width { get; set; } = 0;
		public int Height { get; set; } = 0;
		public string Base64Format { get; set; } = "png";
		public string Base64Image { get; set; } = string.Empty;


		public ImageData()
		{
			// Default constructor for serialization
		}

		[JsonConstructor]
		public ImageData(IMediaObj? obj = null) : base(obj)
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
			this.Base64Image = imageObj.AsBase64Image("png").Result;

		}
	}
}
