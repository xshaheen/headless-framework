// Copyright (c) Mahmoud Shaheen. All rights reserved.

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
[PublicAPI]
public sealed partial class SanitizedHeaderEnricher(
    IHttpContextAccessor contextAccessor,
    string headerName,
    string? propertyName = null,
    int maxLength = 512
) : ILogEventEnricher
{
    private readonly string _propertyName = propertyName ?? _SanitizePropertyName(headerName);

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        var httpContext = contextAccessor.HttpContext;
        if (httpContext is null)
        {
            return;
        }

        if (!httpContext.Request.Headers.TryGetValue(headerName, out var headerValue))
        {
            return;
        }

        var rawValue = headerValue.ToString();
        if (string.IsNullOrEmpty(rawValue))
        {
            return;
        }

        var sanitized = _Sanitize(rawValue, maxLength);
        var property = propertyFactory.CreateProperty(_propertyName, sanitized);
        logEvent.AddPropertyIfAbsent(property);
    }

    private static string _Sanitize(string value, int maxLength)
    {
        // Remove newline characters (prevents log line injection)
        var sanitized = value.Replace("\r", "", StringComparison.Ordinal).Replace("\n", "", StringComparison.Ordinal);

        // Remove ANSI escape sequences (prevents terminal manipulation)
        sanitized = _AnsiEscapeRegex().Replace(sanitized, "");

        // Remove other control characters (ASCII 0-31 except tab)
        sanitized = _ControlCharRegex().Replace(sanitized, "");

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

    // Matches ANSI escape sequences: ESC[ followed by parameters and a letter
    [GeneratedRegex(@"\x1b\[[0-9;]*[a-zA-Z]|\x1b\].*?\x07", RegexOptions.Compiled, 100)]
    private static partial Regex _AnsiEscapeRegex();

    // Matches control characters (0x00-0x1F) except tab (0x09)
    [GeneratedRegex(@"[\x00-\x08\x0b\x0c\x0e-\x1f]", RegexOptions.Compiled, 100)]
    private static partial Regex _ControlCharRegex();
}
