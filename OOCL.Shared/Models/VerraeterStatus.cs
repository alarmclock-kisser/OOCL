using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OOCL.Shared.Models
{
	public class VerraeterStatus
	{

		public string Info { get; set; } = string.Empty;

		[System.Text.Json.Serialization.JsonConstructor]
		public VerraeterStatus(string info)
		{
			this.Info = info;
		}

		public VerraeterStatus()
		{
			// Default constructor for serialization
		}

	}
}
