// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Logging;

namespace Tests.Helpers;

/// <summary>
/// An <see cref="ILoggerProvider"/> that records the level, event id, and formatted message of every entry it sees, so
/// a test can assert that a code path logged a given event, and what the event named. Assert on the event id where the
/// template may change; assert on the message only for a value the template is meant to carry, such as a stream name.
/// </summary>
[PublicAPI]
public sealed class CapturingLoggerProvider(List<(LogLevel Level, EventId EventId, string Message)> log)
    : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName)
    {
        return new CapturingLogger(log);
    }

    public void Dispose() { }

    private sealed class CapturingLogger(List<(LogLevel Level, EventId EventId, string Message)> log) : ILogger
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
            var message = formatter(state, exception);

            // Log calls arrive from any thread; the list is the test's, so the provider serializes its writes.
            lock (log)
            {
                log.Add((logLevel, eventId, message));
            }
        }
    }
}
