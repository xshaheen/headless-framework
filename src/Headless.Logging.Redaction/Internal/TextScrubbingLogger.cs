// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections;
using Microsoft.Extensions.Logging;

namespace Headless.Logging.Internal;

/// <summary>
/// Runs the registered <see cref="ILogTextRedactor"/>s over every string a log entry or scope carries, and over the
/// rendered message, before the provider sees them.
/// </summary>
/// <remarks>
/// It sits beneath the classification and path stages, so it only ever sees their already-redacted copies. Every value
/// a provider might keep is copied out during the call.
/// </remarks>
internal sealed class TextScrubbingLogger(ILogger inner, ILogTextRedactor[] redactors) : ILogger
{
    public bool IsEnabled(LogLevel logLevel) => inner.IsEnabled(logLevel);

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull
    {
        return state switch
        {
            string text => inner.BeginScope(_Scrub(text)),
            IEnumerable<KeyValuePair<string, object?>> values => inner.BeginScope(
                new ScrubbedState(_Scrub(values), state.ToString() is { } t ? _Scrub(t) : string.Empty)
            ),
            _ => inner.BeginScope(state),
        };
    }

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter
    )
    {
        if (!inner.IsEnabled(logLevel))
        {
            return;
        }

        var message = _Scrub(formatter(state, exception));

        // A state that is not a list of values cannot be copied field by field, so providers get the scrubbed text
        // alone rather than a state that may still hold the raw value.
        var values = state is IEnumerable<KeyValuePair<string, object?>> pairs ? _Scrub(pairs) : [];

        inner.Log(logLevel, eventId, new ScrubbedState(values, message), exception, static (s, _) => s.ToString());
    }

    private KeyValuePair<string, object?>[] _Scrub(IEnumerable<KeyValuePair<string, object?>> values)
    {
        return [.. values.Select(pair => pair.Value is string text ? new(pair.Key, _Scrub(text)) : pair)];
    }

    private string _Scrub(string text)
    {
        foreach (var redactor in redactors)
        {
            text = redactor.Redact(text);
        }

        return text;
    }

    private sealed class ScrubbedState(KeyValuePair<string, object?>[] values, string text)
        : IReadOnlyList<KeyValuePair<string, object?>>
    {
        public int Count => values.Length;

        public KeyValuePair<string, object?> this[int index] => values[index];

        public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() =>
            ((IEnumerable<KeyValuePair<string, object?>>)values).GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public override string ToString() => text;
    }
}
