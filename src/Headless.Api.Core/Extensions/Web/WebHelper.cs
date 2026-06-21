// Copyright (c) Mahmoud Shaheen. All rights reserved.

using DeviceDetectorNET;
using Microsoft.Extensions.Caching.Memory;

namespace Headless.Api.Extensions.Web;

/// <summary>
/// Utility methods for parsing and caching HTTP client information from User-Agent strings.
/// </summary>
[PublicAPI]
public static class WebHelper
{
    // 512 characters is generous for real-world User-Agent strings; anything longer is
    // likely abuse or garbage. DeviceDetector.NET parses the entire string, so we cap
    // length before handing it off to avoid unbounded regex work.
    private const int _MaxUserAgentLength = 512;

    // Bounded, sliding-expiry cache. SizeLimit is in "size units" where each entry is 1.
    // 1 000 entries covers a typical fleet of distinct UA strings without unbounded growth.
    // Sliding expiry evicts stale entries from rare or rotated agents, keeping the working
    // set fresh without a manual Clear() thundering-herd problem.
    private static readonly MemoryCache _DeviceInfoCache = new(new MemoryCacheOptions { SizeLimit = 1000 });

    private static readonly MemoryCacheEntryOptions _CacheEntryOptions = new MemoryCacheEntryOptions()
        .SetSize(1)
        .SetSlidingExpiration(TimeSpan.FromHours(6));

    /// <summary>
    /// Parses the operating system and browser/client name from a User-Agent string.
    /// Results are cached in a bounded sliding-expiry in-process cache (1 000 entries, 6-hour
    /// expiry) to amortise the regex work performed by DeviceDetector.NET.
    /// User-Agent strings longer than 512 characters are truncated before parsing.
    /// </summary>
    /// <param name="userAgent">The raw User-Agent header value.</param>
    /// <returns>
    /// A human-readable string combining OS name and client name (e.g. <c>"Windows Chrome"</c>),
    /// or <see langword="null"/> when <paramref name="userAgent"/> is blank or the parser cannot
    /// identify the device.
    /// </returns>
    public static string? GetDeviceInfo(string? userAgent)
    {
        if (userAgent.IsNullOrWhiteSpace())
        {
            return null;
        }

        // Cap before lookup so the cache key is also bounded.
        if (userAgent.Length > _MaxUserAgentLength)
        {
            userAgent = userAgent[.._MaxUserAgentLength];
        }

        if (_DeviceInfoCache.TryGetValue(userAgent, out string? cached))
        {
            return cached;
        }

        var result = _ParseDeviceInfo(userAgent);
        _DeviceInfoCache.Set(userAgent, result, _CacheEntryOptions);

        return result;
    }

    private static string? _ParseDeviceInfo(string userAgent)
    {
        // Allocate a new DeviceDetector per parse rather than reusing a [ThreadStatic]
        // instance. [ThreadStatic] is unsafe across async continuations: an async method
        // can resume on a different thread pool thread, leaving stale state behind. The
        // parse cost is amortised by the cache above.
        var detector = new DeviceDetector(userAgent);
        detector.Parse();

        if (!detector.IsParsed())
        {
            return null;
        }

        string? deviceInfo = null;

        var osInfo = detector.GetOs();

        if (osInfo.Success)
        {
            deviceInfo = osInfo.Match.Name;
        }

        var clientInfo = detector.GetClient();

        if (clientInfo.Success)
        {
            deviceInfo = deviceInfo.IsNullOrWhiteSpace()
                ? clientInfo.Match.Name
                : deviceInfo + " " + clientInfo.Match.Name;
        }

        return deviceInfo;
    }
}
