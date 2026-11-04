// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Headless.Blobs;

/// <summary>
/// Wraps the logger ASP.NET Core's hosting layer writes request logs through, so a signed blob URL's token never
/// reaches its request scope (<c>RequestPath</c>) or its "Request starting" and "Request finished" lines.
/// </summary>
/// <remarks>
/// <para>
/// The hosting layer opens the scope and writes both lines before any middleware runs, so no middleware can rewrite
/// them. It takes its logger from the container's <see cref="ILoggerFactory"/> when the server starts, which is the
/// one place the entries can be rewritten before any provider (console, OpenTelemetry, Serilog) sees them.
/// </para>
/// <para>
/// Every other category passes through untouched. The factory owns the one it wraps, which the container does not
/// track once it is decorated.
/// </para>
/// </remarks>
internal sealed class BlobSignedUrlRedactingLoggerFactory(ILoggerFactory inner, BlobSignedUrlPathRedactor redactor)
    : ILoggerFactory
{
    /// <summary>The category ASP.NET Core's hosting layer opens the request scope and writes the request lines under.</summary>
    public const string HostingDiagnosticsCategory = "Microsoft.AspNetCore.Hosting.Diagnostics";

    public ILogger CreateLogger(string categoryName)
    {
        var logger = inner.CreateLogger(categoryName);

        return string.Equals(categoryName, HostingDiagnosticsCategory, StringComparison.Ordinal)
            ? new RedactingLogger(logger, redactor)
            : logger;
    }

    public void AddProvider(ILoggerProvider provider) => inner.AddProvider(provider);

    public void Dispose() => inner.Dispose();

    private sealed class RedactingLogger(ILogger inner, BlobSignedUrlPathRedactor redactor) : ILogger
    {
        // The request scope names the path RequestPath; the request lines name it Path, beside a separate PathBase.
        private const string _ScopePathKey = "RequestPath";
        private const string _LinePathKey = "Path";

        public bool IsEnabled(LogLevel logLevel) => inner.IsEnabled(logLevel);

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return _TryRedact(state, _ScopePathKey, out var redacted)
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
            if (!_TryRedact(state, _LinePathKey, out var redacted))
            {
                inner.Log(logLevel, eventId, state, exception, formatter);

                return;
            }

            // The message is rendered from the original state once, then rewritten, so the line keeps whatever shape
            // the hosting layer gives it; the state's own values carry the redacted path for structured sinks.
            var message = redacted.Rewrite(formatter(state, exception));

            inner.Log(logLevel, eventId, redacted, exception, (_, _) => message);
        }

        private bool _TryRedact<TState>(TState state, string pathKey, out RedactedState redacted)
        {
            redacted = null!;

            if (state is not IReadOnlyList<KeyValuePair<string, object?>> values)
            {
                return false;
            }

            for (var i = 0; i < values.Count; i++)
            {
                var (key, value) = values[i];

                if (
                    string.Equals(key, pathKey, StringComparison.Ordinal)
                    && value is string raw
                    && raw.Length > 0
                    && raw[0] == '/'
                    && redactor.TryRedact(new PathString(raw), out var path)
                )
                {
                    redacted = new RedactedState(values, i, raw, path.ToString(), state.ToString());

                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>A copy of a log state with its path value redacted, rendering as the original text with the path replaced.</summary>
    private sealed class RedactedState : IReadOnlyList<KeyValuePair<string, object?>>
    {
        private readonly KeyValuePair<string, object?>[] _values;
        private readonly string _raw;
        private readonly string _redacted;
        private readonly string? _text;

        public RedactedState(
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

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

        public override string ToString() => _text ?? string.Empty;
    }
}
