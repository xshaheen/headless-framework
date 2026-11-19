// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net.Sockets;
using System.Threading.Channels;
using DotNet.Testcontainers.Builders;
using Headless.Messaging;
using Headless.Messaging.Redis;
using Headless.Messaging.Transport;
using Headless.Testing.Testcontainers;
using Headless.Testing.Tests;
using Headless.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Testcontainers.Redis;
using Tests.Helpers;
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

    // A listener whose connect failed reopens through the reply supervisor, which waits at most its back-off ceiling
    // between passes; once the server serves, the address follows within that wait and one connect, subscribe, and PING
    // pass on a host under suite load.
    private static readonly TimeSpan _ReopenBound = ReconnectBackoff.Ceiling + TimeSpan.FromSeconds(30);

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

        // then: closing the listener drains its loop, which unsubscribes the channel before it ends
        delivered.Headers[MessagingHeaders.InReplyTo].Should().Be("first");
        delivered.Body.ToArray().Should().Equal("{\"ok\":true}"u8.ToArray());
        keysWhileListening.Should().BeEmpty("a reply is a PUBLISH, which writes no key");
        (await fixture.CountSubscribersAsync(address, AbortToken))
            .Should()
            .Be(0, "closing the listener unsubscribed its channel");
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
        var log = new List<(LogLevel Level, EventId EventId, string Message)>();
        using var loggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(logging =>
            logging.SetMinimumLevel(LogLevel.Trace).AddProvider(new CapturingLoggerProvider(log))
        );
        var transport = new RedisReplyTransport(
            _CreatePool(server.ConnectionString, loggerFactory: loggerFactory),
            loggerFactory.CreateLogger<RedisReplyTransport>()
        );

        // when
        await using var listener = await transport.OpenListenerAsync(
            (reply, _) => received.Writer.WriteAsync(reply, AbortToken),
            AbortToken
        );
        var whileDown = listener.WaitForAddressAsync(AbortToken).AsTask();
        whileDown.IsCompleted.Should().BeFalse("no address is handed out while the server is down");
        // Returns once Redis answers PING through its published port, so the listener's next pass can connect.
        await server.StartAsync(AbortToken);

        // then
        // Each failed pass while the server was down doubled the listener's back-off, up to its ceiling, so the next pass
        // can be that far away.
        string address;
        try
        {
            address = await whileDown.WaitAsync(_ReopenBound, AbortToken);
        }
        catch (TimeoutException e)
        {
            string logged;
            lock (log)
            {
                logged = string.Join(
                    Environment.NewLine,
                    log.Select(entry => $"  {entry.Level} {entry.EventId.Name}: {entry.Message}")
                );
            }

            throw new TimeoutException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Waited {_ReopenBound.TotalSeconds:0} s for the address once the server was up. The listener and its pool logged:{Environment.NewLine}{logged}"
                ),
                e
            );
        }

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

    private RedisConnectionPool _CreatePool(
        string connectionString,
        int connectionPoolSize = 2,
        ILoggerFactory? loggerFactory = null
    )
    {
        var pool = new RedisConnectionPool(
            Options.Create(
                new RedisMessagingOptions
                {
                    Configuration = ConfigurationOptions.Parse(connectionString),
                    ConnectionPoolSize = connectionPoolSize,
                }
            ),
            loggerFactory ?? NullLoggerFactory.Instance
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
/// A Redis server of one test's own behind a <see cref="BrokerPortForwarder"/>, so a client can be pointed at its
/// address while it is still down. It is never reused, so its state stays this test's.
/// </summary>
internal sealed class DelayedRedisServer : IAsyncDisposable
{
    private const int _RedisPort = 6379;

    // Names this server's snapshot file, which CONFIG GET reads back: another test's forwarder to another Redis also
    // answers PING, but not with this name.
    private readonly string _identity = $"delayed-{Guid.NewGuid():N}.rdb";
    private readonly PublishedPortCheck _check;
    private readonly RedisContainer _container;
    private readonly BrokerPortForwarder _forwarder;

    public DelayedRedisServer()
    {
        _check = new PublishedPortCheck(_RedisPort, _ReadIdentityAsync);
        _container = new RedisBuilder(TestImages.Redis)
            .WithCommand("--dbfilename", _identity)
            .WithWaitStrategy(_check.AddTo(Wait.ForUnixContainer()))
            .Build();
        _forwarder = new BrokerPortForwarder(() => _container, _RedisPort);
    }

    /// <summary>The server's address, known before it starts.</summary>
    public string ConnectionString => _forwarder.Address;

    /// <summary>Starts the server and returns once it answers through its published port from the test host.</summary>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        return _check.StartAsync(_container, cancellationToken);
    }

    private async Task<PublishedPortReply> _ReadIdentityAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        await stream.WriteAsync("CONFIG GET dbfilename\r\n"u8.ToArray(), cancellationToken);

        // A CONFIG GET reply is five lines (an array of two bulk strings); an error is one. Read the whole of either, so a
        // foreign Redis whose reply names another file is told apart without waiting out the attempt.
        var reply = await PublishedPortCheck.ReadReplyAsync(
            stream,
            text => text.Split("\r\n").Length > (text.StartsWith('-') ? 1 : 5),
            maxBytes: 256,
            cancellationToken
        );

        return reply switch
        {
            // The runtime accepted the connection but cannot reach the server yet.
            null => PublishedPortReply.NotYet,
            // Redis answers LOADING while it reads its data at start, and serves once that finishes.
            _ when reply.StartsWith("-LOADING", StringComparison.Ordinal) => PublishedPortReply.NotYet,
            _ when reply.Contains(_identity, StringComparison.Ordinal) => PublishedPortReply.Container,
            _ => PublishedPortReply.AnotherListener,
        };
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await _container.DisposeAsync();
        }
        finally
        {
            await _forwarder.DisposeAsync();
        }
    }
}
