// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Microsoft.Extensions.Logging;

namespace Headless.Logging.Internal;

/// <summary>Masks the text of every log entry and scope with the registered <see cref="ILogTextRedactor"/>s.</summary>
/// <remarks>
/// It sits beneath the classification and path stages, so it scans copies they already redacted. When nothing matches,
/// the original state and formatter reach the provider untouched, so an entry with nothing to mask costs one scan and
/// no copy.
/// </remarks>
internal sealed class TextScrubbingLogger(ILogger inner, LogTextRedactorRegistration[] registrations) : ILogger
{
    // Every registration scans every level unless one is scoped to a higher level, so the common case needs no filter.
    private readonly LogLevel _lowestLevel = registrations.Min(static r => r.MinimumLevel);

    public bool IsEnabled(LogLevel logLevel) => inner.IsEnabled(logLevel);

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull
    {
        // A scope has no level, so every registration scans it.
        return state switch
        {
            string text => inner.BeginScope(_Scrub(text, LogLevel.None)),
            IReadOnlyList<KeyValuePair<string, object?>> values => _TryScrub(values, LogLevel.None, out var scrubbed)
                ? inner.BeginScope(new ScrubbedState(scrubbed, _ScrubText(state, LogLevel.None)))
                : inner.BeginScope(state),
            IEnumerable<KeyValuePair<string, object?>> values => _BeginScope(state, [.. values]),
            // KeyValuePair is a struct, so a string-valued dictionary is not covariant with the object-valued case.
            IEnumerable<KeyValuePair<string, string>> values => _BeginScope(
                state,
                [.. values.Select(static pair => new KeyValuePair<string, object?>(pair.Key, pair.Value))]
            ),
            // Providers print any other scope (an anonymous object, a custom type) through ToString, so scan that text.
            _ => _ScrubValue(state, LogLevel.None) is string scrubbed
                ? inner.BeginScope(scrubbed)
                : inner.BeginScope(state),
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
        if (logLevel < _lowestLevel || !inner.IsEnabled(logLevel))
        {
            inner.Log(logLevel, eventId, state, exception, formatter);
            return;
        }

        var rendered = formatter(state, exception);
        var message = _Scrub(rendered, logLevel);
        var messageChanged =
            !ReferenceEquals(message, rendered) && !string.Equals(message, rendered, StringComparison.Ordinal);

        if (state is IReadOnlyList<KeyValuePair<string, object?>> values)
        {
            if (_TryScrub(values, logLevel, out var scrubbed))
            {
                inner.Log(
                    logLevel,
                    eventId,
                    new ScrubbedState(scrubbed, message),
                    exception,
                    static (s, _) => s.ToString()
                );
                return;
            }

            if (!messageChanged)
            {
                inner.Log(logLevel, eventId, state, exception, formatter);
                return;
            }

            // Only the text matched: keep every value, replace the message.
            inner.Log(
                logLevel,
                eventId,
                new ScrubbedState([.. values], message),
                exception,
                static (s, _) => s.ToString()
            );
            return;
        }

        if (!messageChanged)
        {
            inner.Log(logLevel, eventId, state, exception, formatter);
            return;
        }

        // A state that is not a list of values cannot be copied field by field, so providers get the masked text alone
        // rather than a state that may still hold the raw value.
        inner.Log(logLevel, eventId, new ScrubbedState([], message), exception, static (s, _) => s.ToString());
    }

    private IDisposable? _BeginScope<TState>(TState state, KeyValuePair<string, object?>[] values)
        where TState : notnull
    {
        return _TryScrub(values, LogLevel.None, out var scrubbed)
            ? inner.BeginScope(new ScrubbedState(scrubbed, _ScrubText(state, LogLevel.None)))
            : inner.BeginScope(state);
    }

    // Copies the values only once one of them changes; returns false, and no copy, when none did.
    private bool _TryScrub(
        IReadOnlyList<KeyValuePair<string, object?>> values,
        LogLevel logLevel,
        [NotNullWhen(true)] out KeyValuePair<string, object?>[]? scrubbed
    )
    {
        KeyValuePair<string, object?>[]? copy = null;

        for (var i = 0; i < values.Count; i++)
        {
            var (key, value) = values[i];
            var masked = _ScrubValue(value, logLevel);

            if (ReferenceEquals(masked, value))
            {
                continue;
            }

            copy ??= [.. values];
            copy[i] = new(key, masked);
        }

        scrubbed = copy;

        return copy is not null;
    }

    // Returns the same instance when nothing matched.
    private object? _ScrubValue(object? value, LogLevel logLevel)
    {
        switch (value)
        {
            case null:
                return null;
            case string text:
                var masked = _Scrub(text, logLevel);
                return string.Equals(masked, text, StringComparison.Ordinal) ? text : masked;
        }

        // These render to text no value shape can hide in, so formatting them would only cost an allocation.
        // Integers are still scanned: a 14-digit id held as a long renders as that id.
        if (_HasNoMaskableText(value))
        {
            return value;
        }

        var formatted = Convert.ToString(value, CultureInfo.InvariantCulture);

        if (formatted is null)
        {
            return value;
        }

        var scrubbed = _Scrub(formatted, logLevel);

        return string.Equals(scrubbed, formatted, StringComparison.Ordinal) ? value : scrubbed;
    }

    private static bool _HasNoMaskableText(object value)
    {
        return value is bool or Guid or Enum or DateTime or DateTimeOffset or DateOnly or TimeOnly or TimeSpan;
    }

    private string _ScrubText(object state, LogLevel logLevel) =>
        state.ToString() is { } text ? _Scrub(text, logLevel) : string.Empty;

    // LogLevel.None marks a scope, which every registration scans.
    private string _Scrub(string text, LogLevel logLevel)
    {
        foreach (var registration in registrations)
        {
            if (logLevel != LogLevel.None && logLevel < registration.MinimumLevel)
            {
                continue;
            }

            text = registration.Redactor.Redact(text);
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
