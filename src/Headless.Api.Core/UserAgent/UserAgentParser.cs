// Copyright (c) Mahmoud Shaheen. All rights reserved.

using DeviceDetectorNET;
using Headless.Abstractions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Headless.Api.UserAgent;

/// <summary>
/// <see cref="IUserAgentParser"/> backed by DeviceDetector.NET, memoizing results in a bounded
/// sliding-expiry in-process cache to amortise the regex work each parse performs.
/// </summary>
/// <remarks>
/// The memo is a private <see cref="MemoryCache"/> owned by this instance, not the host's shared
/// <c>IMemoryCache</c>: entries here are derived from a header value and must not compete for the
/// application's cache budget. It is deliberately not a <c>Headless.Caching</c> <c>ICache</c> either —
/// <see cref="GetDeviceInfo"/> is called from a synchronous property on <c>IWebClientInfoProvider</c>,
/// and what it caches is local CPU work, so a remote lookup to avoid a regex would cost more than it saves.
/// </remarks>
internal sealed class UserAgentParser : IUserAgentParser, IDisposable
{
    private readonly MemoryCache _memo;
    private readonly MemoryCacheEntryOptions _entryOptions;
    private readonly int _maxUserAgentLength;

    public UserAgentParser(IOptions<UserAgentParserOptions> options)
    {
        var value = options.Value;

        // SizeLimit counts "size units"; every entry below is registered with a size of 1, so the
        // limit is a straight entry count.
        _memo = new MemoryCache(new MemoryCacheOptions { SizeLimit = value.MaxEntries });
        _entryOptions = new MemoryCacheEntryOptions().SetSize(1).SetSlidingExpiration(value.SlidingExpiration);
        _maxUserAgentLength = value.MaxUserAgentLength;
    }

    public string? GetDeviceInfo(string? userAgent)
    {
        if (userAgent.IsNullOrWhiteSpace())
        {
            return null;
        }

        // Cap before lookup so the memo key is bounded too.
        if (userAgent.Length > _maxUserAgentLength)
        {
            userAgent = userAgent[.._maxUserAgentLength];
        }

        if (_memo.TryGetValue(userAgent, out string? cached))
        {
            return cached;
        }

        var result = _Parse(userAgent);
        _memo.Set(userAgent, result, _entryOptions);

        return result;
    }

    public void Dispose() => _memo.Dispose();

    private static string? _Parse(string userAgent)
    {
        // A new DeviceDetector per parse rather than a [ThreadStatic] instance: [ThreadStatic] is unsafe
        // across async continuations, where a method can resume on a different pool thread and read stale
        // state. The allocation is amortised by the memo above.
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
