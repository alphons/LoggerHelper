using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LoggerExtensions;

public static class CustomLogging
{
	public static IServiceCollection AddCustomLogging(this IServiceCollection services, IConfiguration configuration)
	{
		services.AddLogging(logging =>
		{
			logging.ClearProviders();
			logging.AddFilter("Microsoft.AspNetCore.Watch", LogLevel.None);
			logging.AddFilter("Microsoft.AspNetCore.Watch.BrowserRefresh", LogLevel.None);
			if (configuration.GetSection("CustomLogging").GetValue<bool>("ConsoleLogging"))
			{
				logging.AddSimpleConsole(c =>
				{
					c.SingleLine = true;
					c.TimestampFormat = "HH:mm:ss ";
				});
			}
			if (configuration.GetSection("CustomLogging").GetValue<bool>("FileLogging"))
			{
				logging.AddFileLogger("Logs");
			}
			// Minimum level from configuration (standard Logging section)
			// Supports: "Trace", "Debug", "Information", "Warning", "Error", "Critical", "None"
			var loggingSection = configuration.GetSection("Logging");
			LogLevel minimumLevel = LogLevel.Information; // always default
			if (loggingSection.Exists())
			{
				string? minLevelString = loggingSection.GetValue<string>("LogLevel:Default");
				if (!string.IsNullOrWhiteSpace(minLevelString) &&
					Enum.TryParse<LogLevel>(minLevelString, true, out LogLevel parsedLevel))
				{
					minimumLevel = parsedLevel;
				}
			}
			logging.SetMinimumLevel(minimumLevel);
		});
		return services;
	}

	public static ILogger<T> CreateLogger<T>(IConfiguration configuration)
	{
		var services = new ServiceCollection();

		services.AddCustomLogging(configuration);

		var sp = services.BuildServiceProvider();

		var logger = sp.GetRequiredService<ILogger<T>>();

		return logger;
	}
}

