﻿using OOCL.Core;
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
		public Guid Id { get; set; } = Guid.Empty;

		public string Name { get; set; } = "No name available.";

		public string Filepath { get; set; } = "No file path available.";

		public string Extension { get; set; } = "No file format available.";

		public string Meta { get; set; } = string.Empty;

		public bool OnHost { get; set; } = true;

		public float SizeMb { get; set; } = 0.0f;

		public string PointerHex { get; set; } = "null";

		public string DataType { get; set; } = "void";

		public string DataStructure { get; set; } = "[]";

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
			this.Extension = Path.GetExtension(obj.Filepath).ToUpperInvariant();
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
