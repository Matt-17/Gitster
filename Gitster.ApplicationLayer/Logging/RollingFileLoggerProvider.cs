using System.Globalization;
using System.IO;
using System.Text;

using Microsoft.Extensions.Logging;

namespace Gitster.ApplicationLayer.Logging;

public sealed class RollingFileLoggerProvider : ILoggerProvider
{
    private readonly object _gate = new();
    private readonly string _directory;
    private readonly int _retentionDays;

    public RollingFileLoggerProvider(string directory, int retentionDays = 7)
    {
        _directory = directory;
        _retentionDays = Math.Max(1, retentionDays);
        Directory.CreateDirectory(_directory);
        PruneOldLogs();
    }

    public ILogger CreateLogger(string categoryName) => new RollingFileLogger(this, categoryName);

    public void Dispose()
    {
    }

    internal void Write(
        string category,
        LogLevel level,
        EventId eventId,
        string message,
        Exception? exception)
    {
        if (level == LogLevel.None)
            return;

        var now = DateTimeOffset.Now;
        var path = Path.Combine(_directory, $"gitster-{now:yyyyMMdd}.log");
        var line = FormatLine(now, category, level, eventId, message, exception);

        lock (_gate)
        {
            File.AppendAllText(path, line, Encoding.UTF8);
        }
    }

    private static string FormatLine(
        DateTimeOffset timestamp,
        string category,
        LogLevel level,
        EventId eventId,
        string message,
        Exception? exception)
    {
        var builder = new StringBuilder()
            .Append(timestamp.ToString("O", CultureInfo.InvariantCulture))
            .Append(" [")
            .Append(level)
            .Append("] ")
            .Append(category);

        if (eventId.Id != 0 || !string.IsNullOrWhiteSpace(eventId.Name))
            builder.Append(" (").Append(eventId).Append(')');

        builder.Append(": ").Append(message);
        if (exception is not null)
            builder.AppendLine().Append(exception);

        return builder.AppendLine().ToString();
    }

    private void PruneOldLogs()
    {
        try
        {
            var cutoff = DateTimeOffset.Now.AddDays(-_retentionDays);
            foreach (var file in Directory.EnumerateFiles(_directory, "gitster-*.log"))
            {
                var info = new FileInfo(file);
                if (info.LastWriteTime < cutoff.LocalDateTime)
                    info.Delete();
            }
        }
        catch
        {
            // Logging must never block application startup.
        }
    }

    private sealed class RollingFileLogger : ILogger
    {
        private readonly RollingFileLoggerProvider _provider;
        private readonly string _category;

        public RollingFileLogger(RollingFileLoggerProvider provider, string category)
        {
            _provider = provider;
            _category = category;
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull =>
            NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;

            _provider.Write(_category, logLevel, eventId, formatter(state, exception), exception);
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new();
        public void Dispose()
        {
        }
    }
}
