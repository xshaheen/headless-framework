using Microsoft.Extensions.Logging;

namespace Tests.Helpers;

/// <summary>
/// In-memory <see cref="ILoggerProvider"/> that records the <see cref="LogLevel"/> and
/// <see cref="EventId"/> of every log entry it sees. Intended for tests that need to assert
/// on EventId-level log emission (e.g. EventId 79 / 80 from the retry processor).
/// Captures only metadata, never the formatted message string, to keep assertions stable
/// against message-template changes.
/// </summary>
public sealed class CapturingLoggerProvider(List<(LogLevel Level, EventId EventId)> log) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName)
    {
        return new CapturingLogger(log);
    }

    public void Dispose() { }

    private sealed class CapturingLogger(List<(LogLevel Level, EventId EventId)> log) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            lock (log)
            {
                log.Add((logLevel, eventId));
            }
        }
    }
}
