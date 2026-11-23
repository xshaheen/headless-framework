// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Logging;

namespace Tests;

/// <summary>Records every entry and active scope a logger factory hands its providers.</summary>
internal sealed class CapturingProvider : ILoggerProvider, ISupportExternalScope
{
    private readonly Lock _lock = new();
    private IExternalScopeProvider? _scopes;

    public List<Entry> Entries { get; } = [];

    public List<IReadOnlyList<KeyValuePair<string, object?>>> Scopes { get; } = [];

    public ILogger CreateLogger(string categoryName) => new Logger(this);

    public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopes = scopeProvider;

    public void Dispose() { }

    internal sealed record Entry(string Message, IReadOnlyDictionary<string, object?> Properties);

    private sealed class Logger(CapturingProvider owner) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => owner._scopes?.Push(state);

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            var properties = new Dictionary<string, object?>(StringComparer.Ordinal);

            if (state is IEnumerable<KeyValuePair<string, object?>> values)
            {
                foreach (var (key, value) in values)
                {
                    properties[key] = value;
                }
            }

            lock (owner._lock)
            {
                owner._scopes?.ForEachScope(
                    static (scope, scopes) =>
                    {
                        if (scope is IEnumerable<KeyValuePair<string, object?>> pairs)
                        {
                            scopes.Add([.. pairs]);
                        }
                    },
                    owner.Scopes
                );

                owner.Entries.Add(new Entry(formatter(state, exception), properties));
            }
        }
    }
}
