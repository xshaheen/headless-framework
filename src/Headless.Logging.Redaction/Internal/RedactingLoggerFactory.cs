// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections;
using Microsoft.Extensions.Compliance.Redaction;
using Microsoft.Extensions.Logging;

namespace Headless.Logging.Internal;

/// <summary>
/// Wraps the registered <see cref="ILoggerFactory"/>, whichever stack owns it, so every logger it creates applies the
/// host's redaction before any provider sees an entry.
/// </summary>
/// <remarks>
/// <para>
/// It does two things. A <c>[LoggerMessage]</c> parameter that carries a data classification is redacted through the
/// <see cref="IRedactorProvider"/> and handed to providers as an ordinary value. Microsoft's <c>EnableRedaction</c> does
/// that only when Microsoft's own factory is in charge; under Serilog's factory the parameter was dropped and the text
/// kept a literal <c>{Name}</c> hole. And a path an <see cref="ILogPathRedactor"/> recognizes is replaced in ASP.NET
/// Core's request scope and request lines, which the hosting layer writes before any middleware runs.
/// </para>
/// <para>
/// The factory owns the one it wraps, which the container no longer tracks once it is decorated.
/// </para>
/// </remarks>
internal sealed class RedactingLoggerFactory(
    ILoggerFactory inner,
    IRedactorProvider? redactorProvider,
    ILogPathRedactor[] pathRedactors,
    LogTextRedactorRegistration[] textRedactors
) : ILoggerFactory
{
    /// <summary>The category ASP.NET Core's hosting layer opens the request scope and writes the request lines under.</summary>
    internal const string HostingDiagnosticsCategory = "Microsoft.AspNetCore.Hosting.Diagnostics";

    public ILogger CreateLogger(string categoryName)
    {
        var logger = inner.CreateLogger(categoryName);

        // Text scrubbing sits beneath the other stages, so it scans their already-redacted copies.
        // Patterns scoped to other categories are dropped here, so a category none applies to pays nothing.
        LogTextRedactorRegistration[] applicable = [.. textRedactors.Where(r => r.AppliesTo(categoryName))];

        if (applicable.Length > 0)
        {
            logger = new TextScrubbingLogger(logger, applicable);
        }

        var redactsPaths =
            pathRedactors.Length > 0
            && string.Equals(categoryName, HostingDiagnosticsCategory, StringComparison.Ordinal);

        // A logger with nothing to redact is returned as is, so the wrapper costs nothing on those categories.
        return redactorProvider is null && !redactsPaths
            ? logger
            : new RedactingLogger(logger, redactorProvider, redactsPaths ? pathRedactors : []);
    }

    public void AddProvider(ILoggerProvider provider) => inner.AddProvider(provider);

    public void Dispose() => inner.Dispose();

    private sealed class RedactingLogger(
        ILogger inner,
        IRedactorProvider? redactorProvider,
        ILogPathRedactor[] pathRedactors
    ) : ILogger
    {
        // The request scope names the path RequestPath; the request lines name it Path, beside a separate PathBase.
        private const string _ScopePathKey = "RequestPath";
        private const string _LinePathKey = "Path";

        public bool IsEnabled(LogLevel logLevel) => inner.IsEnabled(logLevel);

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return _TryRedactPath(state, _ScopePathKey, out var redacted)
                ? inner.BeginScope(redacted)
                : inner.BeginScope(state);
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            // A level nobody listens to costs nothing: no copy, no redactor call.
            if (
                redactorProvider is not null
                && inner.IsEnabled(logLevel)
                && state is LoggerMessageState { ClassifiedTagsCount: > 0 } classified
                && classified.RedactedTagArray.Length >= classified.ClassifiedTagsCount
            )
            {
                _LogClassified(logLevel, eventId, classified, exception, formatter, redactorProvider);

                return;
            }

            if (_TryRedactPath(state, _LinePathKey, out var redacted))
            {
                // The message is rendered from the original state once, then rewritten, so the line keeps whatever
                // shape the hosting layer gives it; the state's own values carry the redacted path for structured sinks.
                var message = redacted.Rewrite(formatter(state, exception));

                inner.Log(logLevel, eventId, redacted, exception, (_, _) => message);

                return;
            }

            inner.Log(logLevel, eventId, state, exception, formatter);
        }

        // The generator's state is a thread-local instance it clears once Log returns, so everything a provider might
        // keep is copied out here, and the raw classified values never leave this method.
        private void _LogClassified<TState>(
            LogLevel logLevel,
            EventId eventId,
            LoggerMessageState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter,
            IRedactorProvider provider
        )
        {
            var values = new KeyValuePair<string, object?>[state.TagsCount + state.ClassifiedTagsCount];
            Array.Copy(state.TagArray, values, state.TagsCount);

            for (var i = 0; i < state.ClassifiedTagsCount; i++)
            {
                var tag = state.ClassifiedTagArray[i];
                var redactedValue = tag.Value is null
                    ? null
                    : provider.GetRedactor(tag.Classifications).Redact(tag.Value);

                // The generated formatter renders a classified parameter from RedactedTagArray, the slot Microsoft's
                // own redacting logger fills; filling it here makes the rendered text carry the redacted value too.
                state.RedactedTagArray[i] = new(tag.Name, redactedValue);
                values[state.TagsCount + i] = new(tag.Name, redactedValue);
            }

            var message = formatter((TState)(object)state, exception);

#pragma warning disable CA1873 // False positive: Log checks inner.IsEnabled before calling this method.
            inner.Log(logLevel, eventId, new RedactedState(values, message), exception, static (s, _) => s.ToString());
#pragma warning restore CA1873
        }

        private bool _TryRedactPath<TState>(TState state, string pathKey, out PathRedactedState redacted)
        {
            redacted = null!;

            if (pathRedactors.Length == 0 || state is not IReadOnlyList<KeyValuePair<string, object?>> values)
            {
                return false;
            }

            for (var i = 0; i < values.Count; i++)
            {
                var (key, value) = values[i];

                if (
                    !string.Equals(key, pathKey, StringComparison.Ordinal)
                    || value is not string raw
                    || raw.Length == 0
                    || raw[0] != '/'
                )
                {
                    continue;
                }

                foreach (var pathRedactor in pathRedactors)
                {
                    if (pathRedactor.TryRedact(raw, out var path))
                    {
                        redacted = new PathRedactedState(values, i, raw, path, state.ToString());

                        return true;
                    }
                }

                return false;
            }

            return false;
        }
    }

    /// <summary>A copy of a classified log state's values, already redacted, with its rendered text.</summary>
    private sealed class RedactedState(KeyValuePair<string, object?>[] values, string text)
        : IReadOnlyList<KeyValuePair<string, object?>>
    {
        public int Count => values.Length;

        public KeyValuePair<string, object?> this[int index] => values[index];

        public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() =>
            ((IEnumerable<KeyValuePair<string, object?>>)values).GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public override string ToString() => text;
    }

    /// <summary>A copy of a log state with its path value redacted, rendering as the original text with the path replaced.</summary>
    private sealed class PathRedactedState : IReadOnlyList<KeyValuePair<string, object?>>
    {
        private readonly KeyValuePair<string, object?>[] _values;
        private readonly string _raw;
        private readonly string _redacted;
        private readonly string? _text;

        public PathRedactedState(
            IReadOnlyList<KeyValuePair<string, object?>> values,
            int pathIndex,
            string raw,
            string redacted,
            string? text
        )
        {
            _values = new KeyValuePair<string, object?>[values.Count];

            for (var i = 0; i < values.Count; i++)
            {
                _values[i] = i == pathIndex ? new(values[i].Key, redacted) : values[i];
            }

            _raw = raw;
            _redacted = redacted;
            _text = text is null ? null : Rewrite(text);
        }

        public int Count => _values.Length;

        public KeyValuePair<string, object?> this[int index] => _values[index];

        public string Rewrite(string text) => text.Replace(_raw, _redacted, StringComparison.Ordinal);

        public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() =>
            ((IEnumerable<KeyValuePair<string, object?>>)_values).GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public override string ToString() => _text ?? string.Empty;
    }
}
