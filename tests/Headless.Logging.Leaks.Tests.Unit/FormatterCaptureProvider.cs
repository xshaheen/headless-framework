// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;

namespace Tests;

/// <summary>
/// Writes every entry through Microsoft's console formatter into a <see cref="TextWriter"/>, so the suite reads the exact
/// bytes the console provider would print without touching the process-wide stdout.
/// </summary>
internal sealed class FormatterCaptureProvider(ConsoleFormatter formatter, TextWriter writer)
    : ILoggerProvider,
        ISupportExternalScope
{
    private readonly Lock _lock = new();
    private IExternalScopeProvider _scopes = new LoggerExternalScopeProvider();

    public ILogger CreateLogger(string categoryName) => new CaptureLogger(categoryName, this);

    public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopes = scopeProvider;

    public void Dispose() { }

    private sealed class CaptureLogger(string category, FormatterCaptureProvider owner) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => owner._scopes.Push(state);

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            var entry = new LogEntry<TState>(logLevel, category, eventId, state, exception, formatter);

            lock (owner._lock)
            {
                owner.Formatter.Write(in entry, owner._scopes, owner.Writer);
            }
        }
    }

    private ConsoleFormatter Formatter => formatter;

    private TextWriter Writer => writer;
}
