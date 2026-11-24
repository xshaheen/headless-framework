// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections;
using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
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
/// that only when Microsoft's own factory is in charge; under another library's factory the parameter is dropped and
/// the text keeps a literal <c>{Name}</c> hole. And a path an <see cref="ILogPathRedactor"/> recognizes is replaced in ASP.NET
/// Core's request scope and request lines, which the hosting layer writes before any middleware runs; the request lines'
/// query values are replaced too.
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

    // Built once: the projection caches each type's shape for every logger the factory creates.
    private readonly ClassifiedObjectProjection? _projection = redactorProvider is null
        ? null
        : new ClassifiedObjectProjection(redactorProvider);

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

        var hosting = string.Equals(categoryName, HostingDiagnosticsCategory, StringComparison.Ordinal);

        // A logger with nothing to redact is returned as is, so the wrapper costs nothing on those categories.
        return redactorProvider is null && !hosting
            ? logger
            : new RedactingLogger(
                logger,
                redactorProvider,
                hosting ? pathRedactors : [],
                hosting,
                propertyRedactors,
                _projection
            );
    }

    public void AddProvider(ILoggerProvider provider) => inner.AddProvider(provider);

    public void Dispose() => inner.Dispose();

    private sealed class RedactingLogger(
        ILogger inner,
        IRedactorProvider? redactorProvider,
        ILogPathRedactor[] pathRedactors,
        bool redactsQuery,
        FrozenDictionary<string, Redactor> propertyRedactors,
        ClassifiedObjectProjection? projection
    ) : ILogger
    {
        // Logging renders a null value in a collection as this text.
        private const string _NullText = "(null)";

        // The request scope names the path RequestPath; the request lines name it Path, beside a separate PathBase.
        private const string _ScopePathKey = "RequestPath";
        private const string _LinePathKey = "Path";
        private const string _LineQueryKey = "QueryString";
        private const string _RedactedQueryValue = "Redacted";

        // Logging's own key for a message's template; its formatted states carry it after the hole values.
        private const string _OriginalFormatKey = "{OriginalFormat}";

        // `{@Name}` and `{$Name}` keep the sigil in the key, so a classified name is looked up without it; the span
        // lookup avoids a substring per value. The table ignores case, which supports it; the empty table may not.
        private readonly FrozenDictionary<string, Redactor>.AlternateLookup<ReadOnlySpan<char>> _sigilFreeLookup =
            propertyRedactors.TryGetAlternateLookup<ReadOnlySpan<char>>(out var lookup) ? lookup : default;

        public bool IsEnabled(LogLevel logLevel) => inner.IsEnabled(logLevel);

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            if (_TryRedactNamedScope(state, out var named))
            {
                return _TryRedactRequest(named, _ScopePathKey, query: false, out var namedPath)
                    ? inner.BeginScope(namedPath)
                    : inner.BeginScope(named);
            }

            // An object passed as the scope itself is printed through ToString, so one with a classified member is
            // replaced by its redacted form, the way a `{Value}` hole is.
            if (
                projection is not null
                && LogStateShape<TState>.MayCarryObjects
                && state
                    is not (IEnumerable<KeyValuePair<string, object?>> or IEnumerable<KeyValuePair<string, string>>)
                && projection.TryProject(state, captured: false, out var text)
            )
            {
                return inner.BeginScope(text);
            }

            return _TryRedactRequest(state, _ScopePathKey, query: false, out var redacted)
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
                && state is LoggerMessageState { ClassifiedTagsCount: > 0 } classified
                && classified.RedactedTagArray.Length >= classified.ClassifiedTagsCount
                && inner.IsEnabled(logLevel)
            )
            {
                _LogClassified(logLevel, eventId, classified, exception, formatter, redactorProvider);

                return;
            }

            if (
                (
                    propertyRedactors.Count > 0
                    || (projection is not null && LogStateShape<TState>.MayCarryObjects && _MayHoldObject(state))
                )
                && state is IReadOnlyList<KeyValuePair<string, object?>> values
                && inner.IsEnabled(logLevel)
                && _TryRedactNames(values, out var named, out var rewrites)
            )
            {
                var message = _RenderNamed(state, exception, formatter, named, rewrites);
                _LogNamed(logLevel, eventId, named, message, exception);

                return;
            }

            if (_TryRedactRequest(state, _LinePathKey, redactsQuery, out var redacted))
            {
                // The message is rendered from the original state once, then rewritten, so the line keeps whatever
                // shape the hosting layer gives it; the state's own values carry the redacted path and query for
                // structured sinks.
                var message = redacted.Rewrite(formatter(state, exception));

                inner.Log(logLevel, eventId, redacted, exception, (_, _) => message);

                return;
            }

            inner.Log(logLevel, eventId, state, exception, formatter);
        }

        // A generated message's values sit in an array, so an entry built from strings, numbers, and dates is ruled out
        // without going through the list interface value by value; any other state is looked at in full.
        private static bool _MayHoldObject<TState>(TState state)
        {
            if (state is not LoggerMessageState generated)
            {
                return true;
            }

            var tags = generated.TagArray;

            for (var i = 0; i < generated.TagsCount; i++)
            {
                if (tags[i].Value is { } value && Type.GetTypeCode(value.GetType()) == TypeCode.Object)
                {
                    return true;
                }
            }

            return false;
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

            // A plain parameter whose name the app classified is redacted here too, so one entry is never half done.
            // The generated formatter reads each plain value from TagArray, so the redacted value goes back there and
            // the text is rendered from it rather than patched afterwards.
            for (var i = 0; i < state.TagsCount && (propertyRedactors.Count > 0 || projection is not null); i++)
            {
                if (_TryRedactNamed(values[i], out var namedValue, out _))
                {
                    values[i] = namedValue;
                    state.TagArray[i] = namedValue;
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

            var message = formatter((TState)(object)state, exception);

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

            // ASP.NET Core's request line can carry a classified name beside a credential path and a query.
            if (_TryRedactRequest(named, _LinePathKey, redactsQuery, out var pathRedacted))
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

            if (propertyRedactors.Count == 0 && (projection is null || !LogStateShape<TState>.MayCarryObjects))
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

            redacted = new RedactedState(
                named,
                _TryRenderTemplate(named, out var rendered)
                    ? rendered
                    : _Rewrite(state?.ToString() ?? string.Empty, rewrites)
            );

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

            if (pair.Value is null)
            {
                return false;
            }

            if (!_TryGetPropertyRedactor(pair.Key, out var redactor))
            {
                // An object whose members are classified is replaced by its redacted form; `{@Name}` keeps the `@`
                // in the key, which lets a record's members show as the capture asked.
                if (
                    projection is not null
                    && projection.TryProject(pair.Value, pair.Key.StartsWith('@'), out var projected)
                )
                {
                    named = new(pair.Key, projected);
                    rewrite = (_RenderLikeLogging(pair.Value), projected);

                    return true;
                }

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

        private bool _TryGetPropertyRedactor(string key, [NotNullWhen(true)] out Redactor? redactor)
        {
            if (key.Length > 1 && key[0] is '@' or '$')
            {
                return _sigilFreeLookup.Dictionary is not null
                    ? _sigilFreeLookup.TryGetValue(key.AsSpan(1), out redactor)
                    : propertyRedactors.TryGetValue(key[1..], out redactor);
            }

            return propertyRedactors.TryGetValue(key, out redactor);
        }

        // Renders the text from the redacted values, so a hole's alignment and format never decide whether the raw
        // value survives in the text, and a short value never matches unrelated text. Rewriting the raw rendering is
        // the fallback for a state that carries no template the holes can be matched to.
        private static string _RenderNamed<TState>(
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter,
            KeyValuePair<string, object?>[] named,
            List<(string Raw, string Redacted)>? rewrites
        )
        {
            // The generated formatter reads each value back from TagArray, whose slots the state's list exposes one for
            // one, so the redacted copy goes there; the generator clears the state once Log returns.
            if (state is LoggerMessageState generated && generated.ClassifiedTagsCount == 0)
            {
                Array.Copy(named, generated.TagArray, generated.TagsCount);

                return formatter(state, exception);
            }

            return _TryRenderTemplate(named, out var rendered)
                ? rendered
                : _Rewrite(formatter(state, exception), rewrites);
        }

        // Logging's formatted states list the hole values in template order, then the template under
        // "{OriginalFormat}". The template is rendered the way logging renders it: each hole becomes a composite-format
        // item that keeps its alignment and format, formatted with the invariant culture. Returns false, and the
        // caller falls back, when the holes do not line up with the values by position and name.
        private static bool _TryRenderTemplate(
            KeyValuePair<string, object?>[] values,
            [NotNullWhen(true)] out string? rendered
        )
        {
            rendered = null;
            var holeCount = values.Length - 1;

            if (
                holeCount < 0
                || values[holeCount].Value is not string template
                || !string.Equals(values[holeCount].Key, _OriginalFormatKey, StringComparison.Ordinal)
            )
            {
                return false;
            }

            var composite = new StringBuilder(template.Length + (holeCount * 2));
            var hole = 0;

            for (var i = 0; i < template.Length; i++)
            {
                var c = template[i];

                if (c is '{' or '}' && i + 1 < template.Length && template[i + 1] == c)
                {
                    // An escaped brace keeps its escape, which composite formatting reads the same way.
                    composite.Append(c).Append(c);
                    i++;

                    continue;
                }

                if (c == '}')
                {
                    return false;
                }

                if (c != '{')
                {
                    composite.Append(c);

                    continue;
                }

                var close = template.IndexOf('}', i + 1);

                if (close < 0 || hole >= holeCount)
                {
                    return false;
                }

                var body = template.AsSpan(i + 1, close - i - 1);
                var delimiter = body.IndexOfAny(',', ':');
                var name = delimiter < 0 ? body : body[..delimiter];

                if (!name.SequenceEqual(values[hole].Key))
                {
                    return false;
                }

                composite.Append('{').Append(hole.ToString(CultureInfo.InvariantCulture));

                if (delimiter >= 0)
                {
                    composite.Append(body[delimiter..]);
                }

                composite.Append('}');
                hole++;
                i = close;
            }

            if (hole != holeCount)
            {
                return false;
            }

            var args = new object?[holeCount];

            for (var h = 0; h < holeCount; h++)
            {
                // Logging renders a null as "(null)" and a collection element by element; anything else goes to the
                // format item, so a format such as `{Birthday:yyyy-MM-dd}` still applies to a value left as is.
                args[h] = values[h].Value switch
                {
                    null => _NullText,
                    IEnumerable items and not string => _RenderLikeLogging(items),
                    var value => value,
                };
            }

            try
            {
                rendered = string.Format(CultureInfo.InvariantCulture, composite.ToString(), args);

                return true;
            }
            catch (FormatException)
            {
                // A format the value's own formatter rejects: the raw rendering, rewritten, is the best left.
                return false;
            }
        }

        // Logging renders a collection element by element and anything else through ToString, with the invariant culture.
        private static string _RenderLikeLogging(object value)
        {
            if (value is IEnumerable items and not string)
            {
                List<string> rendered = [];

                foreach (var item in items)
                {
                    rendered.Add(
                        item is null ? _NullText : Convert.ToString(item, CultureInfo.InvariantCulture) ?? string.Empty
                    );
                }

                return string.Join(", ", rendered);
            }

            return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
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

        // The request lines carry the path a path redactor recognizes and the raw query, whose values are as likely to
        // hold a token or an e-mail as the path; the request scope carries the path only.
        private bool _TryRedactRequest<TState>(
            TState state,
            string pathKey,
            bool query,
            out RequestRedactedState redacted
        )
        {
            redacted = null!;

            if (
                (pathRedactors.Length == 0 && !query)
                || state is not IReadOnlyList<KeyValuePair<string, object?>> values
            )
            {
                return false;
            }

            List<(int Index, string Raw, string Redacted)>? changes = null;

            for (var i = 0; i < values.Count; i++)
            {
                var (key, value) = values[i];

                if (value is not string { Length: > 1 } raw)
                {
                    continue;
                }

                if (raw[0] == '/' && string.Equals(key, pathKey, StringComparison.Ordinal))
                {
                    foreach (var pathRedactor in pathRedactors)
                    {
                        if (pathRedactor.TryRedact(raw, out var path))
                        {
                            (changes ??= []).Add((i, raw, path));

                            break;
                        }
                    }
                }
                else if (
                    query
                    && raw[0] == '?'
                    && string.Equals(key, _LineQueryKey, StringComparison.Ordinal)
                    && _RedactQuery(raw) is var redactedQuery
                    && !ReferenceEquals(redactedQuery, raw)
                )
                {
                    (changes ??= []).Add((i, raw, redactedQuery));
                }
            }

            if (changes is null)
            {
                return false;
            }

            redacted = new RequestRedactedState(values, changes, state.ToString());

            return true;
        }

        // Keeps each parameter's name, which says what the request did, and replaces its value: "?a=1&b" becomes
        // "?a=Redacted&b". Returns the same instance when no parameter has a value.
        private static string _RedactQuery(string query)
        {
            if (!query.Contains('=', StringComparison.Ordinal))
            {
                return query;
            }

            var builder = new StringBuilder(query.Length).Append('?');
            var first = true;

            foreach (var range in query.AsSpan(1).Split('&'))
            {
                var parameter = query.AsSpan(1)[range];

                if (!first)
                {
                    builder.Append('&');
                }

                first = false;
                var equals = parameter.IndexOf('=');

                if (equals < 0 || equals == parameter.Length - 1)
                {
                    builder.Append(parameter);
                }
                else
                {
                    builder.Append(parameter[..(equals + 1)]).Append(_RedactedQueryValue);
                }
            }

            return builder.ToString();
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

    /// <summary>
    /// A copy of a log state with its path or query values redacted, rendering as the original text with each raw value
    /// replaced.
    /// </summary>
    private sealed class RequestRedactedState : IReadOnlyList<KeyValuePair<string, object?>>
    {
        private readonly KeyValuePair<string, object?>[] _values;
        private readonly List<(int Index, string Raw, string Redacted)> _changes;
        private readonly string? _text;

        public RequestRedactedState(
            IReadOnlyList<KeyValuePair<string, object?>> values,
            List<(int Index, string Raw, string Redacted)> changes,
            string? text
        )
        {
            _values = [.. values];

            foreach (var (index, _, redacted) in changes)
            {
                _values[index] = new(_values[index].Key, redacted);
            }

            _changes = changes;
            _text = text is null ? null : Rewrite(text);
        }

        public int Count => _values.Length;

        public KeyValuePair<string, object?> this[int index] => _values[index];

        public string Rewrite(string text)
        {
            foreach (var (_, raw, redacted) in _changes)
            {
                text = text.Replace(raw, redacted, StringComparison.Ordinal);
            }

            return text;
        }

        public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() =>
            ((IEnumerable<KeyValuePair<string, object?>>)_values).GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public override string ToString() => _text ?? string.Empty;
    }
}
