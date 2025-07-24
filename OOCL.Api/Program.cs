using Microsoft.AspNetCore.Http.Features;
using Microsoft.OpenApi.Models;
using OOCL.Core.CommonStaticMethods;
using TextCopy;
using Microsoft.AspNetCore.Mvc;
using OOCL.Core;
using System.Reflection;


namespace OOCL.Api
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

			// Get appsettings
			bool useSwagger = builder.Configuration.GetValue<bool>("UseSwagger", false);
			int maxUploadSize = builder.Configuration.GetValue<int>("MaxUploadMb", 64) * 1_000_000;
			bool saveMemory = builder.Configuration.GetValue<bool>("SaveMemory", false);
			int spareWorkers = builder.Configuration.GetValue<int>("SpareWorkers", 0);
			int defaultVolume = builder.Configuration.GetValue<int>("DefaultVolume", 50);
			int waveformFps = builder.Configuration.GetValue<int>("WaveformFps", 30);
			int defaultWidth = builder.Configuration.GetValue<int>("DefaultImageWidth", 720);
			int defaultHeight = builder.Configuration.GetValue<int>("DefaultImageHeight", 480);

			// Server config
			var serverConfig = builder.Configuration.GetSection("ServerConfig");
			string serverName = serverConfig.GetValue<string>("ServerName") ?? Assembly.GetExecutingAssembly().GetName().Name ?? "OOCL.Api";
			string serverProtocol = serverConfig.GetValue<string>("ServerProtocol") ?? "http";
			int serverPort = serverConfig.GetValue<int>("ServerPort", 5555);
			string serverUrl = serverConfig.GetValue<string>("ServerUrl") ?? (serverProtocol.ToLower().EndsWith('s') ? "https://localhost:" + serverPort : "http://localhost:" + serverPort);
			string fqdn = serverConfig.GetValue<string>("FQDN") ?? "localhost";
			string fqdnFallback = serverConfig.GetValue<string>("FQDN_fallback") ?? "localhost";
			string serverVersion = serverConfig.GetValue<string>("ServerVersion") ?? "0.0.0";
			string serverDescription = serverConfig.GetValue<string>("ServerDescription") ?? "OOCL API";
			int initializeDeviceId = serverConfig.GetValue<int>("InitializeDeviceId", -1);
			string defaultDeviceName = serverConfig.GetValue<string>("DefaultDeviceName") ?? "CPU";

			// Build ApiConfig & add to DI container
			var apiConfig = new ApiConfig
			{
				ServerName = serverName,
				ServerProtocol = serverProtocol,
				ServerPort = serverPort,
				ServerUrl = serverUrl,
				FQDN = fqdn,
				FQDN_fallback = fqdnFallback,
				ServerVersion = serverVersion,
				ServerDescription = serverDescription,
				InitializeDeviceId = initializeDeviceId,
				DefaultDeviceName = defaultDeviceName
			};
			builder.Services.AddSingleton(apiConfig);


			// Log retrieved settings on console
			Console.WriteLine($" ~ ~ ~ ~ ~ ~ ~ ~ OOCL.Api \\ appsettings.json ~ ~ ~ ~ ~ User options: ~ ~ ~ ~ ~ ");
			Console.WriteLine();
			Console.WriteLine($"~appsettings~: {(useSwagger ? "Not u" : "U")}sing swagger UI with{(useSwagger ? "" : "out")} endpoints. ['UseSwagger'] = '{(useSwagger ? "true" : "false")}'");
			Console.WriteLine($"~appsettings~: Max upload size set to {(maxUploadSize / 1_000_000)} MB. ['MaxUploadMb'] = '{maxUploadSize / 1_000_000}'");
			Console.WriteLine($"~appsettings~: Memory saving {(saveMemory ? "en" : "dis")}abled. ['SaveMemory'] = '{(saveMemory ? "true" : "false")}'");
			if (saveMemory)
			{
				Console.WriteLine("~ ~ ~ ~ ~ ~ ~: (Warning: This will wipe all media objects except for the most recent one!)");
			}
			Console.WriteLine($"~appsettings~: Spare workers set to {spareWorkers}. ['SpareWorkers'] = '{spareWorkers}'" +
				$" (using {(CommonStatics.ActiveWorkers)} of max. {CommonStatics.MaxAvailableWorkers})");
			Console.WriteLine($"~appsettings~: Default playback volume set to {defaultVolume}%. ['DefaultVolume'] = '{defaultVolume}'");
			Console.WriteLine($"~appsettings~: Waveform FPS set to {waveformFps}. ['WaveformFps'] = '{waveformFps}'");
			Console.WriteLine($"~appsettings~: Default image resolution set to {defaultWidth}x{defaultHeight} px. ['DefaultImageWidth'] = '{defaultWidth}', ['DefaultImageHeight'] = '{defaultHeight}'");
			Console.WriteLine($"~appsettings~: Server name set to '{serverName}'. ['ServerName'] = '{serverName}'");
			Console.WriteLine($"~appsettings~: Server protocol set to '{serverProtocol}'. ['ServerProtocol'] = '{serverProtocol}'");
			Console.WriteLine($"~appsettings~: Server port set to {serverPort}. ['ServerPort'] = '{serverPort}'");
			Console.WriteLine($"~appsettings~: Server URL set to '{serverUrl}'. ['ServerUrl'] = '{serverUrl}'");
			Console.WriteLine($"~appsettings~: FQDN set to '{fqdn}'. ['FQDN'] = '{fqdn}'");
			Console.WriteLine($"~appsettings~: FQDN fallback set to '{fqdnFallback}'. ['FQDN_fallback'] = '{fqdnFallback}'");
			Console.WriteLine($"~appsettings~: Server version set to '{serverVersion}'. ['ServerVersion'] = '{serverVersion}'");
			Console.WriteLine($"~appsettings~: Server description set to '{serverDescription}'. ['ServerDescription'] = '{serverDescription}'");
			Console.WriteLine($"~appsettings~: Initialize device ID set to {initializeDeviceId}. ['InitializeDeviceId'] = '{initializeDeviceId}'");
			if (initializeDeviceId < 0)
			{
				Console.WriteLine("~ ~ ~ ~ ~ ~ ~: (Warning: No device id is selected for initializing at startup!)");
			}
			Console.WriteLine($"~appsettings~: Default device name set to '{defaultDeviceName}'. ['DefaultDeviceName'] = '{defaultDeviceName}'");
			Console.WriteLine();
			Console.WriteLine($" ~ ~ ~ ~ ~ ~ ~ ~ User options END ~ ~ ~ ~ ~ ");

			// CORS policy
			builder.Services.AddCors(options =>
			{
				options.AddPolicy("BlazorCors", policy =>
				{
					policy.WithOrigins("https://localhost:7172")
						  .AllowAnyHeader()
						  .AllowAnyMethod();
				});
			});

			// Add services to the container.
			builder.Services.AddSingleton<OOCL.OpenCl.OpenClService>();
			// Here please add the AudioCollection as Singleton with the SaveMemory option (Set fielt to saveMemory)
			// Correct the syntax for adding AudioCollection as a singleton service
			builder.Services.AddSingleton<OOCL.Core.AudioCollection>(provider =>
				new OOCL.Core.AudioCollection
				{
					SaveMemory = saveMemory,
					DefaultPlaybackVolume = defaultVolume,
					AnimationDelay = waveformFps
				});

			builder.Services.AddSingleton<OOCL.Core.ImageCollection>(provider =>
			new OOCL.Core.ImageCollection
				{
					SaveMemory = saveMemory,
					DefaultWidth = 720,
					DefaultHeight = 480
				});

			builder.Services.InjectClipboard();

			// Set spare workers (threads / cores) for OpenCL
			CommonStatics.SpareWorkers = spareWorkers;

			// Swagger/OpenAPI
			builder.Services.AddEndpointsApiExplorer();
			if (useSwagger)
			{
				// Show full Swagger UI with endpoints
				builder.Services.AddSwaggerGen();
			}
			else
			{
				builder.Services.AddSwaggerGen(c =>
				{
					c.SwaggerDoc("v1", new OpenApiInfo
					{
						Version = "v1",
						Title = "APICL",
						Description = "API + WebApp using OpenCL for media manipulation",
						TermsOfService = new Uri("https://localhost:7172/terms"),
						Contact = new OpenApiContact { Name = "Developer", Email = "marcel.king91299@gmail.com" }
					});

					c.AddServer(new OpenApiServer { Url = "https://localhost:7171" });
					c.DocInclusionPredicate((_, api) => !string.IsNullOrWhiteSpace(api.GroupName));
					c.TagActionsBy(api => [api.GroupName ?? "Default"]);
				});
			}

			// Request Body Size Limits
			builder.WebHost.ConfigureKestrel(options =>
			{
				options.Limits.MaxRequestBodySize = maxUploadSize;
			});

			builder.Services.Configure<IISServerOptions>(options =>
			{
				options.MaxRequestBodySize = maxUploadSize;
			});

			builder.Services.Configure<FormOptions>(options =>
			{
				options.MultipartBodyLengthLimit = maxUploadSize;
			});

			// Add Endpoints etc.
			builder.Services.AddControllers();
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();

			// Logging
			builder.Logging.AddConsole();
			builder.Logging.AddDebug();
			builder.Logging.SetMinimumLevel(LogLevel.Debug);

			var app = builder.Build();

			// Development-only Middlewares
			if (app.Environment.IsDevelopment())
			{
				app.UseSwagger(c =>
				{
					
				});

				if (useSwagger)
				{
					// Show endpoints
					app.UseSwaggerUI();
				}
				else
				{
					// Show only info page about the API
					app.UseSwaggerUI(c =>
					{
						c.SwaggerEndpoint("/swagger/v1/swagger.json", "APICL v1.0");
					});
				}
			}

			app.UseStaticFiles();
			app.UseHttpsRedirection();
			app.UseCors("BlazorCors");
			app.MapControllers();

			app.Run();
        }
    }

	public class ApiConfig
	{
		public string ServerName { get; set; } = string.Empty;
		public string ServerProtocol { get; set; } = string.Empty;
		public int ServerPort { get; set; } = 0;
		public string ServerUrl { get; set; } = string.Empty;
		public string FQDN { get; set; } = string.Empty;
		public string FQDN_fallback { get; set; } = string.Empty;
		public string ServerVersion { get; set; } = string.Empty;
		public string ServerDescription { get; set; } = string.Empty;
		public int InitializeDeviceId { get; set; } = -1;
		public string DefaultDeviceName { get; set; } = string.Empty;
	}
}
