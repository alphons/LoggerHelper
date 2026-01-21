using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LoggerExtensions;

public sealed class FileLogger : ILogger
{
	private readonly string filePath;
	private static readonly Lock lockObject = new();

	public FileLogger(string categoryName, string logDirectory = "Logs")
	{
		Directory.CreateDirectory(logDirectory);
		filePath = Path.Combine(logDirectory, $"log-{DateTime.Today:yyyyMMdd}.txt");
	}

	public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

	public bool IsEnabled(LogLevel logLevel) => true;

	public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
	{
		var message = $"{DateTime.Now:HH:mm:ss} [{logLevel}] {formatter(state, exception)}";
		if (exception != null)
			message += $"{Environment.NewLine}{exception}";

		lock (lockObject)
		{
			File.AppendAllText(filePath, message + Environment.NewLine);
		}
	}
}

public sealed class FileLoggerProvider(string logDirectory = "Logs") : ILoggerProvider
{
	public ILogger CreateLogger(string categoryName) => new FileLogger(categoryName, logDirectory);

	public void Dispose() { }
}

public static class LoggingBuilderExtensions
{
	public static ILoggingBuilder AddFileLogger(this ILoggingBuilder builder, string logDirectory = "Logs")
	{
		builder.Services.AddSingleton<ILoggerProvider>(sp => new FileLoggerProvider(logDirectory));
		return builder;
	}
}
