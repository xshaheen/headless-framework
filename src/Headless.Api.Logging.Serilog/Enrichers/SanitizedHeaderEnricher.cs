// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Serilog.Core;
using Serilog.Events;

namespace Headless.Logging.Enrichers;

/// <summary>
/// Enricher that adds sanitized HTTP request headers to log events.
/// Protects against log injection attacks by removing control characters,
/// ANSI escape sequences, and truncating to a maximum length.
/// </summary>
/// <param name="contextAccessor">Provides access to the current <see cref="HttpContext"/>.</param>
/// <param name="headerName">The HTTP request header whose value is attached to log events.</param>
/// <param name="propertyName">
/// The Serilog log property name. When <see langword="null"/>, defaults to <paramref name="headerName"/>
/// with dashes removed (e.g. <c>"User-Agent"</c> becomes <c>"UserAgent"</c>).
/// </param>
/// <param name="maxLength">Maximum character length of the sanitized header value. Default is 512.</param>
[PublicAPI]
public sealed partial class SanitizedHeaderEnricher(
    IHttpContextAccessor contextAccessor,
    string headerName,
    string? propertyName = null,
    int maxLength = 512
) : ILogEventEnricher
{
    private readonly string _propertyName = propertyName ?? _SanitizePropertyName(headerName);

    /// <summary>
    /// Per-instance key into <see cref="HttpContext.Items"/>. A pipeline carries one enricher per header,
    /// each with its own header name and length budget, so the memo slots must not collide.
    /// </summary>
    private readonly object _memoKey = new();

    /// <summary>
    /// Reads the configured request header from the current <see cref="HttpContext"/>, sanitizes its value,
    /// and adds it as a Serilog property on <paramref name="logEvent"/> if the header is present and non-empty.
    /// </summary>
    /// <param name="logEvent">The log event to enrich.</param>
    /// <param name="propertyFactory">Factory used to create the Serilog property.</param>
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        var httpContext = contextAccessor.HttpContext;

        if (httpContext is null)
        {
            return;
        }

        // A request emits many log events but its headers are fixed once the pipeline is running, so the
        // sanitized value is resolved on the first event and reused by the rest. A null memo records
        // "header absent or empty" so the miss is not re-probed either.
        if (!httpContext.Items.TryGetValue(_memoKey, out var memoized))
        {
            memoized = _ResolveSanitizedValue(httpContext);
            httpContext.Items[_memoKey] = memoized;
        }

        if (memoized is not string sanitized)
        {
            return;
        }

        var property = propertyFactory.CreateProperty(_propertyName, sanitized);
        logEvent.AddPropertyIfAbsent(property);
    }

    private string? _ResolveSanitizedValue(HttpContext httpContext)
    {
        if (!httpContext.Request.Headers.TryGetValue(headerName, out var headerValue))
        {
            return null;
        }

        var rawValue = headerValue.ToString();

        return string.IsNullOrEmpty(rawValue) ? null : _Sanitize(rawValue, maxLength);
    }

    private static string _Sanitize(string value, int maxLength)
    {
        // Every removal below is anchored on a character in [0x00-0x1F] other than tab: the newline
        // replacements target CR/LF, the ANSI patterns start at ESC (0x1B), and the control-character
        // pattern covers the rest. A value carrying none of them is already sanitized, so the common
        // clean case skips the four scans and hands back the original instance.
        if (!value.AsSpan().ContainsAny(_StrippedCharacters))
        {
            return value.Length > maxLength ? value[..maxLength] : value;
        }

        // Remove newline characters (prevents log line injection)
        var sanitized = value.Replace("\r", "", StringComparison.Ordinal).Replace("\n", "", StringComparison.Ordinal);

        // Remove ANSI escape sequences (prevents terminal manipulation)
        sanitized = AnsiEscapeRegex.Replace(sanitized, "");

        // Remove other control characters (ASCII 0-31 except tab)
        sanitized = ControlCharRegex.Replace(sanitized, "");

        // Truncate to max length
        if (sanitized.Length > maxLength)
        {
            sanitized = sanitized[..maxLength];
        }

        return sanitized;
    }

    private static string _SanitizePropertyName(string headerName)
    {
        // Convert header name to property name (e.g., "User-Agent" -> "UserAgent")
        return headerName.Replace("-", "", StringComparison.Ordinal);
    }

    private static readonly SearchValues<char> _StrippedCharacters = SearchValues.Create(_BuildStrippedCharacters());

    private static char[] _BuildStrippedCharacters()
    {
        // ASCII 0x00-0x1F except tab (0x09), which the sanitizer deliberately preserves.
        var characters = new List<char>(31);

        for (var c = '\0'; c < ' '; c++)
        {
            if (c != '\t')
            {
                characters.Add(c);
            }
        }

        return [.. characters];
    }

    // Matches ANSI escape sequences: ESC[ followed by parameters and a letter
    [GeneratedRegex(@"\x1b\[[0-9;]*[a-zA-Z]|\x1b\].*?\x07", RegexOptions.Compiled, 100)]
    private static partial Regex AnsiEscapeRegex { get; }

    // Matches control characters (0x00-0x1F) except tab (0x09)
    [GeneratedRegex(@"[\x00-\x08\x0b\x0c\x0e-\x1f]", RegexOptions.Compiled, 100)]
    private static partial Regex ControlCharRegex { get; }
}
