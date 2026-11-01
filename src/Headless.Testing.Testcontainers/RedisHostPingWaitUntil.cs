// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net.Sockets;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using Testcontainers.Redis;

namespace Headless.Testing.Testcontainers;

/// <summary>
/// Ready once Redis answers <c>PING</c> through the host-mapped port. The stock check runs <c>redis-cli ping</c> inside
/// the container, which passes before a runtime such as OrbStack or Docker Desktop forwards the port: a client that
/// connects right after the start then has its connection refused or reset by the port proxy.
/// </summary>
internal sealed class RedisHostPingWaitUntil : IWaitUntil
{
    private static readonly TimeSpan _AttemptTimeout = TimeSpan.FromSeconds(1);

    public async Task<bool> UntilAsync(IContainer container)
    {
        return await PingAsync(container.Hostname, container.GetMappedPublicPort(RedisBuilder.RedisPort))
            .ConfigureAwait(false);
    }

    internal static async Task<bool> PingAsync(string host, int port)
    {
        using var timeout = new CancellationTokenSource(_AttemptTimeout);

        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(host, port, timeout.Token).ConfigureAwait(false);

            var stream = client.GetStream();
            await stream.WriteAsync("PING\r\n"u8.ToArray(), timeout.Token).ConfigureAwait(false);

            var expected = "+PONG\r\n"u8.ToArray();
            var reply = new byte[expected.Length];
            var received = 0;

            // A proxy that accepted the connection but cannot reach the server yet closes it: the read returns 0.
            while (received < reply.Length)
            {
                var read = await stream.ReadAsync(reply.AsMemory(received), timeout.Token).ConfigureAwait(false);

                if (read == 0)
                {
                    return false;
                }

                received += read;
            }

            return reply.AsSpan().SequenceEqual(expected);
        }
        catch (Exception e) when (e is SocketException or IOException or OperationCanceledException)
        {
            // Not reachable yet; Testcontainers polls again until the container's start timeout.
            return false;
        }
    }
}
