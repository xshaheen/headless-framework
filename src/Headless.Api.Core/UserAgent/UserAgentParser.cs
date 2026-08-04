// Copyright (c) Mahmoud Shaheen. All rights reserved.

using DeviceDetectorNET;
using Headless.Abstractions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Headless.Api.UserAgent;

/// <summary>
/// <see cref="IUserAgentParser"/> backed by DeviceDetector.NET, memoizing results in a bounded in-process cache to
/// amortize the regex work each parse performs.
/// </summary>
/// <remarks>
/// The parser owns this cache rather than registering or consuming the host's shared <c>IMemoryCache</c>: entries are
/// derived from untrusted request headers, must not compete for the application's cache budget, and never need to
/// cross a process boundary. The singleton parser disposes the cache with its own lifetime.
/// </remarks>
internal sealed class UserAgentParser : IUserAgentParser, IDisposable
{
    private readonly MemoryCache _memo;
    private readonly MemoryCacheEntryOptions _entryOptions;
    private readonly int _maxUserAgentLength;
    private readonly Func<string, string?> _parser;

    public UserAgentParser(IOptions<UserAgentParserOptions> options)
        : this(options, _Parse) { }

    internal UserAgentParser(IOptions<UserAgentParserOptions> options, Func<string, string?> parser)
    {
        var value = options.Value;

        _memo = new MemoryCache(new MemoryCacheOptions { SizeLimit = value.MaxEntries });
        _entryOptions = new MemoryCacheEntryOptions()
            .SetSize(1)
            .SetSlidingExpiration(value.SlidingExpiration)
            .SetAbsoluteExpiration(value.Duration);
        _maxUserAgentLength = value.MaxUserAgentLength;
        _parser = parser;
    }

    public string? GetDeviceInfo(string? userAgent)
    {
        if (userAgent.IsNullOrWhiteSpace())
        {
            return null;
        }

        // Cap before parsing and before forming the key so both are bounded.
        var normalized = userAgent.Length > _maxUserAgentLength ? userAgent[.._maxUserAgentLength] : userAgent;

        // Probe before GetOrCreate: the factory lambda captures `normalized` and `this`, so a closure and a
        // delegate are allocated at the call site even when the entry is already cached — the common case.
        if (_memo.TryGetValue<string?>(normalized, out var cached))
        {
            return cached;
        }

        return _memo.GetOrCreate<string?>(normalized, _ => _parser(normalized), _entryOptions);
    }

    public void Dispose() => _memo.Dispose();

    private static string? _Parse(string userAgent)
    {
        // A new DeviceDetector per parse keeps mutable detector state isolated between concurrent callers. The
        // allocation is amortized by the memo above.
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
