// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using DotNet.Testcontainers.Containers;
using Headless.Testing.Testcontainers;
using Headless.Testing.Tests;

namespace Tests.Testcontainers;

public sealed class RedisHostPingWaitUntilTests : TestBase
{
    private const int _ContainerPort = 6379;
    private const string _OwnRunId = "0a1b2c3d4e5f60718293a4b5c6d7e8f901234567";

    [Fact]
    public async Task should_not_be_ready_when_nothing_listens_on_the_port()
    {
        // given - port 1 is privileged, so no test can bind it; a released ephemeral port can be handed to a
        // listener in another test process running at the same time, which would then accept the probe
        var container = _Container(port: 1);

        // when
        var ready = await new RedisHostPingWaitUntil(_ContainerPort).UntilAsync(container);

        // then
        ready.Should().BeFalse();
    }

    [Fact]
    public async Task should_not_be_ready_when_a_port_proxy_accepts_and_closes_the_connection()
    {
        // given - what a container runtime's port proxy does before it can reach the server
        // when
        var (ready, _) = await _ProbeServerAsync(reply: null);

        // then
        ready.Should().BeFalse();
    }

    [Fact]
    public async Task should_not_be_ready_while_the_server_loads_its_data()
    {
        // when
        var (ready, _) = await _ProbeServerAsync(reply: "-LOADING Redis is loading the dataset in memory\r\n");

        // then
        ready.Should().BeFalse();
    }

    [Fact]
    public async Task should_be_ready_when_the_port_answers_with_the_containers_run_id()
    {
        // when
        var (ready, received) = await _ProbeServerAsync(reply: _InfoServer(_OwnRunId));

        // then
        ready.Should().BeTrue();
        received.Should().Be("INFO server\r\n");
    }

    [Fact]
    public async Task should_fail_the_start_when_another_redis_answers_on_the_port()
    {
        // given - another test's Redis, or a forwarder to one, answers PING just as well
        var act = () => _ProbeServerAsync(reply: _InfoServer("ffffffffffffffffffffffffffffffffffffffff"));

        // when / then
        var thrown = await act.Should().ThrowAsync<InvalidOperationException>();
        thrown.Which.Message.Should().Contain("Another Redis or listener answered");
    }

    [Fact]
    public async Task should_fail_the_start_when_a_listener_of_another_protocol_answers()
    {
        // given
        var act = () => _ProbeServerAsync(reply: "HTTP/1.1 400 Bad Request\r\n\r\n");

        // when / then
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // What redis-cli prints for INFO server; through the port it arrives as one bulk string.
    private static string _InfoBody(string runId)
    {
        return $"# Server\r\nredis_version:7.4.0\r\nrun_id:{runId}\r\ntcp_port:6379\r\n";
    }

    private static string _InfoServer(string runId)
    {
        var body = _InfoBody(runId);
        return $"${Encoding.ASCII.GetByteCount(body).ToString(CultureInfo.InvariantCulture)}\r\n{body}\r\n";
    }

    private static IContainer _Container(int port)
    {
        var container = Substitute.For<IContainer>();
        container.Hostname.Returns("127.0.0.1");
        container.Name.Returns("/headless-redis");
        container.GetMappedPublicPort(_ContainerPort).Returns((ushort)port);
        container
            .ExecAsync(Arg.Any<IList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new ExecResult(_InfoBody(_OwnRunId), string.Empty, 0));

        return container;
    }

    /// <summary>Probes a one-connection server that sends <paramref name="reply" />, or closes at once when it is null.</summary>
    private static async Task<(bool Ready, string Received)> _ProbeServerAsync(string? reply)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        try
        {
            var server = _ServeOnceAsync(listener, reply);
            var ready = await new RedisHostPingWaitUntil(_ContainerPort).UntilAsync(
                _Container(((IPEndPoint)listener.LocalEndpoint).Port)
            );

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
