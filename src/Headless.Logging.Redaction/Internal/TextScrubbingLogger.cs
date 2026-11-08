// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections;
using System.Globalization;
using Microsoft.Extensions.Logging;

namespace Headless.Logging.Internal;

/// <summary>
/// Runs the registered <see cref="ILogTextRedactor"/>s over every string a log entry or scope carries, and over the
/// rendered message, before the provider sees them.
/// </summary>
/// <remarks>
/// <para>
/// It sits beneath the classification and path stages, so it only ever sees their already-redacted copies. Every value
/// a provider might keep is copied out during the call.
/// </para>
/// <para>
/// A non-string value is scanned in its invariant text form, because a template-rendering provider such as Serilog
/// prints the value itself and ignores the scrubbed formatter output. A value whose text matches is replaced by its
/// scrubbed text; any other value keeps its type.
/// </para>
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
                new ScrubbedState(_Scrub(values), _ScrubText(state))
            ),
            // KeyValuePair is a struct, so a string-valued dictionary is not covariant with the object-valued case.
            IEnumerable<KeyValuePair<string, string>> values => inner.BeginScope(
                new ScrubbedState(
                    _Scrub(values.Select(static pair => new KeyValuePair<string, object?>(pair.Key, pair.Value))),
                    _ScrubText(state)
                )
            ),
            // Providers print any other scope (an anonymous object, a custom type) through ToString, so scan that text.
            _ => _ScrubValue(state) is string scrubbed ? inner.BeginScope(scrubbed) : inner.BeginScope(state),
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
        return [.. values.Select(pair => new KeyValuePair<string, object?>(pair.Key, _ScrubValue(pair.Value)))];
    }

    private object? _ScrubValue(object? value)
    {
        switch (value)
        {
            case null:
                return null;
            case string text:
                return _Scrub(text);
        }

        var formatted = Convert.ToString(value, CultureInfo.InvariantCulture);

        if (formatted is null)
        {
            return value;
        }

        var scrubbed = _Scrub(formatted);

        return ReferenceEquals(scrubbed, formatted) || string.Equals(scrubbed, formatted, StringComparison.Ordinal)
            ? value
            : scrubbed;
    }

    private string _ScrubText(object state) => state.ToString() is { } text ? _Scrub(text) : string.Empty;

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
