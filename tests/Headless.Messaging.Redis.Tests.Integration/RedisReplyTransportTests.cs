// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using Headless.Messaging;
using Headless.Messaging.Redis;
using Headless.Messaging.Transport;
using Headless.Testing.Testcontainers;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Testcontainers.Redis;
using MessagingHeaders = Headless.Messaging.Headers;

namespace Tests;

/// <summary>
/// Redis-specific reply channel behavior the shared request/reply suite cannot observe: a reply leaves no key, the
/// channel survives a dropped subscription connection, a listener opened while the server is down becomes ready once
/// it is up, and a send outside the reply namespace never reaches the server.
/// </summary>
[Collection<RedisMessagingFixture>]
public sealed class RedisReplyTransportTests(RedisMessagingFixture fixture) : TestBase
{
    // Generous enough for a server under test load; each condition is normally met in well under a second.
    private static readonly TimeSpan _Bound = TimeSpan.FromSeconds(30);

    private readonly List<RedisConnectionPool> _pools = [];

    [Fact]
    public async Task should_deliver_a_reply_without_creating_any_key_under_the_reply_namespace()
    {
        // given
        var received = Channel.CreateUnbounded<TransportMessage>();
        await using var listener = await _CreateTransport(_CreatePool(fixture.ConnectionString))
            .OpenListenerAsync((reply, _) => received.Writer.WriteAsync(reply, AbortToken), AbortToken);
        var address = await listener.WaitForAddressAsync(AbortToken).AsTask().WaitAsync(_Bound, AbortToken);
        var sender = _CreateTransport(_CreatePool(fixture.ConnectionString));

        // when
        await sender.SendAsync(address, _Reply("first", "{\"ok\":true}"u8.ToArray()), AbortToken);
        var delivered = await received.Reader.ReadAsync(AbortToken).AsTask().WaitAsync(_Bound, AbortToken);
        var keysWhileListening = await fixture.ScanKeysAsync($"*{ReplyAddresses.Prefix}*", AbortToken);
        await listener.DisposeAsync();

        // then
        delivered.Headers[MessagingHeaders.InReplyTo].Should().Be("first");
        delivered.Body.ToArray().Should().Equal("{\"ok\":true}"u8.ToArray());
        keysWhileListening.Should().BeEmpty("a reply is a PUBLISH, which writes no key");
        await _UntilAsync(
            async () => await fixture.CountSubscribersAsync(address, AbortToken) == 0,
            "closing the listener unsubscribed its channel"
        );
        (await fixture.ScanKeysAsync($"*{ReplyAddresses.Prefix}*", AbortToken)).Should().BeEmpty();
    }

    [Fact]
    public async Task should_receive_on_the_same_address_after_the_subscription_connection_drops()
    {
        // given
        // One multiplexer in the pool, so the test watches the connection the listener subscribes through.
        var listenerPool = _CreatePool(fixture.ConnectionString, connectionPoolSize: 1);
        var connection = await listenerPool.ConnectAsync(AbortToken);
        var dropped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        // Over RESP3, the client's default, subscriptions share the interactive connection, so any failure counts.
        connection.ConnectionFailed += (_, _) => dropped.TrySetResult();
        var received = Channel.CreateUnbounded<TransportMessage>();
        await using var listener = await _CreateTransport(listenerPool)
            .OpenListenerAsync((reply, _) => received.Writer.WriteAsync(reply, AbortToken), AbortToken);
        var address = await listener.WaitForAddressAsync(AbortToken).AsTask().WaitAsync(_Bound, AbortToken);

        // when
        await fixture.KillPubSubClientsAsync(AbortToken);
        await dropped.Task.WaitAsync(_Bound, AbortToken);

        // then
        var sender = _CreateTransport(_CreatePool(fixture.ConnectionString));
        var delivered = await _SendUntilReceivedAsync(sender, address, received.Reader);
        delivered.Headers[MessagingHeaders.InReplyTo].Should().Be("after-drop");
        (await listener.WaitForAddressAsync(AbortToken).AsTask().WaitAsync(_Bound, AbortToken))
            .Should()
            .Be(address, "the multiplexer re-subscribes the channel, so the address survives the reconnect");
    }

    [Fact]
    public async Task should_hand_out_the_address_once_a_server_unreachable_at_open_comes_up()
    {
        // given
        await using var server = new DelayedRedisServer();
        var received = Channel.CreateUnbounded<TransportMessage>();
        var transport = _CreateTransport(_CreatePool(server.ConnectionString));

        // when
        await using var listener = await transport.OpenListenerAsync(
            (reply, _) => received.Writer.WriteAsync(reply, AbortToken),
            AbortToken
        );
        var whileDown = listener.WaitForAddressAsync(AbortToken).AsTask();
        whileDown.IsCompleted.Should().BeFalse("no address is handed out while the server is down");
        await server.StartAsync(AbortToken);

        // then
        var address = await whileDown.WaitAsync(_Bound, AbortToken);
        var sender = _CreateTransport(_CreatePool(server.ConnectionString));
        await sender.SendAsync(address, _Reply("after-start"), AbortToken);
        var delivered = await received.Reader.ReadAsync(AbortToken).AsTask().WaitAsync(_Bound, AbortToken);
        delivered.Headers[MessagingHeaders.InReplyTo].Should().Be("after-start");
    }

    [Theory]
    [InlineData("headless:messaging:queue:forged-{0}")]
    [InlineData("headless:messaging:bus:forged-{0}")]
    [InlineData("orders-{0}")]
    [InlineData("headless.replyforged-{0}")]
    public async Task should_refuse_a_stream_key_or_unrelated_channel_without_writing(string template)
    {
        // given
        var target = string.Format(CultureInfo.InvariantCulture, template, Guid.NewGuid().ToString("N"));
        var admin = await fixture.GetAdminConnectionAsync();
        var database = admin.GetDatabase();
        // The target is both a stream with one entry and a watched channel, so a write of either kind would show.
        await database.StreamAddAsync(target, "seed", "1");
        var watch = await admin.GetSubscriber().SubscribeAsync(RedisChannel.Literal(target));
        var transport = _CreateTransport(_CreatePool(fixture.ConnectionString));

        try
        {
            // when
            var act = () => transport.SendAsync(target, _Reply("forged"), AbortToken).AsTask();

            // then
            ((IReplyTransport)transport)
                .IsReplyAddress(target)
                .Should()
                .BeFalse();
            await act.Should().ThrowAsync<ArgumentException>().WithParameterName("address");
            (await database.StreamLengthAsync(target)).Should().Be(1, "the refused send added no stream entry");

            // A control message published afterwards arrives first, so the refused send published nothing before it.
            await admin.GetSubscriber().PublishAsync(RedisChannel.Literal(target), "control");
            var first = await watch.ReadAsync(AbortToken).AsTask().WaitAsync(_Bound, AbortToken);
            first.Message.ToString().Should().Be("control");
        }
        finally
        {
            await watch.UnsubscribeAsync();
            await database.KeyDeleteAsync(target);
        }
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        foreach (var pool in _pools)
        {
            await pool.DisposeAsync();
        }

        await base.DisposeAsyncCore();
    }

    private static async Task _UntilAsync(Func<Task<bool>> condition, string what)
    {
        using var bound = new CancellationTokenSource(_Bound);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(bound.Token, AbortToken);

        while (!await condition())
        {
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(20), linked.Token);
            }
            catch (OperationCanceledException e) when (bound.IsCancellationRequested)
            {
                throw new TimeoutException($"Gave up after {_Bound} waiting until {what}.", e);
            }
        }
    }

    private static async Task<TransportMessage> _SendUntilReceivedAsync(
        RedisReplyTransport sender,
        string address,
        ChannelReader<TransportMessage> received
    )
    {
        using var bound = new CancellationTokenSource(_Bound);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(bound.Token, AbortToken);

        // Replies published before the multiplexer has re-subscribed are discarded, so keep sending until one arrives;
        // each attempt waits briefly for its delivery.
        while (true)
        {
            await sender.SendAsync(address, _Reply("after-drop"), linked.Token);

            using var attempt = CancellationTokenSource.CreateLinkedTokenSource(linked.Token);
            attempt.CancelAfter(TimeSpan.FromMilliseconds(250));

            try
            {
                return await received.ReadAsync(attempt.Token);
            }
            catch (OperationCanceledException) when (!linked.IsCancellationRequested) { }
        }
    }

    private RedisConnectionPool _CreatePool(string connectionString, int connectionPoolSize = 2)
    {
        var pool = new RedisConnectionPool(
            Options.Create(
                new RedisMessagingOptions
                {
                    Configuration = ConfigurationOptions.Parse(connectionString),
                    ConnectionPoolSize = connectionPoolSize,
                }
            ),
            NullLoggerFactory.Instance
        );
        _pools.Add(pool);

        return pool;
    }

    private static RedisReplyTransport _CreateTransport(IRedisConnectionPool pool)
    {
        return new RedisReplyTransport(pool, NullLogger<RedisReplyTransport>.Instance);
    }

    private static TransportMessage _Reply(string inReplyTo, byte[]? body = null)
    {
        return new TransportMessage(
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [MessagingHeaders.MessageId] = Guid.NewGuid().ToString("D"),
                [MessagingHeaders.InReplyTo] = inReplyTo,
            },
            body ?? "{}"u8.ToArray()
        );
    }
}

/// <summary>
/// A Redis server of one test's own, on a fixed host port reserved before it starts, so a client can be pointed at it
/// while it is still down. It is never reused, so its state stays this test's.
/// </summary>
internal sealed class DelayedRedisServer : IAsyncDisposable
{
    private const int _RedisPort = 6379;

    private readonly int _port;
    private readonly RedisContainer _container;

    public DelayedRedisServer()
    {
        _port = _ReserveFreePort();
        _container = new RedisBuilder(TestImages.Redis).WithPortBinding(_port, _RedisPort).Build();
    }

    /// <summary>The server's address, known before it starts.</summary>
    public string ConnectionString => $"{_container.Hostname}:{_port}";

    public Task StartAsync(CancellationToken cancellationToken)
    {
        return _container.StartAsync(cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        return _container.DisposeAsync();
    }

    private static int _ReserveFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
