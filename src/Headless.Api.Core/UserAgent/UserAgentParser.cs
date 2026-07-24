// Copyright (c) Mahmoud Shaheen. All rights reserved.

using DeviceDetectorNET;
using Headless.Abstractions;
using Headless.Caching;
using Microsoft.Extensions.Options;

namespace Headless.Api.UserAgent;

/// <summary>
/// <see cref="IUserAgentParser"/> backed by DeviceDetector.NET. Memoizes results in the host's registered
/// <see cref="ICache"/> — under the feature-namespaced <c>api:user-agent:</c> key prefix — to amortise the regex
/// work each parse performs.
/// </summary>
/// <remarks>
/// The cache is resolved <b>optionally</b>: when the host has registered a <c>Headless.Caching</c> provider, parses
/// are memoized (and negatives — unidentifiable agents — are cached too, so garbage is parsed at most once per TTL);
/// when it has not, every call parses directly. This is the framework's standard optional-cache contract
/// (<c>Headless.Jobs.EntityFramework</c>'s cron-expression cache is the reference implementation), so the dashboard
/// UI or any consumer that wants User-Agent parses cached simply registers a cache. It never registers the shared
/// <c>IMemoryCache</c> and never owns a private cache.
/// </remarks>
internal sealed class UserAgentParser : IUserAgentParser
{
    private const string _KeyPrefix = "api:user-agent:";

    private readonly ICache? _cache;
    private readonly CacheEntryOptions _entryOptions;
    private readonly int _maxUserAgentLength;

    public UserAgentParser(IOptions<UserAgentParserOptions> options, ICache? cache = null)
    {
        var value = options.Value;

        _cache = cache;
        _entryOptions = new CacheEntryOptions
        {
            Duration = value.Duration,
            SlidingExpiration = value.SlidingExpiration,
        };
        _maxUserAgentLength = value.MaxUserAgentLength;
    }

    public async ValueTask<string?> GetDeviceInfoAsync(string? userAgent, CancellationToken cancellationToken = default)
    {
        if (userAgent.IsNullOrWhiteSpace())
        {
            return null;
        }

        // Cap before parsing and before forming the key so both are bounded.
        var normalized = userAgent.Length > _maxUserAgentLength ? userAgent[.._maxUserAgentLength] : userAgent;

        if (_cache is null)
        {
            return _Parse(normalized);
        }

        var result = await _cache
            .GetOrAddAsync<string?>(
                _KeyPrefix + normalized,
                _ => new ValueTask<string?>(_Parse(normalized)),
                _entryOptions,
                cancellationToken
            )
            .ConfigureAwait(false);

        // GetOrAddAsync writes on a miss, so a hit is expected; the direct-parse fallback only guards the
        // theoretical NoValue read (mirrors the Jobs cron-cache defensive read).
        return result.HasValue ? result.Value : _Parse(normalized);
    }

    private static string? _Parse(string userAgent)
    {
        // A new DeviceDetector per parse rather than a [ThreadStatic] instance: [ThreadStatic] is unsafe across
        // async continuations, where a method can resume on a different pool thread and read stale state. The
        // allocation is amortised by the memo above.
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
