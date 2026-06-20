// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Headless.Network;

/// <summary>Helpers for discovering the machine's active IPv4 network addresses.</summary>
[PublicAPI]
public static class IpAddressHelper
{
    /// <summary>
    /// Returns the IPv4 (<see cref="AddressFamily.InterNetwork"/>) addresses of the machine's operational,
    /// non-loopback network interfaces that have at least one gateway configured. An interface with multiple
    /// unicast IPv4 addresses contributes all of them to the result.
    /// </summary>
    /// <returns>The list of discovered IPv4 addresses (possibly multiple per interface); empty when no qualifying interface is found.</returns>
    public static List<IPAddress> GetInterNetworkIpAddresses()
    {
        var ipAddresses = NetworkInterface
            .GetAllNetworkInterfaces()
            .Where(i =>
                i
                    is {
                        IsReceiveOnly: false,
                        OperationalStatus: OperationalStatus.Up,
                        NetworkInterfaceType: not NetworkInterfaceType.Loopback,
                    }
            )
            .Select(i => (i.Name, i.NetworkInterfaceType, IpProperties: i.GetIPProperties()))
            .Where(i => i.IpProperties.GatewayAddresses.Count > 0)
            .SelectMany(i =>
                i.IpProperties.UnicastAddresses.Where(ip => ip.Address.AddressFamily == AddressFamily.InterNetwork)
                    .Select(ip => ip.Address)
            )
            .ToList();

        return ipAddresses!;
    }
}
