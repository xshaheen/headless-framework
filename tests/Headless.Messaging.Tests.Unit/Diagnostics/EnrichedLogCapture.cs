// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Tests.Diagnostics;

/// <summary>
/// Records every entry with the fields a provider receives after Microsoft's log enrichment, and the trace that was
/// ambient when it was written.
/// </summary>
internal sealed class EnrichedLogCapture : ILoggerProvider, ILogger
{
    private readonly ConcurrentQueue<CapturedLogEntry> _entries = new();

    public IReadOnlyList<CapturedLogEntry> Entries => [.. _entries];

    public CapturedLogEntry Single(string message) =>
        Entries.Should().ContainSingle(x => string.Equals(x.Message, message, StringComparison.Ordinal)).Subject;

    public ILogger CreateLogger(string categoryName) => this;

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
        var fields = new Dictionary<string, string?>(StringComparer.Ordinal);
        var keys = new List<string>();

        if (state is IReadOnlyList<KeyValuePair<string, object?>> tags)
        {
            foreach (var (key, value) in tags)
            {
                // A repeated key would be a defect of its own; keep the first and let Keys show the count.
                fields.TryAdd(key, value?.ToString());
                keys.Add(key);
            }
        }

        _entries.Enqueue(new(formatter(state, exception), fields, keys, Activity.Current?.TraceId ?? default));
    }

    public void Dispose() { }
}

internal sealed record CapturedLogEntry(
    string Message,
    IReadOnlyDictionary<string, string?> Fields,
    IReadOnlyList<string> Keys,
    ActivityTraceId TraceId
);
