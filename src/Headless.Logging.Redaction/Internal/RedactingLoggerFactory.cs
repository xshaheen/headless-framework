// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections;
using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
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
/// A property whose name an app classified (<c>ClassifyProperty</c>) is redacted the same way, by name, in every entry
/// and scope, through the same redactor a classification attribute would use, so both yield the same pseudonym.
/// </para>
/// <para>
/// The factory owns the one it wraps, which the container no longer tracks once it is decorated.
/// </para>
/// </remarks>
internal sealed class RedactingLoggerFactory(
    ILoggerFactory inner,
    IRedactorProvider? redactorProvider,
    ILogPathRedactor[] pathRedactors,
    LogTextRedactorRegistration[] textRedactors,
    FrozenDictionary<string, Redactor> propertyRedactors
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
            : new RedactingLogger(logger, redactorProvider, redactsPaths ? pathRedactors : [], propertyRedactors);
    }

    public void AddProvider(ILoggerProvider provider) => inner.AddProvider(provider);

    public void Dispose() => inner.Dispose();

    private sealed class RedactingLogger(
        ILogger inner,
        IRedactorProvider? redactorProvider,
        ILogPathRedactor[] pathRedactors,
        FrozenDictionary<string, Redactor> propertyRedactors
    ) : ILogger
    {
        // Logging renders a null value in a collection as this text.
        private const string _NullText = "(null)";

        // The request scope names the path RequestPath; the request lines name it Path, beside a separate PathBase.
        private const string _ScopePathKey = "RequestPath";
        private const string _LinePathKey = "Path";

        public bool IsEnabled(LogLevel logLevel) => inner.IsEnabled(logLevel);

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            if (_TryRedactNamedScope(state, out var named))
            {
                return _TryRedactPath(named, _ScopePathKey, out var namedPath)
                    ? inner.BeginScope(namedPath)
                    : inner.BeginScope(named);
            }

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

            if (
                propertyRedactors.Count > 0
                && state is IReadOnlyList<KeyValuePair<string, object?>> values
                && inner.IsEnabled(logLevel)
                && _TryRedactNames(values, out var named, out var rewrites)
            )
            {
                _LogNamed(logLevel, eventId, named, _Rewrite(formatter(state, exception), rewrites), exception);

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
            List<(string Raw, string Redacted)>? rewrites = null;

            // A plain parameter whose name the app classified is redacted here too, so one entry is never half done.
            for (var i = 0; i < state.TagsCount && propertyRedactors.Count > 0; i++)
            {
                if (_TryRedactNamed(values[i], out var namedValue, out var rewrite))
                {
                    values[i] = namedValue;
                    _AddRewrite(ref rewrites, rewrite);
                }
            }

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

            var message = _Rewrite(formatter((TState)(object)state, exception), rewrites);

#pragma warning disable CA1873 // False positive: Log checks inner.IsEnabled before calling this method.
            inner.Log(logLevel, eventId, new RedactedState(values, message), exception, static (s, _) => s.ToString());
#pragma warning restore CA1873
        }

        private void _LogNamed(
            LogLevel logLevel,
            EventId eventId,
            KeyValuePair<string, object?>[] values,
            string message,
            Exception? exception
        )
        {
            var named = new RedactedState(values, message);

            // ASP.NET Core's request line can carry both a classified name and a credential path.
            if (_TryRedactPath(named, _LinePathKey, out var pathRedacted))
            {
                var rewritten = pathRedacted.Rewrite(message);
                inner.Log(logLevel, eventId, pathRedacted, exception, (_, _) => rewritten);

                return;
            }

#pragma warning disable CA1873 // False positive: the caller checked inner.IsEnabled before calling this method.
            inner.Log(logLevel, eventId, named, exception, static (s, _) => s.ToString());
#pragma warning restore CA1873
        }

        private bool _TryRedactNamedScope<TState>(TState state, [NotNullWhen(true)] out RedactedState? redacted)
        {
            redacted = null;

            if (propertyRedactors.Count == 0)
            {
                return false;
            }

            IReadOnlyList<KeyValuePair<string, object?>>? values = state switch
            {
                IReadOnlyList<KeyValuePair<string, object?>> list => list,
                IEnumerable<KeyValuePair<string, object?>> pairs => [.. pairs],
                // KeyValuePair is a struct, so a string-valued dictionary is not covariant with the object-valued case.
                IEnumerable<KeyValuePair<string, string>> pairs =>
                [
                    .. pairs.Select(static pair => new KeyValuePair<string, object?>(pair.Key, pair.Value)),
                ],
                _ => null,
            };

            if (values is null || !_TryRedactNames(values, out var named, out var rewrites))
            {
                return false;
            }

            redacted = new RedactedState(named, _Rewrite(state?.ToString() ?? string.Empty, rewrites));

            return true;
        }

        // Copies the values only once a name matches; returns false, and no copy, when none did.
        private bool _TryRedactNames(
            IReadOnlyList<KeyValuePair<string, object?>> values,
            [NotNullWhen(true)] out KeyValuePair<string, object?>[]? named,
            out List<(string Raw, string Redacted)>? rewrites
        )
        {
            named = null;
            rewrites = null;

            for (var i = 0; i < values.Count; i++)
            {
                if (_TryRedactNamed(values[i], out var namedValue, out var rewrite))
                {
                    named ??= [.. values];
                    named[i] = namedValue;
                    _AddRewrite(ref rewrites, rewrite);
                }
            }

            return named is not null;
        }

        private bool _TryRedactNamed(
            KeyValuePair<string, object?> pair,
            out KeyValuePair<string, object?> named,
            out (string Raw, string Redacted) rewrite
        )
        {
            named = pair;
            rewrite = default;

            if (pair.Value is null || !propertyRedactors.TryGetValue(pair.Key, out var redactor))
            {
                return false;
            }

            if (pair.Value is IEnumerable items and not string)
            {
                // Rendered the way logging renders a collection, element by element, so the text and each element
                // carry the same redacted values.
                List<string> raw = [];
                List<string> redactedItems = [];

                foreach (var item in items)
                {
                    raw.Add(
                        item is null ? _NullText : Convert.ToString(item, CultureInfo.InvariantCulture) ?? string.Empty
                    );
                    redactedItems.Add(item is null ? _NullText : redactor.Redact(item));
                }

                named = new(pair.Key, redactedItems.ToArray());
                rewrite = (string.Join(", ", raw), string.Join(", ", redactedItems));

                return true;
            }

            var redactedValue = redactor.Redact(pair.Value);
            named = new(pair.Key, redactedValue);
            rewrite = (Convert.ToString(pair.Value, CultureInfo.InvariantCulture) ?? string.Empty, redactedValue);

            return true;
        }

        private static void _AddRewrite(
            ref List<(string Raw, string Redacted)>? rewrites,
            (string Raw, string Redacted) rewrite
        )
        {
            // An empty raw text has nothing to find in the message, and string.Replace rejects it.
            if (rewrite.Raw.Length > 0)
            {
                (rewrites ??= []).Add(rewrite);
            }
        }

        // The message is rendered once from the raw values, then each raw value is replaced, the way a credential path
        // is: logging renders a hole with the invariant culture, which is the text replaced here.
        private static string _Rewrite(string message, List<(string Raw, string Redacted)>? rewrites)
        {
            if (rewrites is null)
            {
                return message;
            }

            foreach (var (raw, redacted) in rewrites)
            {
                message = message.Replace(raw, redacted, StringComparison.Ordinal);
            }

            return message;
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
