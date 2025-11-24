// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Net;
using Framework.Checks;
using Framework.Constants;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Net.Http.Headers;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Http;

[PublicAPI]
public static class HttpContextExtensions
{
    private static readonly ActionDescriptor _EmptyActionDescriptor = new();
    private const string _NoCache = "no-cache";
    private const string _NoCacheMaxAge = "no-cache,max-age=";
    private const string _NoStore = "no-store";
    private const string _NoStoreNoCache = "no-store,no-cache";
    private const string _PublicMaxAge = "public,max-age=";
    private const string _PrivateMaxAge = "private,max-age=";

    /// <summary>Adds the Cache-Control and Pragma HTTP headers by applying the specified cache profile to the HTTP context.</summary>
    /// <param name="context">The HTTP context.</param>
    /// <param name="cacheProfile">The cache profile.</param>
    /// <returns>The same HTTP context.</returns>
    /// <exception cref="ArgumentNullException">context or cacheProfile.</exception>
    public static HttpContext ApplyCacheProfile(this HttpContext context, CacheProfile cacheProfile)
    {
        Argument.IsNotNull(context);
        Argument.IsNotNull(cacheProfile);

        var headers = context.Response.Headers;

        if (!string.IsNullOrEmpty(cacheProfile.VaryByHeader))
        {
            headers.Vary = cacheProfile.VaryByHeader;
        }

        if (cacheProfile.NoStore == true)
        {
            // Cache-control: no-store, no-cache is valid.
            if (cacheProfile.Location == ResponseCacheLocation.None)
            {
                headers.CacheControl = _NoStoreNoCache;
                headers.Pragma = _NoCache;
            }
            else
            {
                headers.CacheControl = _NoStore;
            }

            return context;
        }

        var duration = cacheProfile.Duration.GetValueOrDefault().ToString(CultureInfo.InvariantCulture);
        string cacheControlValue;

        switch (cacheProfile.Location)
        {
            case ResponseCacheLocation.Any:
                cacheControlValue = _PublicMaxAge + duration;

                break;
            case ResponseCacheLocation.Client:
                cacheControlValue = _PrivateMaxAge + duration;

                break;
            case ResponseCacheLocation.None:
                cacheControlValue = _NoCacheMaxAge + duration;
                headers.Pragma = _NoCache;

                break;
            default:
                var message = string.Create(
                    CultureInfo.InvariantCulture,
                    $"Unknown {nameof(ResponseCacheLocation)}: {cacheProfile.Location}"
                );
                var exception = new NotSupportedException(message);
                Debug.Fail(exception.ToString());

                throw exception;
        }

        headers.CacheControl = cacheControlValue;

        return context;
    }

    public static void AddNoCacheHeaders(this HttpContext context)
    {
        var headers = context.Response.Headers;
        headers[HeaderNames.CacheControl] = "no-cache, no-store, must-revalidate";
        headers[HeaderNames.Pragma] = "no-cache";
        headers[HeaderNames.Expires] = "-1";
        headers.Remove(HeaderNames.ETag);
    }

    public static string? GetIpAddress(this HttpContext httpContext)
    {
        // Check X-Forwarded-For header (standard for proxies/load balancers)
        var forwardedFor = httpContext.Request.Headers.TryGetValue("X-Forwarded-For", out var obj)
            ? obj.FirstOrDefault()
            : null;

        if (!string.IsNullOrWhiteSpace(forwardedFor))
        {
            // X-Forwarded-For can contain multiple IPs, take the first (original client)
            var ips = forwardedFor.Split(',', StringSplitOptions.RemoveEmptyEntries);

            if (ips.Length > 0 && IPAddress.TryParse(ips[0].Trim(), out var parsedIp))
            {
                return parsedIp.IsIPv4MappedToIPv6 ? parsedIp.MapToIPv4().ToString() : parsedIp.ToString();
            }
        }

        // Check X-Real-IP header (nginx)
        var realIp = httpContext.Request.Headers.TryGetValue("X-Real-IP", out var realIpObj)
            ? realIpObj.FirstOrDefault()
            : null;

        if (!string.IsNullOrWhiteSpace(realIp) && IPAddress.TryParse(realIp, out var parsedRealIp))
        {
            return parsedRealIp.IsIPv4MappedToIPv6 ? parsedRealIp.MapToIPv4().ToString() : parsedRealIp.ToString();
        }

        // Fallback to connection remote IP

        var ip = httpContext.Connection.RemoteIpAddress;

        return ip is null ? null
            : ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4().ToString()
            : ip.ToString();
    }

    public static string? GetUserAgent(this HttpContext httpContext)
    {
        var values = httpContext.Request.Headers._GetOrDefault(HeaderNames.UserAgent);

        foreach (var value in values)
        {
            return value;
        }

        return null;
    }

    public static string? GetCorrelationId(this HttpContext httpContext)
    {
        var values = httpContext.Request.Headers._GetOrDefault(HttpHeaderNames.CorrelationId);

        foreach (var value in values)
        {
            return value;
        }

        return null;
    }

    public static Task ExecuteResultAsync(this HttpContext httpContext, IActionResult result)
    {
        var actionContext = new ActionContext(httpContext, httpContext.GetRouteData(), _EmptyActionDescriptor);

        return result.ExecuteResultAsync(actionContext);
    }

    private static TValue? _GetOrDefault<TKey, TValue>(this IDictionary<TKey, TValue> dictionary, TKey key)
        where TKey : notnull
    {
        return dictionary.TryGetValue(key, out var obj) ? obj : default;
    }
}
