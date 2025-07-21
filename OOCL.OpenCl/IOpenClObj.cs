using OpenTK.Compute.OpenCL;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OOCL.OpenCl
{
	public interface IOpenClObj
	{
		int Index { get; }
		string Name { get; }
		string Type { get; }
		bool Online { get; }
		string Status { get; }
		IEnumerable<string> ErrorMessages { get; }

	}
}
