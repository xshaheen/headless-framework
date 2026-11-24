// Copyright (c) Mahmoud Shaheen. All rights reserved.

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
/// The headers do not change during a request, so they are read and sanitized once and cached on the request. The
/// user, account, correlation ID, and client IP are read on each entry: authentication sets the user after the first
/// entries of a request are written, and the forwarded-headers middleware rewrites the remote address after the
/// "Request starting" entry. The correlation ID and headers are client-supplied, so they pass through
/// <see cref="LogSanitizer"/> and are cut to <see cref="HeadlessLoggingOptions.MaxHeaderLength"/> characters. Values
/// pass through the classified property names like any enrichment value.
/// </remarks>
internal sealed class RequestLogEnricher(IHttpContextAccessor contextAccessor, HeadlessLoggingOptions options)
    : ILogEnricher
{
    internal const string UserId = "UserId";
    internal const string AccountId = "AccountId";
    internal const string CorrelationId = "CorrelationId";
    internal const string ClientIp = "ClientIp";

    // Property names are the header name without dashes, so they are valid identifiers in every log query language.
    private static readonly (string Header, string Property)[] _LoggedHeaders =
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

            if (requestContext.CorrelationId is { Length: > 0 } correlationId)
            {
                collector.Add(CorrelationId, _Sanitize(correlationId));
            }

            if (requestContext.WebClient.IpAddress is { Length: > 0 } ip)
            {
                collector.Add(ClientIp, ip);
            }
        }

        foreach (var (name, value) in _Headers(context))
        {
            collector.Add(name, value);
        }
    }

    private KeyValuePair<string, string>[] _Headers(HttpContext context)
    {
        if (context.Items.TryGetValue(_CacheKey, out var cached) && cached is KeyValuePair<string, string>[] values)
        {
            return values;
        }

        List<KeyValuePair<string, string>> headers = [];

        foreach (var (header, property) in _LoggedHeaders)
        {
            if (context.Request.Headers.TryGetValue(header, out var raw) && raw.ToString() is { Length: > 0 } text)
            {
                headers.Add(new(property, _Sanitize(text)));
            }
        }

        var result = headers.ToArray();
        context.Items[_CacheKey] = result;

        return result;
    }

    private string _Sanitize(string value)
    {
        // Sanitize first, then cut with no suffix: LogSanitizer's own limit appends "...", which would make a field
        // longer than a MaxHeaderLength below 3. It strips every surrogate, so the cut cannot split a pair.
        var sanitized = LogSanitizer.Sanitize(value);
        var maxLength = options.MaxHeaderLength;

        return sanitized.Length > maxLength ? sanitized[..maxLength] : sanitized;
    }
}
