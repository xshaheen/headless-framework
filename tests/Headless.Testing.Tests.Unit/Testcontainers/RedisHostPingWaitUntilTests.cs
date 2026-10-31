// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using System.Net.Sockets;
using System.Text;
using Headless.Testing.Testcontainers;
using Headless.Testing.Tests;

namespace Tests.Testcontainers;

public sealed class RedisHostPingWaitUntilTests : TestBase
{
    [Fact]
    public async Task should_not_be_ready_when_nothing_listens_on_the_port()
    {
        // given - a port that was free a moment ago
        int port;

        using (var listener = new TcpListener(IPAddress.Loopback, 0))
        {
            listener.Start();
            port = ((IPEndPoint)listener.LocalEndpoint).Port;
        }

        // when
        var ready = await RedisHostPingWaitUntil.PingAsync("127.0.0.1", port);

        // then
        ready.Should().BeFalse();
    }

    [Fact]
    public async Task should_not_be_ready_when_a_port_proxy_accepts_and_closes_the_connection()
    {
        // given - what a container runtime's port proxy does before it can reach the server
        // when
        var (ready, _) = await _PingServerAsync(reply: null);

        // then
        ready.Should().BeFalse();
    }

    [Fact]
    public async Task should_not_be_ready_when_the_server_answers_with_an_error()
    {
        // given - Redis still loading its dataset
        // when
        var (ready, _) = await _PingServerAsync(reply: "-LOADING Redis is loading the dataset in memory\r\n");

        // then
        ready.Should().BeFalse();
    }

    [Fact]
    public async Task should_be_ready_when_the_server_answers_pong()
    {
        // when
        var (ready, received) = await _PingServerAsync(reply: "+PONG\r\n");

        // then
        ready.Should().BeTrue();
        received.Should().Be("PING\r\n");
    }

    /// <summary>Pings a one-connection server that sends <paramref name="reply" />, or closes at once when it is null.</summary>
    private static async Task<(bool Ready, string Received)> _PingServerAsync(string? reply)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        try
        {
            var server = _ServeOnceAsync(listener, reply);
            var ready = await RedisHostPingWaitUntil.PingAsync("127.0.0.1", ((IPEndPoint)listener.LocalEndpoint).Port);

            return (ready, await server);
        }
        finally
        {
            listener.Dispose();
        }
    }

    private static async Task<string> _ServeOnceAsync(TcpListener listener, string? reply)
    {
        using var client = await listener.AcceptTcpClientAsync(AbortToken);

        if (reply is null)
        {
            return string.Empty;
        }

        var stream = client.GetStream();
        var buffer = new byte[64];
        var read = await stream.ReadAsync(buffer, AbortToken);
        await stream.WriteAsync(Encoding.ASCII.GetBytes(reply), AbortToken);

        return Encoding.ASCII.GetString(buffer, 0, read);
    }
}
