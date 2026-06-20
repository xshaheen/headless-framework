// Copyright (c) Mahmoud Shaheen. All rights reserved.

using DeviceDetectorNET;
using Microsoft.Extensions.Caching.Memory;

namespace Headless.Api.Extensions.Web;

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
