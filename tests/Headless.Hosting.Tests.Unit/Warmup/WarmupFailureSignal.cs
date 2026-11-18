// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Logging;

namespace Tests.Warmup;

/// <summary>
/// Completes <see cref="Recorded"/> when the warm-up runner logs a failed attempt. The runner stores the attempt's
/// error for the readiness check before it logs, so the health check names the error from that point on.
/// </summary>
internal sealed class WarmupFailureSignal : ILoggerProvider
{
    private const string _FailedEventName = "WarmupFailed";

    private readonly TaskCompletionSource _recorded = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task Recorded => _recorded.Task;

    public ILogger CreateLogger(string categoryName) => new SignalLogger(_recorded);

    public void Dispose() { }

    private sealed class SignalLogger(TaskCompletionSource recorded) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            if (string.Equals(eventId.Name, _FailedEventName, StringComparison.Ordinal))
            {
                recorded.TrySetResult();
            }
        }
    }
}
