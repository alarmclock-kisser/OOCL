using OOCL.OpenCl;
using OOCL.Shared.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace OOCL.Shared
{
	public class OpenClUsageInfo : OpenClModelBase
	{
		public string TotalMemory { get; private set; } = "0";

		public string UsedMemory { get; private set; } = "0";
		public string FreeMemory { get; private set; } = "0";
		private float usagePercentage = 0.0f;
		public string UsagePercentage { get; private set; } = "0.0";

		public string SizeUnit { get; set; } = "Bytes";

		public IEnumerable<PieChartData> PieChart { get; private set; } = [];

		public string Magnitude { get; set; } = "MBytes";


		public OpenClUsageInfo() : base()
		{

		}

		[JsonConstructor]
		public OpenClUsageInfo(IOpenClObj? obj, bool readable = false) : base(obj)
		{
			if (obj == null)
			{
				this.UpdatePieChartData();
				return;
			}

			var register = obj as OpenClMemoryRegister;
			if (register == null)
			{
				this.UpdatePieChartData();
				return;
			}

			this.SizeUnit = readable ? "MBytes" : "Bytes";
			this.TotalMemory = register.TotalMemoryBytes;
			this.UsedMemory = register.UsedMemoryBytes;
			this.FreeMemory = register.FreeMemoryBytes;
			this.UsagePercentage = register.UsagePercentageString;
			this.usagePercentage = register.UsagePercentage;

			this.GetPieChart();

		}

		private void UpdatePieChartData() =>
			this.PieChart =
			[
				new PieChartData("Used Memory", (float)this.usagePercentage),
				new PieChartData("Free Memory", (float)(100 - this.usagePercentage))
			];

		private void GetPieChart()
		{
			this.PieChart = [new PieChartData() { Label = "Used", Value = this.usagePercentage}, new PieChartData() { Label = "Free", Value = 100f - this.usagePercentage}];
		}

	}



	public class PieChartData
	{
		public string Label { get; set; } = string.Empty;
		public float Value { get; set; } = 0.0f;


		public PieChartData()
		{

		}

		public PieChartData(string label, float value)
		{
			this.Label = label;
			this.Value = value;
		}
	}
}