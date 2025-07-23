using OOCL.OpenCl;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace OOCL.Shared.Models
{
	public class OpenClModelBase : IOpenClObj, IDisposable
	{
		public int Index { get; set; }= -1;
		public string Name { get; set; }= string.Empty;
		public string Type { get; set; }= "OpenCL Object";
		public bool Online { get; set; }= false;
		public string Status { get; set; }= "FICK DEINE ABGEFACKTE DRECKS-MUTTER !!!";
		public string Meta { get; set; }= string.Empty;
		public IEnumerable<string> ErrorMessages { get; set; }= [];
		protected DateTime createdAt = DateTime.Now;
		public string Timestamp => this.createdAt.ToString("yyyy-MM-dd HH:mm:ss.fff");
		public string TickAge => (DateTime.Now.Ticks - this.createdAt.Ticks).ToString();

		public OpenClModelBase()
		{
			// Default constructor for serialization
		}

		[JsonConstructor]
		public OpenClModelBase(IOpenClObj? obj = null)
		{
			this.Index = obj?.Index ?? -1;
			this.Name = obj?.Name ?? string.Empty;
			this.Online = obj?.Online ?? false;
			this.Status = obj?.Status ?? string.Empty;
			this.Meta = obj?.GetType().Name ?? string.Empty; // Just idea
			this.ErrorMessages = obj?.ErrorMessages ?? [];
		}

		public virtual void Dispose()
		{
			GC.SuppressFinalize(this);
		}

	}
}
