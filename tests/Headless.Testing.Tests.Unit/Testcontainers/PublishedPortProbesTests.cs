// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using DotNet.Testcontainers.Containers;
using Headless.Testing.Testcontainers;
using Headless.Testing.Tests;

namespace Tests.Testcontainers;

public sealed class PublishedPortProbesTests : TestBase
{
    private const int _ContainerPort = 9092;

    [Fact]
    public async Task should_name_the_container_when_kafka_answers_with_its_cluster_id()
    {
        // given
        var clusterId = PublishedPortProbes.NewKafkaClusterId();

        // when
        var reply = await _ProbeAsync(
            PublishedPortProbes.KafkaClusterId,
            _KafkaMetadata(clusterId),
            _Container(stdout: _MetaProperties(clusterId))
        );

        // then
        reply.Should().Be(PublishedPortReply.Container);
    }

    [Fact]
    public async Task should_name_another_listener_when_kafka_answers_with_another_cluster_id()
    {
        // given - another broker on the port, with an id of its own
        var reply = await _ProbeAsync(
            PublishedPortProbes.KafkaClusterId,
            _KafkaMetadata("4L6g3nShT-eMCtK--X86sw"),
            _Container(stdout: _MetaProperties(PublishedPortProbes.NewKafkaClusterId()))
        );

        // then
        reply.Should().Be(PublishedPortReply.AnotherListener);
    }

    [Fact]
    public async Task should_name_another_listener_when_an_http_server_answers_a_kafka_probe()
    {
        // when
        var reply = await _ProbeAsync(
            PublishedPortProbes.KafkaClusterId,
            "HTTP/1.1 400 Bad Request\r\n\r\n"u8.ToArray()
        );

        // then
        reply.Should().Be(PublishedPortReply.AnotherListener);
    }

    [Fact]
    public async Task should_wait_when_the_port_closes_before_kafka_answers()
    {
        // when
        var reply = await _ProbeAsync(PublishedPortProbes.KafkaClusterId, reply: null);

        // then
        reply.Should().Be(PublishedPortReply.NotYet);
    }

    [Fact]
    public void should_create_a_kraft_cluster_id_of_sixteen_bytes()
    {
        // when
        var clusterId = PublishedPortProbes.NewKafkaClusterId();

        // then
        clusterId.Should().HaveLength(22).And.MatchRegex("^[A-Za-z0-9_-]+$");
        clusterId.Should().NotBe(PublishedPortProbes.NewKafkaClusterId());
    }

    [Fact]
    public async Task should_name_the_container_when_azurite_knows_the_account()
    {
        // when
        var reply = await _ProbeAsync(
            PublishedPortProbes.AzuriteAccount("probeaccount", tablePort: 10002),
            _Http("403 Server failed to authenticate the request", "<Error><Code>AuthorizationFailure</Code></Error>")
        );

        // then
        reply.Should().Be(PublishedPortReply.Container);
    }

    [Fact]
    public async Task should_name_another_listener_when_azurite_does_not_know_the_account()
    {
        // given - another Azurite reports an account it was not started with as missing
        var reply = await _ProbeAsync(
            PublishedPortProbes.AzuriteAccount("probeaccount", tablePort: 10002),
            _Http("404 The specified resource does not exist.", "<Error><Code>ResourceNotFound</Code></Error>")
        );

        // then
        reply.Should().Be(PublishedPortReply.AnotherListener);
    }

    [Fact]
    public async Task should_name_another_listener_when_another_object_store_refuses_every_request()
    {
        // given - a server that refuses everything, as MinIO does an unsigned request
        var reply = await _ProbeAsync(
            PublishedPortProbes.AzuriteAccount("probeaccount", tablePort: 10002),
            _Http("403 Forbidden", "<Error><Code>AccessDenied</Code></Error>")
        );

        // then
        reply.Should().Be(PublishedPortReply.AnotherListener);
    }

    [Fact]
    public async Task should_name_the_container_when_rabbitmq_names_the_containers_host()
    {
        // when
        var reply = await _ProbeAsync(
            PublishedPortProbes.RabbitMqClusterName,
            _AmqpStart("rabbit@0123456789ab"),
            _Container(stdout: "0123456789ab\n")
        );

        // then
        reply.Should().Be(PublishedPortReply.Container);
    }

    [Fact]
    public async Task should_name_another_listener_when_rabbitmq_names_another_host()
    {
        // when
        var reply = await _ProbeAsync(
            PublishedPortProbes.RabbitMqClusterName,
            _AmqpStart("rabbit@ba9876543210"),
            _Container(stdout: "0123456789ab\n")
        );

        // then
        reply.Should().Be(PublishedPortReply.AnotherListener);
    }

    [Fact]
    public async Task should_name_the_container_when_it_shows_the_open_connection_on_its_port()
    {
        // given - /proc/net/tcp lists the probe's connection as established (01) on local port 9092 (0x2384)
        const string table = """
              sl  local_address rem_address   st tx_queue rx_queue tr tm->when retrnsmt   uid  timeout inode
               0: 00000000:2384 00000000:0000 0A 00000000:00000000 00:00000000 00000000     0        0 1 1
               1: 030011AC:2384 010011AC:C350 01 00000000:00000000 00:00000000 00000000     0        0 2 1
            """;

        // when
        var reply = await _ProbeAsync(
            PublishedPortProbes.ConnectionSeenInside("PING\r\n"u8.ToArray()),
            "+PONG\r\n"u8.ToArray(),
            _Container(stdout: table)
        );

        // then
        reply.Should().Be(PublishedPortReply.Container);
    }

    [Fact]
    public async Task should_name_another_listener_when_the_container_only_listens_on_its_port()
    {
        // given - the reply came from elsewhere: the container's port has a listener (0A) and no connection
        const string table = """
              sl  local_address rem_address   st tx_queue rx_queue tr tm->when retrnsmt   uid  timeout inode
               0: 00000000:2384 00000000:0000 0A 00000000:00000000 00:00000000 00000000     0        0 1 1
               1: 030011AC:9C40 010011AC:2384 01 00000000:00000000 00:00000000 00000000     0        0 2 1
            """;

        // when
        var reply = await _ProbeAsync(
            PublishedPortProbes.ConnectionSeenInside("PING\r\n"u8.ToArray()),
            "+PONG\r\n"u8.ToArray(),
            _Container(stdout: table)
        );

        // then
        reply.Should().Be(PublishedPortReply.AnotherListener);
    }

    private static string _MetaProperties(string clusterId)
    {
        return $"#\nnode.id=1\nversion=1\ncluster.id={clusterId}\n";
    }

    private static byte[] _KafkaMetadata(string clusterId)
    {
        using var body = new MemoryStream();
        _Int32(body, 1); // correlation id
        _Int32(body, 1); // one broker
        _Int32(body, 1); // node id
        _String(body, "localhost");
        _Int32(body, 32_768); // port
        _String(body, null); // rack
        _String(body, clusterId);
        _Int32(body, 1); // controller id
        _Int32(body, 0); // no topics

        using var frame = new MemoryStream();
        _Int32(frame, (int)body.Length);
        body.WriteTo(frame);

        return frame.ToArray();

        static void _Int32(Stream stream, int value)
        {
            Span<byte> bytes = stackalloc byte[4];
            BinaryPrimitives.WriteInt32BigEndian(bytes, value);
            stream.Write(bytes);
        }

        static void _String(Stream stream, string? value)
        {
            Span<byte> length = stackalloc byte[2];
            BinaryPrimitives.WriteInt16BigEndian(length, (short)(value?.Length ?? -1));
            stream.Write(length);

            if (value is not null)
            {
                stream.Write(Encoding.UTF8.GetBytes(value));
            }
        }
    }

    // An AMQP 0-9-1 method frame whose payload carries the server properties of connection.start, cluster name included.
    private static byte[] _AmqpStart(string clusterName)
    {
        var payload = Encoding.ASCII.GetBytes($"\0\n\0\n\0\t\0\0\u0001\u0000\fcluster_nameS\0\0\0\u0013{clusterName}");
        var frame = new byte[7 + payload.Length + 1];
        frame[0] = 1;
        BinaryPrimitives.WriteInt32BigEndian(frame.AsSpan(3), payload.Length);
        payload.CopyTo(frame, 7);
        frame[^1] = 0xCE;

        return frame;
    }

    private static byte[] _Http(string status, string body)
    {
        return Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {status}\r\nContent-Type: application/xml\r\nConnection: close\r\n\r\n{body}"
        );
    }

    private static IContainer _Container(string stdout = "")
    {
        var container = Substitute.For<IContainer>();
        container.Hostname.Returns("127.0.0.1");
        container
            .ExecAsync(Arg.Any<IList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new ExecResult(stdout, string.Empty, 0));

        return container;
    }

    /// <summary>
    /// Runs <paramref name="probe"/> against a one-connection server that reads the probe's request, then sends
    /// <paramref name="reply"/> and closes, or closes at once when it is null.
    /// </summary>
    private static async Task<PublishedPortReply> _ProbeAsync(
        PublishedPortProbe probe,
        byte[]? reply,
        IContainer? container = null
    )
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        try
        {
            var server = _ServeOnceAsync(listener, reply);

            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port, AbortToken);
            var answer = await probe(container ?? _Container(), _ContainerPort, client.GetStream(), AbortToken);

            // Hang up before waiting on the server, which drains until the probe's side closes.
            client.Close();
            await server;

            return answer;
        }
        finally
        {
            listener.Dispose();
        }
    }

    private static async Task _ServeOnceAsync(TcpListener listener, byte[]? reply)
    {
        using var client = await listener.AcceptTcpClientAsync(AbortToken);

        if (reply is null)
        {
            // Close gracefully: send FIN, then drain until the probe hangs up. Closing with the probe's request still
            // unread makes the OS send a reset, so the probe would read IOException instead of end-of-stream.
            var unanswered = client.GetStream();
            client.Client.Shutdown(SocketShutdown.Send);
            var drain = new byte[256];

            while (await unanswered.ReadAsync(drain, AbortToken) > 0) { }

            return;
        }

        var stream = client.GetStream();
        _ = await stream.ReadAsync(new byte[256], AbortToken);
        await stream.WriteAsync(reply, AbortToken);
    }
}
