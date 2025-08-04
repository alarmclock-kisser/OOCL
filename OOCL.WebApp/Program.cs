using OOCL.WebApp.Components;
using Radzen;

namespace OOCL.WebApp
{
	public class Program
	{
		public static void Main(string[] args)
		{
			var builder = WebApplication.CreateBuilder(args);

			string? apiBaseUrl = builder.Configuration.GetValue<string>("ApiUrlHttps") ?? builder.Configuration.GetValue<string>("ApiUrlHttp") ?? "https://localhost:7171";
			if (string.IsNullOrEmpty(apiBaseUrl))
			{
				throw new InvalidOperationException("'" + apiBaseUrl + "' is not configured. Please set the ApiBaseUrl configuration in appsettings.json or environment variables.");
			}

			// Add services to the container.
			builder.Services.AddRazorPages();
			builder.Services.AddServerSideBlazor();
			builder.Services.AddMvc();
			builder.Services.AddRadzenComponents();

			// Add ApiUrlConfig
			builder.Services.AddSingleton(new ApiUrlConfig(apiBaseUrl));

			// Add ApiClient (ctor string is the base URL)
			builder.Services.AddHttpClient<OOCL.Client.ApiClient>(client =>
			{
				client.BaseAddress = new Uri("https://api.oocl.work:7171");
			});

			// BUILD
			var app = builder.Build();

			// Configure the HTTP request pipeline.
			if (!app.Environment.IsDevelopment())
			{
				app.UseExceptionHandler("/Error");
				app.UseHsts();
			}

			// Use-configs
			app.UseHttpsRedirection();
			app.UseStaticFiles();
			app.UseAntiforgery();
			app.UseRouting();

			// Configure endpoints
			app.MapBlazorHub();
			app.MapFallbackToPage("/_Host");

			app.MapRazorPages();

			app.Run();
		}
	}

	public class ApiUrlConfig
	{
		public string Url { get; set; } = "https://localhost:7171";

		public ApiUrlConfig(string baseUrl = "")
		{
			this.Url = !string.IsNullOrEmpty(baseUrl) ? baseUrl : this.Url;
		}
	}
}
