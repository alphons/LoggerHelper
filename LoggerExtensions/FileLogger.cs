using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Threading.Channels;

namespace LoggerExtensions;

public sealed class FileLogger : ILogger, IDisposable
{
	private readonly Channel<string> channel;
	private readonly Task consumerTask;
	private readonly CancellationTokenSource cts = new();
	private readonly string logDirectory;
	private StreamWriter? writer;
	private string currentFilePath = string.Empty;
	private DateTime currentDate = DateTime.MinValue;

	public FileLogger(string logDirectory = "Logs")
	{
		this.logDirectory = logDirectory;
		Directory.CreateDirectory(logDirectory);

		channel = Channel.CreateUnbounded<string>(
			new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

		consumerTask = Task.Run(() => ConsumeAsync(cts.Token));
	}

	public void Log<TState>(
		LogLevel logLevel,
		EventId eventId,
		TState state,
		Exception? exception,
		Func<TState, Exception?, string> formatter)
	{
		if (!IsEnabled(logLevel))
			return;

		var message = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{logLevel,-11}] {formatter(state, exception)}";

		if (exception != null)
			message += $"{Environment.NewLine}{exception}";

		message += Environment.NewLine;

		channel.Writer.TryWrite(message);
	}

	private async Task ConsumeAsync(CancellationToken ct)
	{
		try
		{
			await foreach (var message in channel.Reader.ReadAllAsync(ct))
			{
				await WriteMessageWithRetryAsync(message, ct);
			}
		}
		catch (OperationCanceledException) { }
		catch (Exception ex)
		{
			Console.Error.WriteLine($"FileLogger consumer error: {ex}");
		}
	}

	private async Task WriteMessageWithRetryAsync(string message, CancellationToken ct)
	{
		const int maxRetries = 3;

		for (int attempt = 0; attempt < maxRetries; attempt++)
		{
			try
			{
				await WriteMessageAsync(message, ct);
				return;
			}
			catch (IOException) when (attempt < maxRetries - 1)
			{
				await Task.Delay(100 * (int)Math.Pow(2, attempt), ct);
			}
			catch (Exception ex)
			{
				Console.Error.WriteLine($"Failed to write log: {ex.Message}");
				return;
			}
		}
	}

	private async Task WriteMessageAsync(string message, CancellationToken ct)
	{
		var today = DateTime.Today;

		if (writer == null || today != currentDate)
		{
			await CloseWriterAsync();
			currentDate = today;
			currentFilePath = Path.Combine(logDirectory, $"log-{today:yyyyMMdd}.txt");

			var stream = new FileStream(
				currentFilePath,
				FileMode.Append,
				FileAccess.Write,
				FileShare.ReadWrite,
				bufferSize: 65536,
				useAsync: true);

			writer = new StreamWriter(stream) { AutoFlush = false };
		}

		if (writer != null)
		{
			await writer.WriteAsync(message.AsMemory(), ct);
			await writer.FlushAsync(ct);
		}
	}

	private async Task CloseWriterAsync()
	{
		if (writer == null) return;

		try
		{
			await writer.FlushAsync();
			await writer.DisposeAsync();
		}
		catch { }
		finally
		{
			writer = null;
		}
	}

	public void Dispose()
	{
		channel.Writer.Complete();
		cts.CancelAfter(TimeSpan.FromSeconds(5));

		try
		{
			consumerTask.Wait(TimeSpan.FromSeconds(6));
		}
		catch { }

		CloseWriterAsync().GetAwaiter().GetResult();
		cts.Dispose();
	}

	public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

	public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;
}

public sealed class FileLoggerProvider(string logDirectory = "Logs") : ILoggerProvider
{
	private readonly FileLogger logger = new(logDirectory);

	public ILogger CreateLogger(string categoryName) => logger;

	public void Dispose() => logger.Dispose();
}

public static class LoggingBuilderExtensions
{
	public static ILoggingBuilder AddFileLogger(
		this ILoggingBuilder builder,
		string logDirectory = "Logs")
	{
		builder.Services.AddSingleton<ILoggerProvider>(sp =>
			new FileLoggerProvider(logDirectory));

		return builder;
	}
}
