// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Exceptions;
using Headless.Messaging.Nats;
using Headless.Messaging.Registration;
using Headless.Messaging.Transport;
using Headless.Testing.Testcontainers;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NATS.Client.Core;
using NATS.Client.JetStream;
using Testcontainers.Nats;
using Tests.Helpers;
using Tests.RequestReply;
using INatsConnectionPool = Headless.Messaging.Nats.INatsConnectionPool;
using MessagingHeaders = Headless.Messaging.Headers;
using NatsConnectionPool = Headless.Messaging.Nats.NatsConnectionPool;

namespace Tests;

/// <summary>
/// NATS-specific reply channel behavior the shared request/reply suite cannot observe: replies stay out of JetStream,
/// the subject survives a server restart, a request no stream captures fails at once, and a send outside the reply
/// namespace never reaches the server.
/// </summary>
[Collection("Nats")]
public sealed class NatsReplyTransportTests(NatsFixture fixture) : TestBase
{
    // Generous enough for a server under test load; each condition is normally met in well under a second.
    private static readonly TimeSpan _Bound = TimeSpan.FromSeconds(30);

    private readonly List<NatsConnectionPool> _pools = [];
    private readonly List<ILoggerFactory> _loggerFactories = [];

    [Fact]
    public async Task should_store_requests_in_the_operator_stream_but_never_a_reply_in_any_stream()
    {
        // given
        await fixture.EnsureOperatorStreamAsync();
        var requestFilter = NatsPhysicalAddress.Subject(
            MessageLane.Queue,
            $"{NatsFixture.OperatorMessageNamePrefix}.>"
        );
        var requestsBefore = await _CountStoredSubjectsAsync(requestFilter);

        // when
        await TransportRequestReplyConformance.AssertRoundTripAsync(
            new NatsProviderConformanceDriver(fixture, provisionStreams: false),
            AbortToken
        );

        // then
        // The operator stream keeps what it captures, so it shows the probe sees a stored request.
        (await _CountStoredSubjectsAsync(requestFilter))
            .Should()
            .BeGreaterThan(requestsBefore, "the request was stored by the stream that captures it");
        (await fixture.ListStoredSubjectsAsync($"{ReplyAddresses.Prefix}>", AbortToken))
            .Should()
            .BeEmpty("a reply is a core publish that no stream captures");
    }

    [Fact]
    public async Task should_receive_on_the_same_address_after_the_server_restarts()
    {
        // given
        await using var server = new RestartableNatsServer();
        await server.StartAsync(AbortToken);
        var listenerPool = _CreatePool(server.ConnectionString);
        var disconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        listenerPool.GetConnection().ConnectionDisconnected += (_, _) =>
        {
            disconnected.TrySetResult();
            return ValueTask.CompletedTask;
        };
        var received = Channel.CreateUnbounded<TransportMessage>();
        await using var listener = await _CreateTransport(listenerPool)
            .OpenListenerAsync((reply, _) => received.Writer.WriteAsync(reply, AbortToken), AbortToken);
        var address = await listener.WaitForAddressAsync(AbortToken).AsTask().WaitAsync(_Bound, AbortToken);

        // when
        await server.RestartAsync(AbortToken);
        await disconnected.Task.WaitAsync(_Bound, AbortToken);

        // then
        // A sender on a fresh connection, so the reply reaches the listener only through the restarted server.
        var sender = _CreateTransport(_CreatePool(server.ConnectionString));
        var delivered = await _SendUntilReceivedAsync(sender, address, received.Reader);
        delivered.Headers[MessagingHeaders.InReplyTo].Should().Be("after-restart");
        (await listener.WaitForAddressAsync(AbortToken).AsTask().WaitAsync(_Bound, AbortToken))
            .Should()
            .Be(address, "the client re-sends the subscription, so the address survives the restart");
    }

    [Fact]
    public async Task should_hand_out_the_address_once_a_server_unreachable_at_open_comes_up()
    {
        // given
        await using var server = new RestartableNatsServer();
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

    [Fact]
    public async Task should_fail_a_request_at_once_when_no_stream_captures_its_subject()
    {
        // given
        var run = Guid.NewGuid().ToString("N");
        var services = new ServiceCollection();
        services.AddLogging();
        services.ConfigureMessaging(messaging =>
        {
            messaging.Message<UncapturedRequest>($"uncaptured.{run}.request");
            messaging.Message<UncapturedResponse>($"uncaptured.{run}.response");
        });
        services.AddHeadlessMessaging(setup =>
        {
            setup.UseNats(options => options.Servers = fixture.ConnectionString);
            setup.UseInMemoryStorage();
            setup.Options.RequiredInboxCapability = MessagingInboxCapabilityTier.ProcessLocal;
            setup.AddRequestReply();
        });
        await using var caller = services.BuildServiceProvider();
        await caller.GetRequiredService<IBootstrapper>().BootstrapAsync(AbortToken);
        var timeout = TimeSpan.FromSeconds(60);
        var stopwatch = Stopwatch.StartNew();

        // when
        var act = () =>
            caller
                .GetRequiredService<IRequestClient>()
                .RequestAsync<UncapturedRequest, UncapturedResponse>(
                    new UncapturedRequest("sku-1"),
                    new RequestOptions { Timeout = timeout },
                    AbortToken
                );

        // then — JetStream refused the send, so the request never left: not sent, with the transport's failure inside
        var thrown = await act.Should().ThrowAsync<RequestNotSentException>();
        thrown.Which.InnerException.Should().BeOfType<PublisherSentFailedException>();
        stopwatch
            .Elapsed.Should()
            .BeLessThan(timeout / 4, "JetStream answers at once when no stream captures a subject");
    }

    [Fact]
    public async Task should_warn_once_naming_the_stream_that_captures_the_reply_subject()
    {
        // given — an operator stream whose subject filter covers every reply subject
        var streamName = $"REPLY_CAPTURE_{Guid.NewGuid():N}";
        var js = new NatsJSContext(await fixture.GetConnectionAsync());
        await fixture.EnsureStreamAsync(streamName, $"{ReplyAddresses.Prefix}>");
        var log = new List<(LogLevel Level, EventId EventId, string Message)>();

        try
        {
            // when
            await using var listener = await _CreateTransport(_CreatePool(fixture.ConnectionString), log)
                .OpenListenerAsync((_, _) => ValueTask.CompletedTask, AbortToken);
            var address = await listener.WaitForAddressAsync(AbortToken).AsTask().WaitAsync(_Bound, AbortToken);

            // then — the address is served regardless, and one warning names the stream
            address.Should().StartWith(ReplyAddresses.Prefix);
            await _WaitUntilAsync(() => _CapturedWarnings(log).Count > 0);
            var warning = _CapturedWarnings(log).Should().ContainSingle().Subject;
            warning.Level.Should().Be(LogLevel.Warning);
            warning.Message.Should().Contain(streamName).And.Contain(address);
        }
        finally
        {
            // The sibling test asserts that no stream ever stores a reply, so this stream must not outlive the test.
            await js.DeleteStreamAsync(streamName, AbortToken);
        }
    }

    [Fact]
    public async Task should_not_warn_when_no_stream_captures_the_reply_subject()
    {
        // given — only the lane streams exist
        var log = new List<(LogLevel Level, EventId EventId, string Message)>();

        // when
        await using var listener = await _CreateTransport(_CreatePool(fixture.ConnectionString), log)
            .OpenListenerAsync((_, _) => ValueTask.CompletedTask, AbortToken);
        await listener.WaitForAddressAsync(AbortToken).AsTask().WaitAsync(_Bound, AbortToken);

        // then — the check ran against a live JetStream API and found nothing to warn about
        await Task.Delay(TimeSpan.FromSeconds(2), AbortToken);
        _CapturedWarnings(log).Should().BeEmpty();
        log.Should().NotContain(entry => entry.EventId.Id == 14, "the check itself must not fail on a healthy server");
    }

    [Theory]
    [InlineData("headless.queue.forged-{0}")]
    [InlineData("headless.bus.forged-{0}")]
    [InlineData("_INBOX.forged-{0}")]
    public async Task should_refuse_a_lane_or_inbox_subject_without_publishing(string template)
    {
        // given
        var subject = string.Format(CultureInfo.InvariantCulture, template, Guid.NewGuid().ToString("N"));
        var transport = _CreateTransport(_CreatePool(fixture.ConnectionString));
        var connection = await fixture.GetConnectionAsync();
        await using var watch = await connection.SubscribeCoreAsync<string>(subject, cancellationToken: AbortToken);
        await connection.PingAsync(AbortToken);

        // when
        var act = () => transport.SendAsync(subject, _Reply("forged"), AbortToken).AsTask();

        // then
        transport.IsReplyAddress(subject).Should().BeFalse();
        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("address");

        // A control message published afterwards arrives first, so the refused send published nothing before it.
        await connection.PublishAsync(subject, "control", cancellationToken: AbortToken);
        var first = await watch.Msgs.ReadAsync(AbortToken).AsTask().WaitAsync(_Bound, AbortToken);
        first.Data.Should().Be("control");
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        foreach (var pool in _pools)
        {
            await pool.DisposeAsync();
        }

        foreach (var factory in _loggerFactories)
        {
            factory.Dispose();
        }

        await base.DisposeAsyncCore();
    }

    private async Task<int> _CountStoredSubjectsAsync(string filter)
    {
        return (await fixture.ListStoredSubjectsAsync(filter, AbortToken)).Values.Sum(static subjects =>
            subjects.Count
        );
    }

    private static async Task<TransportMessage> _SendUntilReceivedAsync(
        NatsReplyTransport sender,
        string address,
        ChannelReader<TransportMessage> received
    )
    {
        using var bound = new CancellationTokenSource(_Bound);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(bound.Token, AbortToken);

        // Replies sent before the restarted server has the subscription again are discarded, so keep sending until one
        // arrives; each attempt waits briefly for its delivery.
        while (true)
        {
            await sender.SendAsync(address, _Reply("after-restart"), linked.Token);

            using var attempt = CancellationTokenSource.CreateLinkedTokenSource(linked.Token);
            attempt.CancelAfter(TimeSpan.FromMilliseconds(250));

            try
            {
                return await received.ReadAsync(attempt.Token);
            }
            catch (OperationCanceledException) when (!linked.IsCancellationRequested) { }
        }
    }

    private NatsConnectionPool _CreatePool(string servers)
    {
        var pool = new NatsConnectionPool(
            NullLogger<NatsConnectionPool>.Instance,
            Options.Create(new NatsMessagingOptions { Servers = servers })
        );
        _pools.Add(pool);

        return pool;
    }

    private static NatsReplyTransport _CreateTransport(INatsConnectionPool pool)
    {
        return new NatsReplyTransport(pool, TimeProvider.System, NullLogger<NatsReplyTransport>.Instance);
    }

    private NatsReplyTransport _CreateTransport(
        INatsConnectionPool pool,
        List<(LogLevel Level, EventId EventId, string Message)> log
    )
    {
        // Fully qualified: the test base exposes a LoggerFactory property of its own.
        var factory = Microsoft.Extensions.Logging.LoggerFactory.Create(logging =>
            logging.AddProvider(new CapturingLoggerProvider(log))
        );
        _loggerFactories.Add(factory);
        return new NatsReplyTransport(pool, TimeProvider.System, factory.CreateLogger<NatsReplyTransport>());
    }

    private static List<(LogLevel Level, EventId EventId, string Message)> _CapturedWarnings(
        List<(LogLevel Level, EventId EventId, string Message)> log
    )
    {
        lock (log)
        {
            return [.. log.Where(static entry => entry.EventId.Id == 13)];
        }
    }

    private static async Task _WaitUntilAsync(Func<bool> condition)
    {
        using var bound = new CancellationTokenSource(_Bound);
        while (!condition())
        {
            await Task.Delay(TimeSpan.FromMilliseconds(50), bound.Token);
        }
    }

    private static TransportMessage _Reply(string inReplyTo)
    {
        return new TransportMessage(
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [MessagingHeaders.MessageId] = Guid.NewGuid().ToString("D"),
                [MessagingHeaders.InReplyTo] = inReplyTo,
            },
            "{}"u8.ToArray()
        );
    }

    internal sealed record UncapturedRequest(string Sku);

    internal sealed record UncapturedResponse(string Sku);
}

/// <summary>
/// A NATS server of one test's own, on a fixed host port: a restart keeps the address clients already know, so they
/// reconnect to the restarted server rather than lose it. It is never reused, so a restarted server stays this test's.
/// </summary>
internal sealed class RestartableNatsServer : IAsyncDisposable
{
    private readonly int _port;
    private readonly NatsContainer _container;

    public RestartableNatsServer()
    {
        _port = _ReserveFreePort();
        _container = new NatsBuilder(TestImages.Nats).WithPortBinding(_port, NatsBuilder.NatsClientPort).Build();
    }

    /// <summary>The server's address, known before it starts, so a client can be pointed at a server that is down.</summary>
    public string ConnectionString => $"nats://{_container.Hostname}:{_port}";

    public Task StartAsync(CancellationToken cancellationToken)
    {
        return _container.StartAsync(cancellationToken);
    }

    public async Task RestartAsync(CancellationToken cancellationToken)
    {
        await _container.StopAsync(cancellationToken);
        await _container.StartAsync(cancellationToken);
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
