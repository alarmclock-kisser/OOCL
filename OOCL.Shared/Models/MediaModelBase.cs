using OOCL.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace OOCL.Shared
{
	public class MediaModelBase : IMediaObj, IDisposable
	{
		public Guid Id { get; private set; } = Guid.Empty;

		public string Name { get; private set; } = string.Empty;

		public string Filepath { get; private set; } = string.Empty;

		public string Meta { get; private set; } = string.Empty;

		public bool OnHost { get; private set; } = true;

		public float SizeMb { get; private set; } = 0.0f;

		public string PointerHex { get; private set; } = string.Empty;

		public string DataType { get; private set; } = "void";

		public string DataStructure { get; private set; } = "[]";

		public MediaModelBase()
		{
			// Default constructor for serialization
		}

		public MediaModelBase(IMediaObj? obj = null)
		{
			if (obj == null)
			{
				return;
			}

			this.Id = obj.Id;
			this.Name = obj.Name;
			this.Filepath = obj.Filepath;
			this.Meta = obj.Meta;
			this.OnHost = obj.OnHost;
			this.SizeMb = obj.SizeMb;
			this.PointerHex = obj.PointerHex;
			this.DataType = obj.DataType;
			this.DataStructure = obj.DataStructure;

		}

		public virtual void Dispose()
		{
			GC.SuppressFinalize(this);
		}





	}
}
