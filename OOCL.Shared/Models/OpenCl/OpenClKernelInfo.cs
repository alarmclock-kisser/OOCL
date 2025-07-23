using OOCL.OpenCl;
using OOCL.Shared.Models;
using System.Text.Json.Serialization;

namespace OOCL.Shared
{
	public class OpenClKernelInfo : OpenClModelBase
	{
		public string Filepath = string.Empty;
		public int ArgumentsCount { get; set; } = 0;
		public IEnumerable<string> ArgumentNames { get; set; } = [];
		public IEnumerable<string> ArgumentType { get; set; } = [];
		public string InputPointerType { get; set; } = "void*";
		public string OutputPointerType { get; set; } = string.Empty;

        public string MediaType { get; set; } = "DIV";
		public string FunctionName { get; set; } = string.Empty;



		public OpenClKernelInfo() : base()
		{
			// Empty default ctor
		}

		[JsonConstructor]
		public OpenClKernelInfo(IOpenClObj? obj, int index) : base(obj)
		{
			if (obj == null)
            {
				return;
            }

			var compiler = obj as OpenClKernelCompiler;
			if (compiler == null)
			{
				Console.WriteLine("Compiler object is not of type OpenClKernelCompiler.");
				return;
			}

			if (index < 0 || index >= compiler.KernelFiles.Count())
			{
				Console.WriteLine($"Kernel-index is out of range (max: {(compiler.KernelFiles.Count() - 1)}, was {index})");
				return;
			}

			this.Filepath = compiler.KernelFiles.ElementAt(index);
			this.ArgumentsCount = compiler.ArgumentNames.Count();
			this.ArgumentNames = compiler.ArgumentNames;
			this.ArgumentType = compiler.ArgumentTypes;
			this.InputPointerType = compiler.PointerInputType;
			this.OutputPointerType = compiler.PointerOutputType;

			this.MediaType = this.Filepath.Contains("Image") ? "IMG" :
				this.Filepath.Contains("Audio") ? "AUD" :
				this.Filepath.Contains("Video") ? "VID" : "DIV";

			this.FunctionName = compiler.GetKernelName(this.Filepath) ?? string.Empty;
		}
	}
}
