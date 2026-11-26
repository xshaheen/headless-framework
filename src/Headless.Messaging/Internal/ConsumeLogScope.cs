// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;

namespace Headless.Messaging.Internal;

/// <summary>The log fields of the consume running on the current async flow.</summary>
internal static class ConsumeLogScope
{
    private static readonly AsyncLocal<string?> _CausationTraceId = new();

    /// <summary>Gets the trace id stored with the consumed message, or <see langword="null"/> outside a consume.</summary>
    internal static string? CausationTraceId => _CausationTraceId.Value;

    /// <summary>
    /// Opens the log fields of one consume from its envelope headers. Disposing restores the fields of the consume
    /// that was current before, so a nested consume never leaks its trace into the outer one's later entries.
    /// </summary>
    internal static Scope Begin(IDictionary<string, string?> headers)
    {
        var previous = _CausationTraceId.Value;

        // Parsed once per consume, not per entry. The header comes off the wire, so only a well-formed W3C value is
        // logged, and it is logged as the parsed 32-hex trace id, never as the raw text.
        _CausationTraceId.Value =
            headers.TryGetValue(Headers.TraceParent, out var traceParent)
            && traceParent is not null
            && ActivityContext.TryParse(traceParent, traceState: null, out var context)
                ? context.TraceId.ToHexString()
                : null;

        return new Scope(previous);
    }

    internal readonly struct Scope(string? previous) : IDisposable
    {
        public void Dispose() => _CausationTraceId.Value = previous;
    }
}
