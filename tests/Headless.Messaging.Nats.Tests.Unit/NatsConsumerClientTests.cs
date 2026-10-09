// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using Headless.Messaging;
using Headless.Messaging.Internal;
using Headless.Messaging.Nats;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using NATS.Client.Core;
using NATS.Client.JetStream;
using INatsConnectionPool = Headless.Messaging.Nats.INatsConnectionPool;
using MsOptions = Microsoft.Extensions.Options;

namespace Tests;

public sealed class NatsConsumerClientTests : TestBase
{
    private readonly MsOptions.IOptions<NatsMessagingOptions> _options = MsOptions.Options.Create(
        new NatsMessagingOptions { Servers = "nats://localhost:4222" }
    );

    private readonly IServiceProvider _serviceProvider = new ServiceCollection().BuildServiceProvider();

    [Fact]
    public async Task should_have_correct_broker_address()
    {
        await using var client = _CreateClient("test-group");
        client.BrokerAddress.Name.Should().Be("nats");
        client.BrokerAddress.Endpoint.Should().Be("nats://localhost:4222");
    }

    [Fact]
    public async Task should_redact_credentials_from_broker_address()
    {
        var options = MsOptions.Options.Create(
            new NatsMessagingOptions { Servers = "nats://user:password@localhost:4222" }
        );
        await using var client = new NatsConsumerClient("test-group", 1, options, _serviceProvider);

        client.BrokerAddress.Endpoint.Should().Be("nats://localhost:4222");
    }

    [Fact]
    public async Task should_open_its_own_connection_from_the_supplied_connection_options_when_use_connection()
    {
        // given - the app supplied its connection; the consumer must copy its servers onto a socket of its own
        var appConnection = Substitute.For<INatsConnection>();
        var pool = Substitute.For<INatsConnectionPool>();
        pool.ConnectionOpts.Returns(NatsOpts.Default with { Url = "nats://app-host:4222" });
        pool.ServersAddress.Returns("nats://app-host:4222");
        pool.GetConnection().Returns(appConnection);
        var serviceProvider = new ServiceCollection().AddSingleton(pool).BuildServiceProvider();
        var options = MsOptions.Options.Create(new NatsMessagingOptions().UseConnection(_ => appConnection));
        NatsOpts? connectedOpts = null;

        await using var client = new NatsConsumerClient(
            "test-group",
            1,
            options,
            serviceProvider,
            connect: connection =>
            {
                connectedOpts = connection.Opts;
                return Task.CompletedTask;
            }
        );

        // when
        await client.ConnectAsync(AbortToken);

        // then
        connectedOpts.Should().NotBeNull();
        connectedOpts!.Url.Should().Be("nats://app-host:4222");
        connectedOpts.MaxReconnectRetry.Should().Be(0);
        client.BrokerAddress.Endpoint.Should().Be("nats://app-host:4222");
    }

    [Fact]
    public void should_throw_when_options_value_is_null()
    {
        var nullOptions = Substitute.For<MsOptions.IOptions<NatsMessagingOptions>>();
        nullOptions.Value.Returns((NatsMessagingOptions)null!);

        var act = () => new NatsConsumerClient("test-group", 1, nullOptions, _serviceProvider);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task should_accept_callback_assignment()
    {
        await using var client = _CreateClient("test-group");

        client.OnMessageCallback = (_, _) => Task.CompletedTask;
        client.OnLogCallback = _ => { };

        client.OnMessageCallback.Should().NotBeNull();
        client.OnLogCallback.Should().NotBeNull();
    }

    [Fact]
    public async Task should_throw_when_subscribing_with_null_topics()
    {
        await using var client = _CreateClient("test-group");

        var act = async () => await client.SubscribeAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task should_return_topics_as_collection_from_fetch()
    {
        var options = MsOptions.Options.Create(
            new NatsMessagingOptions
            {
                Servers = "nats://localhost:4222",
                StreamProvisioning = NatsStreamProvisioning.Disabled,
            }
        );
        await using var client = new NatsConsumerClient("test-group", 1, options, _serviceProvider);
        var messageNames = new[] { "topic1", "topic2", "topic3" };

        var result = await client.FetchMessageNamesAsync(messageNames, AbortToken);
        result.Should().BeEquivalentTo(messageNames);
    }

    [Fact]
    public void should_include_exact_and_sharded_descendant_subjects_for_sharded_message_when_build_consumer_subjects()
    {
        NatsConsumerClient
            .BuildConsumerSubjects(["orders"], new HashSet<string>(StringComparer.Ordinal) { "orders" })
            .Should()
            .BeEquivalentTo(["orders", "orders.>"]);
    }

    [Fact]
    public void should_not_include_wildcard_for_unsharded_message_when_build_consumer_subjects()
    {
        NatsConsumerClient
            .BuildConsumerSubjects(["orders"], new HashSet<string>(StringComparer.Ordinal))
            .Should()
            .BeEquivalentTo(["orders"]);
    }

    [Fact]
    public void should_subscribe_every_instance_client_to_the_bus_subjects_of_its_message_names()
    {
        NatsEveryInstanceListener
            .BuildEveryInstanceSubjects(
                ["payments.captured", "orders"],
                names => new HashSet<string>(
                    names.Where(x => string.Equals(x, "orders", StringComparison.Ordinal)),
                    StringComparer.Ordinal
                )
            )
            .Should()
            .Equal("headless.bus.payments.captured", "headless.bus.orders", "headless.bus.orders.>");
    }

    [Fact]
    public void should_drop_every_instance_subjects_a_sharded_wildcard_already_covers()
    {
        // given - a sharded "orders" subscribes "orders.>", which also matches every "orders.created" publish
        NatsEveryInstanceListener
            .BuildEveryInstanceSubjects(["orders", "orders.created"], names => names.ToHashSet(StringComparer.Ordinal))
            .Should()
            .Equal("headless.bus.orders", "headless.bus.orders.>");
    }

    [Fact]
    public void should_include_consumer_identity_for_bus_intent_when_build_durable_name()
    {
        NatsConsumerClient
            .BuildDurableName("payments", "orders.created", MessageLane.Bus)
            .Should()
            .StartWith("bus-payments-orders-created-");
    }

    [Fact]
    public void should_build_valid_stable_durable_name_when_consumer_identity_contains_dots()
    {
        var name = NatsConsumerClient.BuildDurableName("billing.invoice-projection", "orders.created", MessageLane.Bus);

        name.Should().StartWith("bus-billing-invoice-projection-orders-created-");
        name.Should().HaveLength("bus-billing-invoice-projection-orders-created-".Length + 12);
        name.IndexOfAny([' ', '.', '*', '>', '/', '\\']).Should().Be(-1);
        NatsConsumerClient
            .BuildDurableName("billing.invoice-projection", "orders.created", MessageLane.Bus)
            .Should()
            .Be(name);
        NatsConsumerClient
            .BuildDurableName("billing-invoice-projection", "orders.created", MessageLane.Bus)
            .Should()
            .NotBe(name, "a dotted and a dashed identity must not share one durable consumer");
    }

    [Fact]
    public void should_bound_durable_name_to_nats_limit_when_identity_and_subject_are_long()
    {
        var identity = "billing." + new string('a', 190);
        var subject = "orders." + new string('x', 60);

        var name = NatsConsumerClient.BuildDurableName(identity, subject, MessageLane.Bus);

        name.Should().HaveLength(255);
        name.Should().StartWith("bus-billing-aaa");
    }

    [Fact]
    public void should_share_destination_for_queue_intent_when_build_durable_name()
    {
        NatsConsumerClient
            .BuildDurableName("payments", "orders.created", MessageLane.Queue)
            .Should()
            .StartWith("queue-orders_created_");
    }

    [Fact]
    public void should_disambiguate_stream_and_durable_names_changed_by_normalization()
    {
        NatsPhysicalAddress
            .Stream(MessageLane.Bus, "orders.created")
            .Should()
            .NotBe(NatsPhysicalAddress.Stream(MessageLane.Bus, "orders_created"));
        NatsPhysicalAddress
            .Durable(MessageLane.Bus, "sales.east", "orders.created")
            .Should()
            .NotBe(NatsPhysicalAddress.Durable(MessageLane.Bus, "sales_east", "orders_created"));
    }

    // NextBackoff tests
    //
    // NextBackoff subtracts up to 25% jitter from the doubled/capped value (never adds), so every assertion
    // here checks a [0.75x, 1x] range against the ideal (unjittered) value rather than an exact TimeSpan —
    // the prior exact-value assertions were flaky by construction since the function always applies jitter.

    [Fact]
    public void should_never_return_below_the_floor_and_should_still_spread_above_it_when_next_backoff()
    {
        // The floor is a HARD guarantee: callers pass it to promise a minimum wait (JetStream API errors), so
        // jitter must not undercut it. But the floor path is itself a herd — an API error hits every consumer at
        // once — so it must still spread. When the floor pins the delay, jitter therefore goes UP, not down.
        var results = Enumerable
            .Range(0, 200)
            .Select(_ => NatsConsumerClient.NextBackoff(TimeSpan.FromSeconds(1), floor: TimeSpan.FromSeconds(5)))
            .ToList();

        results.Should().OnlyContain(r => r >= TimeSpan.FromSeconds(5), "the floor must never be undercut");
        results.Should().OnlyContain(r => r <= TimeSpan.FromSeconds(6.25), "the upward spread is 25% of the floor");
        results.Distinct().Should().HaveCountGreaterThan(1, "the floor path must not collapse to a lockstep value");
    }

    [Fact]
    public void should_stay_within_jitter_budget_of_the_ideal_doubling_curve_at_every_rung_when_next_backoff()
    {
        // Feed the theoretical (unjittered) doubling curve as input at each rung instead of chaining the
        // previous jittered output — chaining would compound each step's 25% uncertainty into an
        // ever-widening range and make the per-rung assertions meaningless a few steps in.
        (TimeSpan Current, TimeSpan ExpectedNext)[] rungs =
        [
            (TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)),
            (TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4)),
            (TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(8)),
            (TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(16)),
            (TimeSpan.FromSeconds(16), TimeSpan.FromSeconds(30)), // capped
            (TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30)), // stays capped
        ];

        foreach (var (current, expectedNext) in rungs)
        {
            var result = NatsConsumerClient.NextBackoff(current);
            result.Should().BeLessThanOrEqualTo(expectedNext);
            result.Should().BeGreaterThanOrEqualTo(expectedNext * 0.75);
        }
    }

    [Fact]
    public void should_stay_within_jitter_budget_at_every_rung_when_next_backoff_with_floor()
    {
        var floor = TimeSpan.FromSeconds(5);

        // Rung 1 is the floor-pinned case: max(2s, 5s) = 5s leaves no room to jitter downward without breaching
        // the floor, so the band spreads upward to [5s, 6.25s]. Every later rung has the exponential value above
        // the floor, so the band spreads downward as usual to [0.75x, 1x].
        (TimeSpan Current, TimeSpan Lower, TimeSpan Upper)[] rungs =
        [
            (TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(6.25)),
            (TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(7.5), TimeSpan.FromSeconds(10)),
            (TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(20)),
            (TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(22.5), TimeSpan.FromSeconds(30)),
        ];

        foreach (var (current, lower, upper) in rungs)
        {
            var result = NatsConsumerClient.NextBackoff(current, floor);
            result.Should().BeGreaterThanOrEqualTo(lower);
            result.Should().BeLessThanOrEqualTo(upper);
            result.Should().BeGreaterThanOrEqualTo(floor, "the floor is a hard guarantee at every rung");
        }
    }

    [Fact]
    public void should_produce_a_spread_of_values_instead_of_collapsing_to_a_constant_at_the_ceiling_when_next_backoff()
    {
        var results = Enumerable
            .Range(0, 200)
            .Select(_ => NatsConsumerClient.NextBackoff(TimeSpan.FromSeconds(60)))
            .ToHashSet();

        results
            .Should()
            .HaveCountGreaterThan(
                1,
                "jitter must keep spreading retries across a fleet even once the backoff saturates at the ceiling"
            );
        results.Should().OnlyContain(r => r <= TimeSpan.FromSeconds(30) && r >= TimeSpan.FromSeconds(22.5));
    }

    [Fact]
    public async Task should_dispose_without_connection()
    {
        var client = _CreateClient("test-group");

        var act = async () => await client.DisposeAsync();
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task should_defer_connection_disposal_until_canceled_connect_attempt_settles()
    {
        var connectStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var connectCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var connectionDisposed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var client = new NatsConsumerClient(
            "test-group",
            1,
            _options,
            _serviceProvider,
            connect: _ =>
            {
                connectStarted.SetResult();
                return connectCompletion.Task;
            },
            disposeConnection: _ =>
            {
                connectionDisposed.SetResult();
                return ValueTask.CompletedTask;
            }
        );
        using var cts = new CancellationTokenSource();

        var connectTask = client.ConnectAsync(cts.Token);
        await connectStarted.Task.WaitAsync(AbortToken);
        await cts.CancelAsync();

        var act = async () => await connectTask;
        await act.Should().ThrowAsync<OperationCanceledException>();
        await client.DisposeAsync();
        connectionDisposed.Task.IsCompleted.Should().BeFalse();

        connectCompletion.SetResult();
        await connectionDisposed.Task.WaitAsync(AbortToken);
    }

    // Pause/Resume tests

    [Fact]
    public async Task pause_async_is_idempotent_when_called_twice()
    {
        await using var client = _CreateClient("test-group");

        await client.PauseAsync(AbortToken);
        await client.PauseAsync(AbortToken);
    }

    [Fact]
    public async Task resume_async_is_noop_when_not_paused()
    {
        await using var client = _CreateClient("test-group");
        await client.ResumeAsync(AbortToken);
    }

    [Fact]
    public async Task pause_async_then_resume_async_completes_full_cycle()
    {
        await using var client = _CreateClient("test-group");

        await client.PauseAsync(AbortToken);
        await client.ResumeAsync(AbortToken);
    }

    [Fact]
    public async Task resume_async_is_idempotent_after_resume()
    {
        await using var client = _CreateClient("test-group");

        await client.PauseAsync(AbortToken);
        await client.ResumeAsync(AbortToken);
        await client.ResumeAsync(AbortToken);
    }

    [Fact]
    public async Task pause_async_is_noop_after_disposal()
    {
        var client = _CreateClient("test-group");
        await client.DisposeAsync();

        await client.PauseAsync(AbortToken);
    }

    [Fact]
    public async Task resume_async_is_noop_after_disposal()
    {
        var client = _CreateClient("test-group");
        await client.DisposeAsync();

        await client.ResumeAsync(AbortToken);
    }

    // CommitAsync / RejectAsync tests

    [Fact]
    public async Task should_ack_valid_nats_message_when_commit_async()
    {
        await using var client = _CreateClient("test-group");
        var msg = Substitute.For<INatsJSMsg<ReadOnlyMemory<byte>>>();

        await client.CommitAsync(msg, AbortToken);

        await msg.Received(1)
            .AckAsync(
                Arg.Is<AckOpts?>(options => options.HasValue && options.Value.DoubleAck == true),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_nak_with_a_redelivery_delay_when_reject_async()
    {
        await using var client = _CreateClient("test-group");
        var msg = Substitute.For<INatsJSMsg<ReadOnlyMemory<byte>>>();

        await client.RejectAsync(msg, AbortToken);

        // A plain NAK redelivers on the next pull, so an open circuit would spin on the same message.
        await msg.Received(1)
            .NakAsync(
                Arg.Is<AckOpts?>(options => options.HasValue && options.Value.NakDelay > TimeSpan.Zero),
                Arg.Any<CancellationToken>()
            );
    }

    [Theory]
    [InlineData(0UL, 1)]
    [InlineData(1UL, 1)]
    [InlineData(2UL, 2)]
    [InlineData(3UL, 4)]
    [InlineData(5UL, 16)]
    [InlineData(6UL, 30)]
    [InlineData(ulong.MaxValue, 30)]
    public void should_grow_the_nak_delay_with_the_delivery_count_up_to_30_seconds(
        ulong numDelivered,
        int nominalSeconds
    )
    {
        var nominal = TimeSpan.FromSeconds(nominalSeconds);

        var delays = Enumerable
            .Range(0, 200)
            .Select(seed => NatsConsumerClient.NakDelay(numDelivered, new Random(seed)));

        // Jittered down by at most a quarter, so rejections of one open circuit do not return together.
        delays.Should().OnlyContain(delay => delay <= nominal && delay >= nominal * 0.75);
        delays.Distinct().Should().HaveCountGreaterThan(1);
    }

    [Fact]
    public async Task should_not_throw_for_null_sender_when_commit_async()
    {
        await using var client = _CreateClient("test-group");

        var act = async () => await client.CommitAsync(null);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task should_not_throw_for_null_sender_when_reject_async()
    {
        await using var client = _CreateClient("test-group");

        var act = async () => await client.RejectAsync(null);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task should_not_throw_for_non_nats_sender_when_commit_async()
    {
        await using var client = _CreateClient("test-group");

        var act = async () => await client.CommitAsync("not a nats message");
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task should_not_throw_for_non_nats_sender_when_reject_async()
    {
        await using var client = _CreateClient("test-group");

        var act = async () => await client.RejectAsync("not a nats message");
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task should_log_on_ack_failure_when_commit_async()
    {
        await using var client = _CreateClient("test-group");
        LogMessageEventArgs? loggedArgs = null;
        client.OnLogCallback = args => loggedArgs = args;

        var msg = Substitute.For<INatsJSMsg<ReadOnlyMemory<byte>>>();
        msg.AckAsync(Arg.Any<AckOpts?>(), Arg.Any<CancellationToken>())
            .Returns(x => throw new InvalidOperationException("ack failed"));

        await client.CommitAsync(msg, AbortToken);

        loggedArgs.Should().NotBeNull();
        loggedArgs!.LogType.Should().Be(MqLogType.AsyncErrorEvent);
        loggedArgs.Reason.Should().Contain("ack failed");
    }

    [Fact]
    public async Task should_log_on_nak_failure_when_reject_async()
    {
        await using var client = _CreateClient("test-group");
        LogMessageEventArgs? loggedArgs = null;
        client.OnLogCallback = args => loggedArgs = args;

        var msg = Substitute.For<INatsJSMsg<ReadOnlyMemory<byte>>>();
        msg.NakAsync(Arg.Any<AckOpts?>(), Arg.Any<CancellationToken>())
            .Returns(x => throw new InvalidOperationException("nak failed"));

        await client.RejectAsync(msg, AbortToken);

        loggedArgs.Should().NotBeNull();
        loggedArgs!.LogType.Should().Be(MqLogType.AsyncErrorEvent);
        loggedArgs.Reason.Should().Contain("nak failed");
    }

    [Fact]
    public async Task should_nak_without_ack_when_custom_headers_builder_throws()
    {
        // given
        var options = MsOptions.Options.Create(
            new NatsMessagingOptions
            {
                Servers = "nats://localhost:4222",
                CustomHeadersBuilder = (_, _, _) => throw new InvalidOperationException("bad header builder"),
            }
        );

        var msg = Substitute.For<INatsJSMsg<ReadOnlyMemory<byte>>>();
        msg.Data.Returns(new ReadOnlyMemory<byte>("test"u8.ToArray()));
        msg.Headers.Returns(_CreateHeaders());

        var nakCalled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        msg.NakAsync(Arg.Any<AckOpts?>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                nakCalled.TrySetResult();
                return ValueTask.CompletedTask;
            });

        var consumer = Substitute.For<INatsJSConsumer>();
        _OnConsume(consumer, (_, token) => _Deliver(token, messages: msg));

        await using var client = new NatsConsumerClient(
            "test-group",
            0,
            options,
            _serviceProvider,
            (_, _, _) => Task.FromResult(consumer)
        );
        client.OnMessageCallback = (_, _) => Task.CompletedTask;
        client.OnLogCallback = _ => { };
        await client.SubscribeAsync(["orders.created"], AbortToken);

        using var cts = new CancellationTokenSource();
        var listeningTask = client.ListeningAsync(TimeSpan.FromMilliseconds(50), cts.Token).AsTask();
        try
        {
            // when
            await nakCalled.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);

            // then
            await msg.Received(1)
                .NakAsync(
                    Arg.Is<AckOpts?>(options => options.HasValue && options.Value.NakDelay > TimeSpan.Zero),
                    Arg.Any<CancellationToken>()
                );
            await msg.DidNotReceive().AckAsync(Arg.Any<AckOpts?>(), Arg.Any<CancellationToken>());
        }
        finally
        {
            await _StopListeningAsync(listeningTask, cts);
        }
    }

    [Fact]
    public async Task should_stamp_the_arrival_subject_as_transport_address_over_a_wire_value()
    {
        // given - a producer that tries to plant its own address
        var options = MsOptions.Options.Create(new NatsMessagingOptions { Servers = "nats://localhost:4222" });

        var headers = _CreateHeaders();
        headers[Headers.TransportAddress] = "forged.subject";
        var msg = Substitute.For<INatsJSMsg<ReadOnlyMemory<byte>>>();
        msg.Subject.Returns("headless.bus.orders.created.tenant-a");
        msg.Data.Returns(new ReadOnlyMemory<byte>("test"u8.ToArray()));
        msg.Headers.Returns(headers);

        var consumer = Substitute.For<INatsJSConsumer>();
        _OnConsume(consumer, (_, token) => _Deliver(token, messages: msg));

        await using var client = new NatsConsumerClient(
            "test-group",
            0,
            options,
            _serviceProvider,
            (_, _, _) => Task.FromResult(consumer)
        );
        var received = new TaskCompletionSource<TransportMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.OnMessageCallback = (message, _) =>
        {
            received.TrySetResult(message);
            return Task.CompletedTask;
        };
        client.OnLogCallback = _ => { };
        await client.SubscribeAsync(["orders.created"], AbortToken);

        using var cts = new CancellationTokenSource();
        var listeningTask = client.ListeningAsync(TimeSpan.FromMilliseconds(50), cts.Token).AsTask();
        try
        {
            // when
            var message = await received.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);

            // then
            message.Headers[Headers.TransportAddress].Should().Be("headless.bus.orders.created.tenant-a");
        }
        finally
        {
            await _StopListeningAsync(listeningTask, cts);
        }
    }

    [Fact]
    public async Task should_terminate_without_ack_when_required_header_is_missing()
    {
        // given
        var options = MsOptions.Options.Create(
            new NatsMessagingOptions
            {
                Servers = "nats://localhost:4222",
                CustomHeadersBuilder = (_, _, _) => [new KeyValuePair<string, string>(Headers.MessageId, string.Empty)],
            }
        );

        var msg = Substitute.For<INatsJSMsg<ReadOnlyMemory<byte>>>();
        msg.Data.Returns(new ReadOnlyMemory<byte>("test"u8.ToArray()));
        msg.Headers.Returns(_CreateHeaders());

        var callbackInvoked = false;
        var ackCalled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var consumer = Substitute.For<INatsJSConsumer>();
        _OnConsume(consumer, (_, token) => _Deliver(token, messages: msg));

        msg.AckTerminateAsync(Arg.Any<AckOpts?>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                ackCalled.TrySetResult();
                return ValueTask.CompletedTask;
            });

        await using var client = new NatsConsumerClient(
            "test-group",
            0,
            options,
            _serviceProvider,
            (_, _, _) => Task.FromResult(consumer)
        );
        client.OnMessageCallback = (_, _) =>
        {
            callbackInvoked = true;
            return Task.CompletedTask;
        };

        LogMessageEventArgs? loggedArgs = null;
        client.OnLogCallback = args =>
        {
            if (args.LogType == MqLogType.ConsumeError)
            {
                loggedArgs = args;
            }
        };

        await client.SubscribeAsync(["orders.created"], AbortToken);

        using var cts = new CancellationTokenSource();

        // when
        var listeningTask = client.ListeningAsync(TimeSpan.FromMilliseconds(50), cts.Token).AsTask();
        try
        {
            await ackCalled.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);

            // then — malformed input is terminated, so consumer stats and advisories show it, and never reaches user code
            callbackInvoked.Should().BeFalse();
            await msg.Received(1)
                .AckTerminateAsync(
                    Arg.Is<AckOpts?>(options => options.HasValue && options.Value.DoubleAck == true),
                    CancellationToken.None
                );
            await msg.DidNotReceive().AckAsync(Arg.Any<AckOpts?>(), Arg.Any<CancellationToken>());
            loggedArgs.Should().NotBeNull();
            loggedArgs!.Reason.Should().Contain("terminally acknowledged").And.NotContain("Messaging header");
        }
        finally
        {
            await _StopListeningAsync(listeningTask, cts);
        }
    }

    [Fact]
    public async Task should_not_fetch_messages_until_resumed_when_listening_async()
    {
        // given
        var nextCallCount = 0;
        var consumer = Substitute.For<INatsJSConsumer>();
        _OnConsume(
            consumer,
            (_, token) =>
            {
                Interlocked.Increment(ref nextCallCount);
                return _Deliver(token);
            }
        );

        await using var client = new NatsConsumerClient(
            "test-group",
            1,
            _options,
            _serviceProvider,
            (_, _, _) => Task.FromResult(consumer)
        );
        await client.SubscribeAsync(["orders.created", "orders.updated"], AbortToken);
        await client.PauseAsync(AbortToken);

        using var cts = new CancellationTokenSource();

        // when
        var listeningTask = client.ListeningAsync(TimeSpan.FromMilliseconds(50), cts.Token).AsTask();
        try
        {
            await Task.Delay(100, AbortToken);

            // then
            nextCallCount.Should().Be(0);

            await client.ResumeAsync(AbortToken);
            await _WaitUntilAsync(() => Volatile.Read(ref nextCallCount) > 0, TimeSpan.FromSeconds(1));
        }
        finally
        {
            await _StopListeningAsync(listeningTask, cts);
        }
    }

    [Fact]
    public async Task should_stop_the_consume_when_pause_async()
    {
        // given
        var nextStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var fetchCanceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var consumer = Substitute.For<INatsJSConsumer>();
        _OnConsume(consumer, (_, token) => _Deliver(token, idled: nextStarted, drained: fetchCanceled));

        await using var client = new NatsConsumerClient(
            "test-group",
            1,
            _options,
            _serviceProvider,
            (_, _, _) => Task.FromResult(consumer)
        );
        await client.SubscribeAsync(["orders.created"], AbortToken);

        using var cts = new CancellationTokenSource();

        // when
        var listeningTask = client.ListeningAsync(TimeSpan.FromMilliseconds(50), cts.Token).AsTask();
        try
        {
            await nextStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
            await client.PauseAsync(AbortToken);

            // then
            await fetchCanceled.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
        }
        finally
        {
            await _StopListeningAsync(listeningTask, cts);
        }
    }

    [Fact]
    public async Task should_exit_and_report_connect_error_when_listening_async_receive_connection_fails()
    {
        // given
        var connectionFailure = new NatsConnectionFailedException("connection failed after startup");
        var failedConsumer = Substitute.For<INatsJSConsumer>();
        _OnConsume(failedConsumer, (_, _) => _Fail(connectionFailure));
        var siblingCanceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var siblingConsumer = Substitute.For<INatsJSConsumer>();
        _OnConsume(siblingConsumer, (_, token) => _Deliver(token, drained: siblingCanceled));

        await using var client = new NatsConsumerClient(
            "test-group",
            1,
            _options,
            _serviceProvider,
            (_, config, _) =>
                Task.FromResult(
                    string.Equals(config.FilterSubject, "headless.bus.orders.created", StringComparison.Ordinal)
                        ? failedConsumer
                        : siblingConsumer
                )
        );
        await client.SubscribeAsync(["orders.created", "orders.updated"], AbortToken);

        var connectError = new TaskCompletionSource<LogMessageEventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        client.OnLogCallback = args =>
        {
            if (args.LogType == MqLogType.ConnectError)
            {
                connectError.TrySetResult(args);
            }
        };

        // when
        var act = async () =>
            await client
                .ListeningAsync(TimeSpan.FromMilliseconds(50), AbortToken)
                .AsTask()
                .WaitAsync(TimeSpan.FromSeconds(5), AbortToken);

        // then
        await act.Should().ThrowAsync<NatsConnectionFailedException>();
        var logged = await connectError.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
        logged.Reason.Should().Contain("orders");
        logged.Reason.Should().Contain("connection failed after startup");
        await siblingCanceled.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
        failedConsumer
            .Received(1)
            .ConsumeAsync(
                Arg.Any<INatsDeserialize<ReadOnlyMemory<byte>>>(),
                Arg.Any<NatsJSConsumeOpts?>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_retry_protocol_timeout_without_terminating_listener_when_listening_async()
    {
        var timeProvider = new FakeTimeProvider();
        var protocolFailure = new NatsJSProtocolException(408, NatsHeaders.Messages.RequestTimeout, "Request Timeout");
        var transientLogged = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondAttempt = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var consumer = Substitute.For<INatsJSConsumer>();
        _OnConsume(
            consumer,
            (index, token) => index == 0 ? _Fail(protocolFailure) : _Deliver(token, idled: secondAttempt)
        );
        await using var client = new NatsConsumerClient(
            "test-group",
            1,
            _options,
            _serviceProvider,
            (_, _, _) => Task.FromResult(consumer),
            timeProvider: timeProvider
        )
        {
            OnLogCallback = args =>
            {
                if (
                    args.LogType == MqLogType.ExceptionReceived
                    && args.Reason?.Contains("Request Timeout", StringComparison.Ordinal) == true
                )
                {
                    transientLogged.TrySetResult();
                }
            },
        };
        await client.SubscribeAsync(["orders"], AbortToken);
        using var cts = new CancellationTokenSource();
        var listening = client.ListeningAsync(TimeSpan.FromMilliseconds(50), cts.Token).AsTask();

        try
        {
            var firstOutcome = await Task.WhenAny(transientLogged.Task, listening).WaitAsync(AbortToken);
            firstOutcome.Should().Be(transientLogged.Task, "protocol timeouts retry within the subject loop");

            timeProvider.Advance(TimeSpan.FromSeconds(2));
            await secondAttempt.Task.WaitAsync(AbortToken);
            listening.IsCompleted.Should().BeFalse();
        }
        finally
        {
            await _StopListeningAsync(listening, cts);
        }
    }

    [Fact]
    public async Task should_restart_the_consume_with_a_fresh_receive_token_when_pause_async_and_resume_async()
    {
        // given
        var startedSignals = new[]
        {
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously),
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously),
        };
        var canceledSignals = new[]
        {
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously),
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously),
        };
        var seenTokens = new List<CancellationToken>();
        var consumer = Substitute.For<INatsJSConsumer>();
        _OnConsume(
            consumer,
            (callIndex, token) =>
            {
                lock (seenTokens)
                {
                    seenTokens.Add(token);
                }

                return callIndex < startedSignals.Length
                    ? _Deliver(token, idled: startedSignals[callIndex], drained: canceledSignals[callIndex])
                    : _Deliver(token);
            }
        );

        await using var client = new NatsConsumerClient(
            "test-group",
            1,
            _options,
            _serviceProvider,
            (_, _, _) => Task.FromResult(consumer)
        );
        await client.SubscribeAsync(["orders.created"], AbortToken);

        using var cts = new CancellationTokenSource();

        // when
        var listeningTask = client.ListeningAsync(TimeSpan.FromMilliseconds(50), cts.Token).AsTask();
        try
        {
            await startedSignals[0].Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
            await client.PauseAsync(AbortToken);
            await canceledSignals[0].Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);

            await client.ResumeAsync(AbortToken);
            await startedSignals[1].Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
            await client.PauseAsync(AbortToken);
            await canceledSignals[1].Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);

            // then
            seenTokens.Should().HaveCountGreaterThanOrEqualTo(2);
            seenTokens[0].Should().NotBe(seenTokens[1]);
        }
        finally
        {
            await _StopListeningAsync(listeningTask, cts);
        }
    }

    [Fact]
    public async Task should_filter_on_the_shard_wildcard_when_the_host_contract_shards_the_subject()
    {
        // given: the host's own contract shards orders.created, and the consumer declares nothing about shards
        var contract = new MessageContractBuilder<ShardedOrder>("orders.created", "v1");
        contract.OnBus(bus => bus.UseNats(nats => nats.SubjectShard(static order => order.TenantId)));
        var metadata = Substitute.For<IMessageMetadataRegistry>();
        metadata
            .GetAll()
            .Returns([
                new MessageMetadata(
                    new MessageRouteKey(typeof(ShardedOrder), "orders.created", MessageLane.Bus),
                    typeof(ShardedOrder),
                    "v1",
                    CorrelationSelector: null,
                    contract.Build().Bus.ProviderConfigs
                ),
            ]);
        await using var services = new ServiceCollection().AddSingleton(metadata).BuildServiceProvider();

        var consumer = Substitute.For<INatsJSConsumer>();
        _OnConsume(consumer, (_, token) => _Deliver(token));
        var filters = new ConcurrentQueue<string>();

        await using var client = new NatsConsumerClient(
            "test-group",
            1,
            _options,
            services,
            (_, config, _) =>
            {
                filters.Enqueue(config.FilterSubject!);
                return Task.FromResult(consumer);
            }
        );
        await client.SubscribeAsync(["orders.created", "orders.updated"], AbortToken);

        using var cts = new CancellationTokenSource();

        // when
        var listeningTask = client.ListeningAsync(TimeSpan.FromMilliseconds(50), cts.Token).AsTask();
        try
        {
            await _WaitUntilAsync(() => filters.Count >= 3, TimeSpan.FromSeconds(5));

            // then: the sharded message is filtered on its exact subject and every shard beneath it
            filters
                .Should()
                .BeEquivalentTo(
                    "headless.bus.orders.created",
                    "headless.bus.orders.created.>",
                    "headless.bus.orders.updated"
                );
        }
        finally
        {
            await _StopListeningAsync(listeningTask, cts);
        }
    }

    [Fact]
    public async Task dispose_async_drains_in_flight_concurrent_handler_before_completing()
    {
        // given — one message delivered, then the consume idles until cancellation
        var msg = _Message();
        var consumer = Substitute.For<INatsJSConsumer>();
        _OnConsume(consumer, (_, token) => _Deliver(token, messages: msg));

        var handlerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseHandler = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var client = new NatsConsumerClient(
            "test-group",
            1, // concurrent path (groupConcurrent > 0) -> handler runs via Task.Run
            _options,
            _serviceProvider,
            (_, _, _) => Task.FromResult(consumer)
        )
        {
            OnMessageCallback = async (_, _) =>
            {
                handlerStarted.TrySetResult();
                await releaseHandler.Task;
            },
        };

        await client.SubscribeAsync(["orders.created"], AbortToken);

        using var cts = new CancellationTokenSource();
        var listeningTask = client.ListeningAsync(TimeSpan.FromMilliseconds(50), cts.Token).AsTask();

        try
        {
            await handlerStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);

            // when — dispose must drain the still-running handler before completing
            var disposeTask = client.DisposeAsync().AsTask();
            var first = await Task.WhenAny(disposeTask, Task.Delay(300, AbortToken));
            first.Should().NotBe(disposeTask, "DisposeAsync must not complete while a handler is in flight");

            // then — releasing the handler lets dispose complete
            releaseHandler.TrySetResult();
            await disposeTask.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
        }
        finally
        {
            releaseHandler.TrySetResult();
            await cts.CancelAsync();
            try
            {
                await listeningTask.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
            }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }
        }
    }

    [Fact]
    public async Task should_cap_in_flight_drain_to_remaining_shared_budget_when_shutdown_async()
    {
        var timeProvider = new FakeTimeProvider();
        var stuckHandler = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var client = new NatsConsumerClient(
            "test-group",
            1,
            _options,
            _serviceProvider,
            timeProvider: timeProvider
        );
        var inFlightHandlers = (InFlightHandlerTracker)
            typeof(NatsConsumerClient)
                .GetField(
                    "_inFlightHandlers",
                    BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly
                )!
                .GetValue(client)!;
        inFlightHandlers.Track(stuckHandler.Task);

        var shutdown = client.ShutdownAsync(TimeSpan.FromSeconds(2), AbortToken).AsTask();
        shutdown.IsCompleted.Should().BeFalse();

        timeProvider.Advance(TimeSpan.FromSeconds(2));
        await shutdown.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);

        stuckHandler.TrySetResult();
    }

    [Fact]
    public async Task should_terminate_after_max_consecutive_consume_failures_when_listening_async()
    {
        // given — every consume throws an unclassified (non-connection) error, and every rebind of the durable succeeds,
        // so only the consecutive-failure cap can stop the loop spinning in place on a non-reconnecting connection.
        var timeProvider = new FakeTimeProvider();
        var options = MsOptions.Options.Create(
            new NatsMessagingOptions { Servers = "nats://localhost:4222", MaxConsecutiveConsumeFailures = 2 }
        );

        var firstFailureLogged = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var terminationLogged = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var consumer = Substitute.For<INatsJSConsumer>();
        _OnConsume(consumer, (_, _) => _Fail(new InvalidOperationException("boom")));

        await using var client = new NatsConsumerClient(
            "test-group",
            1,
            options,
            _serviceProvider,
            (_, _, _) => Task.FromResult(consumer),
            timeProvider: timeProvider
        )
        {
            OnLogCallback = args =>
            {
                if (
                    args.LogType == MqLogType.ExceptionReceived
                    && args.Reason?.Contains("boom", StringComparison.Ordinal) == true
                )
                {
                    firstFailureLogged.TrySetResult();
                }

                if (
                    args.LogType == MqLogType.ConnectError
                    && args.Reason?.Contains("consecutively", StringComparison.Ordinal) == true
                )
                {
                    terminationLogged.TrySetResult();
                }
            },
        };
        await client.SubscribeAsync(["orders"], AbortToken);
        using var cts = new CancellationTokenSource();

        var listening = client.ListeningAsync(TimeSpan.FromMilliseconds(50), cts.Token).AsTask();
        try
        {
            // when
            await firstFailureLogged.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
            timeProvider.Advance(TimeSpan.FromSeconds(5)); // release the backoff so the second consume runs
            await terminationLogged.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);

            // then — the second consecutive failure escalates to a supervised-restart termination
            var act = async () => await listening.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
            await act.Should()
                .ThrowAsync<BrokerConnectionException>()
                .WithInnerException<BrokerConnectionException, InvalidOperationException>();
        }
        finally
        {
            await _StopListeningIgnoringOutcomeAsync(listening, cts);
        }
    }

    [Fact]
    public async Task should_reset_failure_count_and_backoff_after_a_delivered_message_when_listening_async()
    {
        // given — fail, deliver a message then fail: with a reset on the delivered message the streak never reaches
        // the cap of 2, so the listener must keep running instead of terminating.
        var timeProvider = new FakeTimeProvider();
        var options = MsOptions.Options.Create(
            new NatsMessagingOptions { Servers = "nats://localhost:4222", MaxConsecutiveConsumeFailures = 2 }
        );

        var firstFailureLogged = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondFailureLogged = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var idled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var prematureTermination = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var consumer = Substitute.For<INatsJSConsumer>();
        _OnConsume(
            consumer,
            (index, token) =>
                index switch
                {
                    0 => _Fail(new InvalidOperationException("boom-1")),
                    1 => _Fail(new InvalidOperationException("boom-2"), _Message()),
                    _ => _Deliver(token, idled: idled),
                }
        );

        await using var client = new NatsConsumerClient(
            "test-group",
            1,
            options,
            _serviceProvider,
            (_, _, _) => Task.FromResult(consumer),
            timeProvider: timeProvider
        )
        {
            OnMessageCallback = (_, _) => Task.CompletedTask,
            OnLogCallback = args =>
            {
                if (args.LogType == MqLogType.ExceptionReceived)
                {
                    if (args.Reason?.Contains("boom-1", StringComparison.Ordinal) == true)
                    {
                        firstFailureLogged.TrySetResult();
                    }
                    else if (args.Reason?.Contains("boom-2", StringComparison.Ordinal) == true)
                    {
                        secondFailureLogged.TrySetResult();
                    }
                }

                if (
                    args.LogType == MqLogType.ConnectError
                    && args.Reason?.Contains("consecutively", StringComparison.Ordinal) == true
                )
                {
                    prematureTermination.TrySetResult();
                }
            },
        };
        await client.SubscribeAsync(["orders"], AbortToken);
        using var cts = new CancellationTokenSource();

        var listening = client.ListeningAsync(TimeSpan.FromMilliseconds(50), cts.Token).AsTask();
        try
        {
            // when — first failure, then release its backoff so the delivered message and second failure run
            await firstFailureLogged.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
            timeProvider.Advance(TimeSpan.FromSeconds(5));
            await secondFailureLogged.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
            timeProvider.Advance(TimeSpan.FromSeconds(2));

            // then — the loop reached the idle third consume after only the initial backoff, proving both the
            // failure streak and the retry delay reset on the delivered message
            await idled.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
            prematureTermination.Task.IsCompleted.Should().BeFalse("the streak reset on the delivered message");
            listening.IsCompleted.Should().BeFalse();
        }
        finally
        {
            await _StopListeningAsync(listening, cts);
        }
    }

    [Fact]
    public async Task should_terminate_after_max_consecutive_consumer_bind_failures_when_listening_async()
    {
        var timeProvider = new FakeTimeProvider();
        var options = MsOptions.Options.Create(
            new NatsMessagingOptions { Servers = "nats://localhost:4222", MaxConsecutiveConsumeFailures = 2 }
        );
        var firstFailureLogged = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var terminationLogged = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var bindFailure = new InvalidOperationException("bind failed");

        await using var client = new NatsConsumerClient(
            "test-group",
            1,
            options,
            _serviceProvider,
            (_, _, _) => Task.FromException<INatsJSConsumer>(bindFailure),
            timeProvider: timeProvider
        )
        {
            OnLogCallback = args =>
            {
                if (
                    args.LogType == MqLogType.ExceptionReceived
                    && args.Reason?.Contains("bind failed", StringComparison.Ordinal) == true
                )
                {
                    firstFailureLogged.TrySetResult();
                }

                if (
                    args.LogType == MqLogType.ConnectError
                    && args.Reason?.Contains("consecutively", StringComparison.Ordinal) == true
                )
                {
                    terminationLogged.TrySetResult();
                }
            },
        };
        await client.SubscribeAsync(["orders"], AbortToken);
        using var cts = new CancellationTokenSource();

        var listening = client.ListeningAsync(TimeSpan.FromMilliseconds(50), cts.Token).AsTask();
        try
        {
            await firstFailureLogged.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
            timeProvider.Advance(TimeSpan.FromSeconds(2));
            await terminationLogged.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);

            var act = async () => await listening.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
            await act.Should()
                .ThrowAsync<BrokerConnectionException>()
                .WithInnerException<BrokerConnectionException, InvalidOperationException>();
            listening.IsCompleted.Should().BeTrue("the startup failure must fault the listener");
        }
        finally
        {
            await _StopListeningIgnoringOutcomeAsync(listening, cts);
        }
    }

    [Fact]
    public async Task should_apply_tuned_consumer_limits_over_consumer_options_and_keep_the_provider_identity()
    {
        // given — a host-wide ConsumerOptions callback and a consumer tuned with its own limits
        var options = MsOptions.Options.Create(
            new NatsMessagingOptions
            {
                Servers = "nats://localhost:4222",
                ConsumerOptions = config =>
                {
                    config.AckWait = TimeSpan.FromSeconds(10);
                    config.MaxAckPending = 10;
                    config.NumReplicas = 3;
                },
            }
        );
        var registry = Substitute.For<IConsumerRegistry>();
        registry
            .GetAll()
            .Returns([
                new ConsumerMetadata(
                    typeof(object),
                    typeof(object),
                    "orders.created",
                    1,
                    MessageLane.Bus,
                    "orders-projection",
                    "v1"
                )
                {
                    ProviderConfigs = new Dictionary<Type, object>
                    {
                        [typeof(NatsConsumerConfig)] = new NatsConsumerConfig(
                            IsSharded: false,
                            AckWait: TimeSpan.FromMinutes(2),
                            MaxDeliver: 5,
                            InactiveThreshold: TimeSpan.FromDays(1)
                        ),
                    },
                },
            ]);
        await using var services = new ServiceCollection().AddSingleton(registry).BuildServiceProvider();

        var consumer = Substitute.For<INatsJSConsumer>();
        _OnConsume(consumer, (_, token) => _Deliver(token));
        var bound = new TaskCompletionSource<NATS.Client.JetStream.Models.ConsumerConfig>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        await using var client = new NatsConsumerClient(
            "orders-projection",
            1,
            options,
            services,
            (_, config, _) =>
            {
                bound.TrySetResult(config);
                return Task.FromResult(consumer);
            }
        );
        await client.SubscribeAsync(["orders.created"], AbortToken);

        using var cts = new CancellationTokenSource();

        // when
        var listeningTask = client.ListeningAsync(TimeSpan.FromMilliseconds(50), cts.Token).AsTask();
        try
        {
            var config = await bound.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);

            // then — the consumer's own limits win, the callback fills the rest, and the identity stays the provider's
            config.AckWait.Should().Be(TimeSpan.FromMinutes(2));
            config.MaxDeliver.Should().Be(5);
            config.InactiveThreshold.Should().Be(TimeSpan.FromDays(1));
            config.MaxAckPending.Should().Be(10);
            config.NumReplicas.Should().Be(3);
            config.FilterSubject.Should().Be("headless.bus.orders.created");
            config.DeliverPolicy.Should().Be(NATS.Client.JetStream.Models.ConsumerConfigDeliverPolicy.New);
        }
        finally
        {
            await _StopListeningAsync(listeningTask, cts);
        }
    }

    [Theory]
    [InlineData(0, 1, 0)]
    [InlineData(1, 1, 0)]
    [InlineData(8, 8, 4)]
    public void should_never_prefetch_more_messages_than_the_handlers_take_at_once(
        int concurrency,
        int maxMsgs,
        int thresholdMsgs
    )
    {
        var opts = NatsConsumerClient.BuildConsumeOpts(concurrency, notificationHandler: null);

        opts.MaxMsgs.Should().Be(maxMsgs);
        opts.ThresholdMsgs.Should().Be(thresholdMsgs);
        opts.Expires.Should().Be(TimeSpan.FromSeconds(30));
        opts.IdleHeartbeat.Should().Be(TimeSpan.FromSeconds(5));
        opts.DrainOnCancel.Should().BeTrue();
    }

    [Fact]
    public async Task should_terminate_for_a_rebuild_when_pulls_keep_missing_their_heartbeats()
    {
        // given — NATS.Net reports missed heartbeats and keeps re-pulling, so the consume itself never fails
        var options = MsOptions.Options.Create(
            new NatsMessagingOptions { Servers = "nats://localhost:4222", MaxConsecutiveConsumeFailures = 2 }
        );
        var consumer = Substitute.For<INatsJSConsumer>();
        consumer
            .ConsumeAsync(
                Arg.Any<INatsDeserialize<ReadOnlyMemory<byte>>>(),
                Arg.Any<NatsJSConsumeOpts?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call => _MissHeartbeats(call.Arg<NatsJSConsumeOpts?>()!, call.Arg<CancellationToken>()));

        await using var client = new NatsConsumerClient(
            "test-group",
            1,
            options,
            _serviceProvider,
            (_, _, _) => Task.FromResult(consumer)
        )
        {
            OnLogCallback = _ => { },
        };
        await client.SubscribeAsync(["orders"], AbortToken);
        using var cts = new CancellationTokenSource();

        // when
        var listening = client.ListeningAsync(TimeSpan.FromMilliseconds(50), cts.Token).AsTask();
        try
        {
            // then — the second missed heartbeat trips the cap, as a consume failure would
            var act = async () => await listening.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
            await act.Should()
                .ThrowAsync<BrokerConnectionException>()
                .WithInnerException<BrokerConnectionException, TimeoutException>();
        }
        finally
        {
            await _StopListeningIgnoringOutcomeAsync(listening, cts);
        }
    }

    [Fact]
    public async Task should_hand_back_a_message_drained_after_pause_without_dispatching_it()
    {
        // given — a message NATS.Net had buffered arrives after the pause cancelled the consume
        var msg = _Message();
        var consumeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        msg.NakAsync(Arg.Any<AckOpts?>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                released.TrySetResult();
                return ValueTask.CompletedTask;
            });
        var consumer = Substitute.For<INatsJSConsumer>();
        _OnConsume(
            consumer,
            (index, token) => index == 0 ? _DeliverAfterCancel(msg, consumeStarted, token) : _Deliver(token)
        );

        var dispatched = false;
        await using var client = new NatsConsumerClient(
            "test-group",
            1,
            _options,
            _serviceProvider,
            (_, _, _) => Task.FromResult(consumer)
        )
        {
            OnMessageCallback = (_, _) =>
            {
                dispatched = true;
                return Task.CompletedTask;
            },
            OnLogCallback = _ => { },
        };
        await client.SubscribeAsync(["orders.created"], AbortToken);

        using var cts = new CancellationTokenSource();
        var listeningTask = client.ListeningAsync(TimeSpan.FromMilliseconds(50), cts.Token).AsTask();
        try
        {
            await consumeStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);

            // when
            await client.PauseAsync(AbortToken);
            await released.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);

            // then — released at once with a plain NAK, so it redelivers now rather than after AckWait
            await msg.Received(1).NakAsync(null, CancellationToken.None);
            dispatched.Should().BeFalse();
        }
        finally
        {
            await _StopListeningAsync(listeningTask, cts);
        }
    }

    [Fact]
    public async Task should_report_a_running_delivery_in_progress_until_it_settles()
    {
        // given — a handler that outlasts half the default 30 s AckWait
        var timeProvider = new FakeTimeProvider();
        var msg = _Message();
        var progressCount = 0;
        msg.AckProgressAsync(Arg.Any<AckOpts?>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                Interlocked.Increment(ref progressCount);
                return ValueTask.CompletedTask;
            });
        var consumer = Substitute.For<INatsJSConsumer>();
        _OnConsume(consumer, (_, token) => _Deliver(token, messages: msg));

        var handlerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseHandler = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var settled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        NatsConsumerClient? client = null;
        await using var owned = new NatsConsumerClient(
            "test-group",
            0,
            _options,
            _serviceProvider,
            (_, _, _) => Task.FromResult(consumer),
            timeProvider: timeProvider
        )
        {
            OnMessageCallback = async (_, sender) =>
            {
                handlerStarted.TrySetResult();
                await releaseHandler.Task;
                await client!.CommitAsync(sender, CancellationToken.None);
                settled.TrySetResult();
            },
            OnLogCallback = _ => { },
        };
        client = owned;
        await client.SubscribeAsync(["orders.created"], AbortToken);

        using var cts = new CancellationTokenSource();
        var listeningTask = client.ListeningAsync(TimeSpan.FromMilliseconds(50), cts.Token).AsTask();
        try
        {
            await handlerStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);

            // when — the clock passes half the AckWait while the handler runs
            await _AdvanceUntilAsync(timeProvider, () => Volatile.Read(ref progressCount) > 0);

            releaseHandler.TrySetResult();
            await settled.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
            var progressAtSettlement = Volatile.Read(ref progressCount);
            timeProvider.Advance(TimeSpan.FromMinutes(2));
            await Task.Delay(100, AbortToken);

            // then — progress stopped with the acknowledgement, which reached the delivery behind the token
            Volatile.Read(ref progressCount).Should().Be(progressAtSettlement);
            await msg.Received(1).AckAsync(Arg.Any<AckOpts?>(), Arg.Any<CancellationToken>());
        }
        finally
        {
            releaseHandler.TrySetResult();
            await _StopListeningAsync(listeningTask, cts);
        }
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("2.9.25", false)]
    [InlineData("2.10.3", false)]
    [InlineData("2.10.4", true)]
    [InlineData("2.11.0-beta.2", true)]
    [InlineData("not-a-version", false)]
    public void should_send_a_terminate_reason_only_to_a_server_that_parses_it(string? serverVersion, bool sent)
    {
        var reason = NatsConsumerClient.TerminateReason(serverVersion, new InvalidDataException());

        if (sent)
        {
            reason.Should().Be("malformed headless envelope: InvalidDataException");
        }
        else
        {
            reason.Should().BeNull();
        }
    }

    // Reports a missed heartbeat on every pull, as NATS.Net does when the server goes silent, and ends with whatever the
    // notification handler throws.
    private static async IAsyncEnumerable<INatsJSMsg<ReadOnlyMemory<byte>>> _MissHeartbeats(
        NatsJSConsumeOpts opts,
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        // The cap under test is 2, so the handler throws long before the pulls run out.
        for (var pull = 0; pull < 10; pull++)
        {
            await opts.NotificationHandler!(NatsJSTimeoutNotification.Default, cancellationToken);
        }

        yield break;
    }

    // Idles until the consume is cancelled, then yields one message it had buffered, as a draining consume does.
    private static async IAsyncEnumerable<INatsJSMsg<ReadOnlyMemory<byte>>> _DeliverAfterCancel(
        INatsJSMsg<ReadOnlyMemory<byte>> buffered,
        TaskCompletionSource started,
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        started.TrySetResult();

        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Draining: the buffered message still comes through.
        }

        yield return buffered;
    }

    // Steps the fake clock until the condition holds, because the loop under test registers its timer asynchronously.
    private static async Task _AdvanceUntilAsync(FakeTimeProvider timeProvider, Func<bool> condition)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
        cts.CancelAfter(TimeSpan.FromSeconds(5));

        while (!condition())
        {
            timeProvider.Advance(TimeSpan.FromSeconds(1));
            await Task.Delay(10, cts.Token);
        }
    }

    [Fact]
    public async Task should_cancel_each_receive_lease_with_its_own_token_when_leases_overlap()
    {
        // given — two listeners hold leases at once, each linked to its own stopping token
        await using var client = _CreateClient("test-group");
        using var firstStop = new CancellationTokenSource();
        using var secondStop = new CancellationTokenSource();
        using var first = client.AcquireReceiveLease(firstStop.Token);
        using var second = client.AcquireReceiveLease(secondStop.Token);

        // when
        await firstStop.CancelAsync();

        // then — the earlier lease still cancels, though a later one linked another token
        first.Token.IsCancellationRequested.Should().BeTrue();
        second.Token.IsCancellationRequested.Should().BeFalse();
    }

    [Fact]
    public async Task should_cancel_every_held_receive_lease_when_pause_async()
    {
        await using var client = _CreateClient("test-group");
        using var stop = new CancellationTokenSource();
        using var lease = client.AcquireReceiveLease(stop.Token);

        await client.PauseAsync(AbortToken);

        lease.Token.IsCancellationRequested.Should().BeTrue();
    }

    // Stands in for INatsJSConsumer.ConsumeAsync: each call is one consume, given its zero-based index and its token.
    private static void _OnConsume(
        INatsJSConsumer consumer,
        Func<int, CancellationToken, IAsyncEnumerable<INatsJSMsg<ReadOnlyMemory<byte>>>> consume
    )
    {
        var calls = 0;
        consumer
            .ConsumeAsync(
                Arg.Any<INatsDeserialize<ReadOnlyMemory<byte>>>(),
                Arg.Any<NatsJSConsumeOpts?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call => consume(Interlocked.Increment(ref calls) - 1, call.Arg<CancellationToken>()));
    }

    // Yields the messages, then idles like an empty pull until the token cancels, and ends quietly as a consume with
    // DrainOnCancel does.
    private static async IAsyncEnumerable<INatsJSMsg<ReadOnlyMemory<byte>>> _Deliver(
        [EnumeratorCancellation] CancellationToken cancellationToken,
        TaskCompletionSource? idled = null,
        TaskCompletionSource? drained = null,
        params INatsJSMsg<ReadOnlyMemory<byte>>[] messages
    )
    {
        foreach (var message in messages)
        {
            yield return message;
        }

        idled?.TrySetResult();

        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            drained?.TrySetResult();
        }
    }

    // Yields the messages, then fails the consume.
    private static async IAsyncEnumerable<INatsJSMsg<ReadOnlyMemory<byte>>> _Fail(
        Exception failure,
        params INatsJSMsg<ReadOnlyMemory<byte>>[] messages
    )
    {
        foreach (var message in messages)
        {
            yield return message;
        }

        await Task.FromException(failure);
    }

    private static INatsJSMsg<ReadOnlyMemory<byte>> _Message(NatsHeaders? headers = null)
    {
        var msg = Substitute.For<INatsJSMsg<ReadOnlyMemory<byte>>>();
        msg.Data.Returns(new ReadOnlyMemory<byte>("test"u8.ToArray()));
        msg.Headers.Returns(headers ?? _CreateHeaders());
        return msg;
    }

    private static NatsHeaders _CreateHeaders()
    {
        return new NatsHeaders { { Headers.MessageId, "msg-1" }, { Headers.MessageName, "TestEvent" } };
    }

    private NatsConsumerClient _CreateClient(string groupName, byte groupConcurrent = 1)
    {
        return new NatsConsumerClient(groupName, groupConcurrent, _options, _serviceProvider);
    }

    private static async Task _WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
        cts.CancelAfter(timeout);

        while (!condition())
        {
            cts.Token.ThrowIfCancellationRequested();
            await Task.Delay(20, cts.Token);
        }
    }

    private async Task _StopListeningAsync(Task listeningTask, CancellationTokenSource cts)
    {
        await cts.CancelAsync();

        try
        {
            await listeningTask.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            // Normal shutdown.
        }
    }

    // Awaits the listening task within the using scope (so the resource-lifetime analyzer is satisfied) but
    // observes any fault instead of re-throwing — used when the test has already asserted the terminal fault.
    private static async Task _StopListeningIgnoringOutcomeAsync(Task listeningTask, CancellationTokenSource cts)
    {
        await cts.CancelAsync();
        await listeningTask.ContinueWith(
            static _ => { },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default
        );
    }

    private sealed record ShardedOrder(string TenantId);
}
