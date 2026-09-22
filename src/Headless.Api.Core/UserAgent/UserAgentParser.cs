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
    private readonly Func<string, UserAgentInfo?> _parser;

    public UserAgentParser(IOptions<UserAgentParserOptions> options)
        : this(options, _Parse) { }

    internal UserAgentParser(IOptions<UserAgentParserOptions> options, Func<string, UserAgentInfo?> parser)
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

    public UserAgentInfo? Parse(string? userAgent)
    {
        if (userAgent.IsNullOrWhiteSpace())
        {
            return null;
        }

        // Cap before parsing and before forming the key so both are bounded.
        var normalized = userAgent.Length > _maxUserAgentLength ? userAgent[.._maxUserAgentLength] : userAgent;

        // Probe before GetOrCreate: the factory lambda captures `normalized` and `this`, so a closure and a
        // delegate are allocated at the call site even when the entry is already cached — the common case.
        if (_memo.TryGetValue<UserAgentInfo?>(normalized, out var cached))
        {
            return cached;
        }

        return _memo.GetOrCreate<UserAgentInfo?>(normalized, _ => _parser(normalized), _entryOptions);
    }

    // Not the interface's default implementation: that would re-probe the memo through a second virtual call for
    // what is one dictionary lookup here.
    public string? GetDeviceInfo(string? userAgent) => Parse(userAgent)?.Summary;

    public void Dispose() => _memo.Dispose();

    private static UserAgentInfo? _Parse(string userAgent)
    {
        // A new DeviceDetector per parse keeps mutable detector state isolated between concurrent callers. The
        // allocation is amortized by the memo above.
        var detector = new DeviceDetector(userAgent);
        detector.Parse();

        if (!detector.IsParsed())
        {
            return null;
        }

        var isBot = detector.IsBot();
        var os = detector.GetOs();
        var client = detector.GetClient();
        var bot = isBot ? detector.GetBot() : null;
        var device = isBot ? DeviceType.Bot : _MapDevice(detector.GetDeviceName());
        var brand = _Clean(detector.GetBrandName());
        var model = _Clean(detector.GetModel());

        // IsParsed only says the detector ran, not that it recognised anything: it is true for arbitrary junk,
        // which would otherwise yield a record whose every field is null. Report that as "not identified".
        if (!isBot && device is DeviceType.Unknown && !os.Success && !client.Success && brand is null && model is null)
        {
            return null;
        }

        return new UserAgentInfo
        {
            UserAgent = userAgent,
            IsBot = isBot,
            Device = device,
            DeviceBrand = brand,
            DeviceModel = model,
            OsName = os.Success ? _Clean(os.Match?.Name) : null,
            OsVersion = os.Success ? _Clean(os.Match?.Version) : null,
            OsPlatform = os.Success ? _Clean(os.Match?.Platform) : null,
            ClientName = client.Success ? _Clean(client.Match?.Name) : null,
            ClientVersion = client.Success ? _Clean(client.Match?.Version) : null,
            ClientType = client.Success ? _Clean(client.Match?.Type) : null,
            ClientEngine = client.Success
                ? _Clean((client.Match as DeviceDetectorNET.Results.Client.BrowserMatchResult)?.Engine)
                : null,
            BotName = _Clean(bot?.Match?.Name),
            BotCategory = _Clean(bot?.Match?.Category),
        };
    }

    // The detector returns "" rather than null for an unidentified field; the contract says null.
    private static string? _Clean(string? value) => value.IsNullOrWhiteSpace() ? null : value;

    private static DeviceType _MapDevice(string? deviceName)
    {
        return deviceName?.ToLowerInvariant() switch
        {
            "desktop" => DeviceType.Desktop,
            "smartphone" or "feature phone" => DeviceType.Phone,
            "tablet" or "phablet" => DeviceType.Tablet,
            "console" => DeviceType.Console,
            "tv" => DeviceType.Tv,
            "car browser" => DeviceType.CarBrowser,
            "smart display" => DeviceType.SmartDisplay,
            "smart speaker" => DeviceType.SmartSpeaker,
            "wearable" => DeviceType.Wearable,
            "camera" => DeviceType.Camera,
            "portable media player" => DeviceType.PortableMediaPlayer,
            _ => DeviceType.Unknown,
        };
    }
}
