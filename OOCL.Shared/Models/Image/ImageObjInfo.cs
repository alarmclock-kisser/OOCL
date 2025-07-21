using OOCL.Core;
using System.Text.Json.Serialization;

namespace OOCL.Shared
{
    public class ImageObjInfo : MediaModelBase
	{
		public int Width { get; private set; } = 0;
		public int Height { get; private set; } = 0;
		public int Channels { get; private set; } = 0;
		public int Bitdepth { get; private set; } = 0;


		public ImageObjInfo() : base()
		{
			// Default constructor for serialization
		}

		[JsonConstructor]
		public ImageObjInfo(ImageObj? obj) : base(obj)
		{
            if (obj == null)
            {
                return;
            }

			this.Width = obj.Width;
			this.Height = obj.Height;
			this.Channels = obj.Channels;
			this.Bitdepth = obj.Bitdepth;

		}

		
	}
}
