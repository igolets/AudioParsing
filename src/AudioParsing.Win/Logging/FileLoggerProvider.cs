using System.IO;
using Microsoft.Extensions.Logging;

namespace AudioParsing.Win.Logging;

/// <summary>
/// <see cref="ILoggerProvider"/> appending diagnostics to
/// <c>%LocalAppData%\AudioParsing\logs\win-yyyyMMdd.log</c>. Logging never throws.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly object _gate = new();

    public ILogger CreateLogger(string categoryName) => new FileLogger(categoryName, this);

    public void Dispose()
    {
    }

    internal void Write(string categoryName, LogLevel level, string message)
    {
        string directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AudioParsing",
            "logs");
        string path = Path.Combine(directory, $"win-{DateTime.Now:yyyyMMdd}.log");
        string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {categoryName}: {message}{Environment.NewLine}";
        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(directory);
                File.AppendAllText(path, line);
            }
            catch (IOException)
            {
                // Logging must never break the app.
            }
            catch (UnauthorizedAccessException)
            {
                // Logging must never break the app.
            }
        }
    }

    private sealed class FileLogger : ILogger
    {
        private readonly string _categoryName;
        private readonly FileLoggerProvider _provider;

        public FileLogger(string categoryName, FileLoggerProvider provider)
        {
            _categoryName = categoryName;
            _provider = provider;
        }

        IDisposable ILogger.BeginScope<TState>(TState state) => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);

            if (!IsEnabled(logLevel))
            {
                return;
            }

            string message = formatter(state, exception);
            if (exception is not null)
            {
                message += $"{Environment.NewLine}{exception}";
            }

            _provider.Write(_categoryName, logLevel, message);
        }

        private sealed class NullScope : IDisposable
        {
            public static NullScope Instance { get; } = new();

            public void Dispose()
            {
            }
        }
    }
}
