// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Framework.Kernel.BuildingBlocks;
using Framework.Kernel.Checks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Net.Http.Headers;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Http;

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
        var forwardedForValues = httpContext.Request.Headers.TryGetValue("X-Forwarded-For", out var obj)
            ? obj
            : default;

        var ipAddress = forwardedForValues.FirstOrDefault();

        // Is not proxy
        if (string.IsNullOrWhiteSpace(ipAddress))
        {
            var ip = httpContext.Connection.RemoteIpAddress;

            return ip is null ? null
                : ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4().ToString()
                : ip.ToString();
        }

        // Is proxy
        var addresses = ipAddress.Split(separator: ',');

        if (addresses.Length == 0)
        {
            return null;
        }

        // If IP contains port, it will be after the last : (IPv6 uses : as delimiter and could have more of them)
        return addresses[0].Contains(value: ':', StringComparison.Ordinal)
            ? addresses[0][..addresses[0].LastIndexOf(value: ':')]
            : addresses[0];
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
