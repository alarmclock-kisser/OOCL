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
		public string TotalMemory { get; set; } = "0";

		public string UsedMemory { get; set; } = "0";
		public string FreeMemory { get; set; } = "0";
		private float usagePercentage = 0.0f;
		public string UsagePercentage { get; set; } = "0.0";

		public string SizeUnit { get; set; } = "Bytes";

		public IEnumerable<PieChartData> PieChart { get; set; } = [];


		public OpenClUsageInfo() : base()
		{

		}

		[JsonConstructor]
		public OpenClUsageInfo(IOpenClObj? obj, string magnitude = "KB") : base(obj)
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

			int magnitudeFactor = magnitude.ToUpper() switch
			{
				"KB" => 1024,
				"MB" => 1024 * 1024,
				"GB" => 1024 * 1024 * 1024,
				_ => 1 // Default to Bytes
			};

			this.SizeUnit = magnitude;
			
			double totalMemory = (long.TryParse(register.TotalMemoryBytes, out long parsedTotal) ? parsedTotal : 0) / magnitudeFactor;
			this.TotalMemory = totalMemory.ToString("N2");
			double usedMemory = (long.TryParse(register.UsedMemoryBytes, out long parsedUsed) ? parsedUsed : 0) / magnitudeFactor;
			this.UsedMemory = usedMemory.ToString("N2");
			double freeMemory = (long.TryParse(register.FreeMemoryBytes, out long parsedFree) ? parsedFree : 0) / magnitudeFactor;
			this.FreeMemory = freeMemory.ToString("N2");

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