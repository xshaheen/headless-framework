// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Headless.Checks;
using Headless.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Net.Http.Headers;

namespace Microsoft.AspNetCore.Http;

[PublicAPI]
public static class HeadlessHttpContextExtensions
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
    /// <exception cref="ArgumentNullException"><paramref name="context"/> or <paramref name="cacheProfile"/> is <see langword="null"/>.</exception>
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
                var exception = new NotSupportedException(
                    $"Unknown {nameof(ResponseCacheLocation)}: {cacheProfile.Location}"
                );

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
        headers.Remove(HttpHeaderNames.ETag);
    }

    /// <summary>
    /// Returns the client IP address from <see cref="ConnectionInfo.RemoteIpAddress"/>, which
    /// <c>UseForwardedHeaders</c> (when configured with trusted proxies) already rewrites from
    /// the <c>X-Forwarded-For</c> / <c>X-Real-IP</c> headers. Reading those headers directly
    /// is unsafe because any client can forge them; relying on the rewritten connection address
    /// is the secure default.
    /// </summary>
    /// <param name="httpContext">The current HTTP context.</param>
    /// <returns>The IPv4 or IPv6 address string, or <see langword="null"/> when no remote address is available.</returns>
    public static string? GetIpAddress(this HttpContext httpContext)
    {
        var ip = httpContext.Connection.RemoteIpAddress;

        return ip is null ? null
            : ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4().ToString()
            : ip.ToString();
    }

    /// <summary>
    /// Returns a key that groups the client's address for rate limiting: the whole address for IPv4, and the
    /// <paramref name="ipv6PrefixLength"/>-bit network (<c>2001:db8:1:2::/64</c>) for IPv6. One IPv6 host usually owns
    /// a whole /64 and can rotate through it freely, so keying on the full address hands it a fresh budget per request.
    /// </summary>
    /// <param name="httpContext">The current HTTP context.</param>
    /// <param name="ipv6PrefixLength">The IPv6 prefix length, from 0 to 128. Defaults to 64.</param>
    /// <returns>The partition key, or <see langword="null"/> when no remote address is available.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="httpContext"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="ipv6PrefixLength"/> is outside 0 to 128.</exception>
    /// <remarks>
    /// Reads <see cref="ConnectionInfo.RemoteIpAddress"/>, like <see cref="GetIpAddress"/>, so behind a proxy it is
    /// only as trustworthy as the <c>UseForwardedHeaders</c> configuration: with no known proxies or networks, any
    /// client can forge <c>X-Forwarded-For</c> and choose its own partition. IPv4-mapped IPv6 addresses
    /// (<c>::ffff:a.b.c.d</c>, which a dual-stack listener reports for IPv4 clients) are keyed as IPv4; masking them
    /// as IPv6 would put every IPv4 client into the single <c>::/64</c> partition.
    /// </remarks>
    public static string? GetIpAddressPartition(this HttpContext httpContext, int ipv6PrefixLength = 64)
    {
        Argument.IsNotNull(httpContext);
        Argument.IsInclusiveBetween(ipv6PrefixLength, 0, 128);

        var ip = httpContext.Connection.RemoteIpAddress;

        if (ip is null)
        {
            return null;
        }

        if (ip.IsIPv4MappedToIPv6)
        {
            return ip.MapToIPv4().ToString();
        }

        if (ip.AddressFamily != AddressFamily.InterNetworkV6)
        {
            return ip.ToString();
        }

        Span<byte> bytes = stackalloc byte[16];
        ip.TryWriteBytes(bytes, out _);

        var fullBytes = ipv6PrefixLength / 8;
        var remainingBits = ipv6PrefixLength % 8;

        if (remainingBits > 0)
        {
            bytes[fullBytes] &= (byte)(0xFF << (8 - remainingBits));
            fullBytes++;
        }

        bytes[fullBytes..].Clear();

        // Built from bytes, so a link-local scope id (fe80::1%eth0) does not split one network into many keys.
        return string.Create(CultureInfo.InvariantCulture, $"{new IPAddress(bytes)}/{ipv6PrefixLength}");
    }

    /// <summary>Returns the <c>User-Agent</c> request header value, or <see langword="null"/> when absent.</summary>
    /// <param name="httpContext">The current HTTP context.</param>
    /// <returns>The raw <c>User-Agent</c> header value, or <see langword="null"/> when the header is missing.</returns>
    public static string? GetUserAgent(this HttpContext httpContext)
    {
        // Indexer instead of FirstOrDefault(): the LINQ path boxes the StringValues struct.
        return httpContext.Request.Headers.TryGetValue(HttpHeaderNames.UserAgent, out var value) && value.Count > 0
            ? value[0]
            : null;
    }

    /// <summary>
    /// Returns the <c>X-Correlation-ID</c> request header value, or <see langword="null"/> when absent.
    /// </summary>
    /// <param name="httpContext">The current HTTP context.</param>
    /// <returns>The raw <c>X-Correlation-ID</c> header value, or <see langword="null"/> when the header is missing.</returns>
    public static string? GetCorrelationId(this HttpContext httpContext)
    {
        // Indexer instead of FirstOrDefault(): the LINQ path boxes the StringValues struct.
        return httpContext.Request.Headers.TryGetValue(HttpHeaderNames.CorrelationId, out var value) && value.Count > 0
            ? value[0]
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
