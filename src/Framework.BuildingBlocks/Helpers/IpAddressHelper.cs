using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Framework.BuildingBlocks.Helpers;

public static class IpAddressHelper
{
    public static List<IPAddress> GetInterNetworkIpAddresses()
    {
        var ipAddresses = NetworkInterface
            .GetAllNetworkInterfaces()
            .Where(i =>
                i
                    is {
                        IsReceiveOnly: false,
                        OperationalStatus: OperationalStatus.Up,
                        NetworkInterfaceType: not NetworkInterfaceType.Loopback
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
