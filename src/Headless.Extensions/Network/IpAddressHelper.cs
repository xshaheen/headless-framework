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
    /// non-loopback network interfaces that have at least one gateway configured.
    /// </summary>
    /// <returns>The list of discovered IPv4 addresses; empty when no qualifying interface is found.</returns>
    /// <exception cref="InvalidOperationException">Thrown when a qualifying interface exposes more than one IPv4 unicast address.</exception>
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
            .Select(i =>
                i.IpProperties.UnicastAddresses.SingleOrDefault(ip =>
                    ip.Address.AddressFamily == AddressFamily.InterNetwork
                )?.Address
            )
            .Where(i => i is not null)
            .ToList();

        return ipAddresses!;
    }
}
