using Microsoft.Extensions.Logging;

namespace Belac.Dock.Infrastructure;

public sealed class RollingFileLoggerProvider : ILoggerProvider
{
    private readonly string _directory;
    private readonly object _gate = new();

    public RollingFileLoggerProvider(string directory)
    {
        _directory = directory;
        Directory.CreateDirectory(directory);
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    public void Dispose()
    {
    }

    private void Write(string line)
    {
        if (line.Contains("Bearer ", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("token=", StringComparison.OrdinalIgnoreCase))
            return;

        if (line.Length > 2000)
            line = line[..2000];

        lock (_gate)
        {
            var path = Path.Combine(_directory, "dock.log");
            var info = new FileInfo(path);
            if (info.Exists && info.Length > 1_000_000)
            {
                File.Copy(path, Path.Combine(_directory, "dock.1.log"), true);
                File.WriteAllText(path, "");
            }

            File.AppendAllText(path, line + Environment.NewLine);
        }
    }

    private sealed class FileLogger(RollingFileLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;

            var message = formatter(state, exception);
            var suffix = exception is null ? "" : " " + exception.GetType().Name;
            provider.Write($"{DateTimeOffset.Now:O} {logLevel} {category}: {message}{suffix}");
        }
    }
}
