using Microsoft.Extensions.Logging;
using VibeShadowsocks.Infrastructure.Options;

namespace VibeShadowsocks.Infrastructure.Diagnostics;

public sealed class SimpleFileLoggerProvider(AppPaths paths) : ILoggerProvider
{
    private readonly AppPaths _paths = paths;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly Dictionary<string, SimpleFileLogger> _loggers = new(StringComparer.OrdinalIgnoreCase);

    public ILogger CreateLogger(string categoryName)
    {
        lock (_loggers)
        {
            if (_loggers.TryGetValue(categoryName, out var existing))
            {
                return existing;
            }

            var logger = new SimpleFileLogger(categoryName, _paths, _writeGate);
            _loggers[categoryName] = logger;
            return logger;
        }
    }

    public void Dispose()
    {
        lock (_loggers)
        {
            _loggers.Clear();
        }

        _writeGate.Dispose();
    }
}

internal sealed class SimpleFileLogger(string categoryName, AppPaths paths, SemaphoreSlim writeGate) : ILogger
{
    private readonly string _categoryName = categoryName;
    private readonly AppPaths _paths = paths;
    private readonly SemaphoreSlim _writeGate = writeGate;

    public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        var message = formatter(state, exception);
        if (string.IsNullOrWhiteSpace(message) && exception is null)
        {
            return;
        }

        var line = $"{DateTimeOffset.UtcNow:O} [{logLevel}] {_categoryName}: {message}";
        if (exception is not null)
        {
            line += Environment.NewLine + exception;
        }

        try
        {
            WriteLine(line).GetAwaiter().GetResult();
        }
        catch
        {
        }
    }

    private async Task WriteLine(string line)
    {
        Directory.CreateDirectory(_paths.LogsDirectory);
        var path = Path.Combine(_paths.LogsDirectory, $"app-{DateTimeOffset.Now:yyyyMMdd}.log");

        await _writeGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await File.AppendAllTextAsync(path, line + Environment.NewLine).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose()
        {
        }
    }
}
