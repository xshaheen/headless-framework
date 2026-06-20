// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Headless.Checks;
using Headless.Constants;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Net.Http.Headers;

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

    /// <summary>
    /// Adds <c>Cache-Control: no-cache, no-store, must-revalidate</c>, <c>Pragma: no-cache</c>,
    /// and <c>Expires: -1</c> response headers, and removes any <c>ETag</c> header.
    /// </summary>
    /// <param name="context">The HTTP context whose response headers are modified.</param>
    public static void AddNoCacheHeaders(this HttpContext context)
    {
        var headers = context.Response.Headers;
        headers[HeaderNames.CacheControl] = "no-cache, no-store, must-revalidate";
        headers[HeaderNames.Pragma] = "no-cache";
        headers[HeaderNames.Expires] = "-1";
        headers.Remove(HeaderNames.ETag);
    }

    /// <summary>
    /// Returns the client IP address from <see cref="ConnectionInfo.RemoteIpAddress"/>, which
    /// <c>UseForwardedHeaders</c> (when configured with trusted proxies) already rewrites from
    /// the <c>X-Forwarded-For</c> / <c>X-Real-IP</c> headers. Reading those headers directly
    /// is unsafe because any client can forge them; relying on the rewritten connection address
    /// is the secure default.
    /// </summary>
    public static string? GetIpAddress(this HttpContext httpContext)
    {
        var ip = httpContext.Connection.RemoteIpAddress;

        return ip is null ? null
            : ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4().ToString()
            : ip.ToString();
    }

    /// <summary>Returns the <c>User-Agent</c> request header value, or <see langword="null"/> when absent.</summary>
    /// <param name="httpContext">The current HTTP context.</param>
    public static string? GetUserAgent(this HttpContext httpContext)
    {
        return httpContext.Request.Headers.TryGetValue(HeaderNames.UserAgent, out var value)
            ? value.FirstOrDefault()
            : null;
    }

    /// <summary>
    /// Returns the <c>X-Correlation-ID</c> request header value, or <see langword="null"/> when absent.
    /// </summary>
    /// <param name="httpContext">The current HTTP context.</param>
    public static string? GetCorrelationId(this HttpContext httpContext)
    {
        return httpContext.Request.Headers.TryGetValue(HttpHeaderNames.CorrelationId, out var value)
            ? value.FirstOrDefault()
            : null;
    }

    /// <summary>
    /// Executes an <see cref="IActionResult"/> against the current HTTP context without an MVC
    /// controller, using an empty <see cref="Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor"/>.
    /// </summary>
    /// <param name="httpContext">The current HTTP context.</param>
    /// <param name="result">The action result to execute.</param>
    /// <returns>A task that completes when the result has been written to the response.</returns>
    public static Task ExecuteResultAsync(this HttpContext httpContext, IActionResult result)
    {
        var actionContext = new ActionContext(httpContext, httpContext.GetRouteData(), _EmptyActionDescriptor);

        return result.ExecuteResultAsync(actionContext);
    }
}
