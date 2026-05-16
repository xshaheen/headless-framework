// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using DeviceDetectorNET;

namespace Headless.Api.Extensions.Web;

[PublicAPI]
public static class WebHelper
{
    private const int _MaxCacheSize = 1000;

    private static readonly ConcurrentDictionary<string, string?> _DeviceInfoCache = new(StringComparer.Ordinal);

    [ThreadStatic]
    private static DeviceDetector? _detector;

    public static string? GetDeviceInfo(string? userAgent)
    {
        if (userAgent.IsNullOrWhiteSpace())
        {
            return null;
        }

        if (_DeviceInfoCache.TryGetValue(userAgent, out var cached))
        {
            return cached;
        }

        var result = _ParseDeviceInfo(userAgent);

        if (_DeviceInfoCache.Count >= _MaxCacheSize)
        {
            _DeviceInfoCache.Clear();
        }

        _DeviceInfoCache.TryAdd(userAgent, result);

        return result;
    }

    private static string? _ParseDeviceInfo(string userAgent)
    {
        _detector ??= new DeviceDetector();
        _detector.SetUserAgent(userAgent);
        _detector.Parse();

        var deviceDetector = _detector;

        if (!deviceDetector.IsParsed())
        {
            return null;
        }

        string? deviceInfo = null;

        var osInfo = deviceDetector.GetOs();

        if (osInfo.Success)
        {
            deviceInfo = osInfo.Match.Name;
        }

        var clientInfo = deviceDetector.GetClient();

        if (clientInfo.Success)
        {
            deviceInfo = deviceInfo.IsNullOrWhiteSpace()
                ? clientInfo.Match.Name
                : deviceInfo + " " + clientInfo.Match.Name;
        }

        return deviceInfo;
    }
}
