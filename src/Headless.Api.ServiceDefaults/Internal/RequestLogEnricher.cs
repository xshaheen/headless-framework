// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers;
using System.Text.RegularExpressions;
using Headless.Api;
using Headless.Http;
using Headless.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Enrichment;

namespace Headless.Api.ServiceDefaults.Internal;

/// <summary>
/// Adds the current request's user, account, correlation ID, client IP, and selected request headers to every entry
/// written inside that request, on every provider.
/// </summary>
/// <remarks>
/// The client IP and headers do not change during a request, so they are read and sanitized once and cached on the
/// request. The user and account are read on each entry, because authentication runs after the first entries of a
/// request are written. Values pass through the classified property names like any enrichment value.
/// </remarks>
internal sealed partial class RequestLogEnricher(IHttpContextAccessor contextAccessor, HeadlessLoggingOptions options)
    : ILogEnricher
{
    internal const string UserId = "UserId";
    internal const string AccountId = "AccountId";
    internal const string CorrelationId = "CorrelationId";
    internal const string ClientIp = "ClientIp";

    // Property names are the header name without dashes, so they are valid identifiers in every log query language.
    private static readonly (string Header, string Property)[] _Headers =
    [
        (HttpHeaderNames.UserAgent, "UserAgent"),
        (HttpHeaderNames.ClientVersion, "XClientVersion"),
        (HttpHeaderNames.ApiVersion, "ApiVersion"),
    ];

    private static readonly object _CacheKey = new();

    public void Enrich(IEnrichmentTagCollector collector)
    {
        if (contextAccessor.HttpContext is not { } context)
        {
            return;
        }

        var requestContext = context.RequestServices?.GetService<IRequestContext>();

        if (requestContext is not null)
        {
            if (requestContext.User.UserId is { } userId)
            {
                collector.Add(UserId, userId.ToString());
            }

            if (requestContext.User.AccountId is { } accountId)
            {
                collector.Add(AccountId, accountId.ToString());
            }

            if (requestContext.CorrelationId is { } correlationId)
            {
                collector.Add(CorrelationId, correlationId);
            }
        }

        foreach (var (name, value) in _Stable(context, requestContext))
        {
            collector.Add(name, value);
        }
    }

    private KeyValuePair<string, string>[] _Stable(HttpContext context, IRequestContext? requestContext)
    {
        if (context.Items.TryGetValue(_CacheKey, out var cached) && cached is KeyValuePair<string, string>[] values)
        {
            return values;
        }

        List<KeyValuePair<string, string>> stable = [];

        if (requestContext?.WebClient.IpAddress is { Length: > 0 } ip)
        {
            stable.Add(new(ClientIp, ip));
        }

        foreach (var (header, property) in _Headers)
        {
            if (context.Request.Headers.TryGetValue(header, out var raw) && raw.ToString() is { Length: > 0 } text)
            {
                stable.Add(new(property, Sanitize(text, options.MaxHeaderLength)));
            }
        }

        var result = stable.ToArray();
        context.Items[_CacheKey] = result;

        return result;
    }

    /// <summary>Strips line breaks, ANSI escape sequences, and other control characters, then truncates.</summary>
    internal static string Sanitize(string value, int maxLength)
    {
        // Every removal is anchored on a character in [0x00-0x1F] other than tab: line breaks, the ESC that starts an
        // ANSI sequence, and the rest of the control range. A value with none of them is returned as is.
        if (!value.AsSpan().ContainsAny(_StrippedCharacters))
        {
            return value.Length > maxLength ? value[..maxLength] : value;
        }

        var sanitized = value.Replace("\r", "", StringComparison.Ordinal).Replace("\n", "", StringComparison.Ordinal);
        sanitized = _AnsiEscapeRegex.Replace(sanitized, "");
        sanitized = _ControlCharRegex.Replace(sanitized, "");

        return sanitized.Length > maxLength ? sanitized[..maxLength] : sanitized;
    }

    private static readonly SearchValues<char> _StrippedCharacters = SearchValues.Create([
        .. Enumerable.Range(0, 0x20).Where(static c => c != '\t').Select(static c => (char)c),
    ]);

    [GeneratedRegex(@"\x1b\[[0-9;]*[a-zA-Z]|\x1b\].*?\x07", RegexOptions.None, 100)]
    private static partial Regex _AnsiEscapeRegex { get; }

    [GeneratedRegex(@"[\x00-\x08\x0b\x0c\x0e-\x1f]", RegexOptions.None, 100)]
    private static partial Regex _ControlCharRegex { get; }
}
