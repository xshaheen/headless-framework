// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;

namespace Headless.Messaging.Internal;

/// <summary>Host/network identity helpers used to stamp a stable owner name on processed messages.</summary>
internal static class HostIdentity
{
    public static string? GetInstanceHostname()
    {
        try
        {
            var hostName = Dns.GetHostName();
            if (hostName.Length <= 50)
            {
                return hostName;
            }

            return hostName[..50];
        }
#pragma warning disable ERP022
        catch
        {
            return null;
        }
#pragma warning restore ERP022
    }

    public static bool IsInnerIp(string ipAddress)
    {
        if (string.IsNullOrWhiteSpace(ipAddress))
        {
            return false;
        }

        // Ensure proper IPv4 format (must have exactly 3 dots)
        var octets = ipAddress.Split('.');
        if (octets.Length != 4)
        {
            return false;
        }

        if (
            !IPAddress.TryParse(ipAddress, out var ip)
            || ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork
        )
        {
            return false;
        }

        //Private IP：
        //category A: 10.0.0.0-10.255.255.255
        //category B: 172.16.0.0-172.31.255.255
        //category C: 192.168.0.0-192.168.255.255

        var bytes = ip.GetAddressBytes();
        var first = bytes[0];
        var second = bytes[1];

        // Class A: 10.0.0.0/8
        if (first == 10)
        {
            return true;
        }

        // Class B: 172.16.0.0/12
        if (first == 172 && second is >= 16 and <= 31)
        {
            return true;
        }

        // Class C: 192.168.0.0/16
        if (first == 192 && second == 168)
        {
            return true;
        }

        return false;
    }
}
