// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using DeviceDetectorNET;

namespace Framework.Api.Extensions.Web;

public static class WebHelper
{
    public static string? GetDeviceInfo(string? userAgent)
    {
        if (userAgent.IsNullOrWhiteSpace())
        {
            return null;
        }

        var deviceDetector = new DeviceDetector(userAgent);

        string? deviceInfo = null;

        deviceDetector.Parse();

        if (!deviceDetector.IsParsed())
        {
            return deviceInfo;
        }

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
