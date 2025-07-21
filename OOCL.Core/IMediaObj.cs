using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

// This interface-class is used for AudioObj & ImageObj

namespace OOCL.Core
{
	public interface IMediaObj : IDisposable
	{
		Guid Id { get; }
		string Name { get; }
		string Filepath { get; }
		string Meta { get; }
		bool OnHost { get; }
		float SizeMb { get; }
		string PointerHex { get; }
		string DataType { get; }
		string DataStructure { get; }




	}
}
