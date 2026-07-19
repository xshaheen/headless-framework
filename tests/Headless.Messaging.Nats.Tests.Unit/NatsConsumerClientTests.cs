// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Reflection;
using Headless.Messaging;
using Headless.Messaging.Exceptions;
using Headless.Messaging.Nats;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using NATS.Client.Core;
using NATS.Client.JetStream;
using MsOptions = Microsoft.Extensions.Options;

#pragma warning disable MA0045 // Do not use blocking calls, even when the calling method must become async
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
                EnableSubscriberClientStreamAndSubjectCreation = false,
            }
        );
        await using var client = new NatsConsumerClient("test-group", 1, options, _serviceProvider);
        var messageNames = new[] { "topic1", "topic2", "topic3" };

        var result = await client.FetchMessageNamesAsync(messageNames, AbortToken);
        result.Should().BeEquivalentTo(messageNames);
    }

    [Fact]
    public void should_use_exact_subject_for_unsharded_topic_when_build_stream_subjects()
    {
        NatsConsumerClient
            .BuildStreamSubjects(["orders"], new HashSet<string>(StringComparer.Ordinal))
            .Should()
            .BeEquivalentTo(["orders"]);
    }

    [Fact]
    public void should_add_wildcard_only_for_sharded_message_names_when_build_stream_subjects()
    {
        NatsConsumerClient
            .BuildStreamSubjects(["orders.created"], new HashSet<string>(StringComparer.Ordinal) { "orders.created" })
            .Should()
            .BeEquivalentTo(["orders.created", "orders.created.>"]);
    }

    [Fact]
    public void should_mix_sharded_and_unsharded_subjects_precisely_when_build_stream_subjects()
    {
        NatsConsumerClient
            .BuildStreamSubjects(
                ["orders", "orders.created"],
                new HashSet<string>(StringComparer.Ordinal) { "orders.created" }
            )
            .Should()
            .BeEquivalentTo(["orders", "orders.created", "orders.created.>"]);
    }

    [Fact]
    public void should_deduplicate_topics_when_build_stream_subjects()
    {
        NatsConsumerClient
            .BuildStreamSubjects(
                ["orders.created", "orders.created"],
                new HashSet<string>(StringComparer.Ordinal) { "orders.created" }
            )
            .Should()
            .BeEquivalentTo(["orders.created", "orders.created.>"]);
    }

    [Fact]
    public void should_preserve_non_prefix_topics_without_wildcard_when_build_stream_subjects_unsharded()
    {
        NatsConsumerClient
            .BuildStreamSubjects(["orders.created"], new HashSet<string>(StringComparer.Ordinal))
            .Should()
            .BeEquivalentTo(["orders.created"]);
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
    public void should_include_group_for_bus_intent_when_build_durable_name()
    {
        NatsConsumerClient
            .BuildDurableName("payments", "orders.created", IntentType.Bus)
            .Should()
            .Be("payments-orders_created");
    }

    [Fact]
    public void should_share_destination_for_queue_intent_when_build_durable_name()
    {
        NatsConsumerClient
            .BuildDurableName("payments", "orders.created", IntentType.Queue)
            .Should()
            .Be("queue-orders_created");
    }

    // NextBackoff tests
    //
    // NextBackoff subtracts up to 25% jitter from the doubled/capped value (never adds), so every assertion
    // here checks a [0.75x, 1x] range against the ideal (unjittered) value rather than an exact TimeSpan —
    // the prior exact-value assertions were flaky by construction since the function always applies jitter.

    [Fact]
    public void should_double_current_delay_within_jitter_budget_when_next_backoff()
    {
        var result = NatsConsumerClient.NextBackoff(TimeSpan.FromSeconds(1));
        result.Should().BeLessThanOrEqualTo(TimeSpan.FromSeconds(2));
        result.Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(1.5));
    }

    [Fact]
    public void should_cap_at_30_seconds_within_jitter_budget_when_next_backoff()
    {
        var result = NatsConsumerClient.NextBackoff(TimeSpan.FromSeconds(20));
        result.Should().BeLessThanOrEqualTo(TimeSpan.FromSeconds(30));
        result.Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(22.5));
    }

    [Fact]
    public void should_not_exceed_ceiling_even_with_large_input_when_next_backoff()
    {
        var result = NatsConsumerClient.NextBackoff(TimeSpan.FromSeconds(60));
        result.Should().BeLessThanOrEqualTo(TimeSpan.FromSeconds(30));
        result.Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(22.5));
    }

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
    public void should_not_enforce_floor_when_next_backoff_next_is_above()
    {
        var result = NatsConsumerClient.NextBackoff(TimeSpan.FromSeconds(5), floor: TimeSpan.FromSeconds(5));
        result.Should().BeLessThanOrEqualTo(TimeSpan.FromSeconds(10));
        result.Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(7.5));
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
    public void should_never_exceed_the_30_second_ceiling_across_many_iterations_and_inputs_when_next_backoff()
    {
        TimeSpan[] inputs =
        [
            TimeSpan.FromSeconds(20),
            TimeSpan.FromSeconds(30),
            TimeSpan.FromSeconds(45),
            TimeSpan.FromSeconds(60),
            TimeSpan.FromMinutes(10),
        ];
        var ceiling = TimeSpan.FromSeconds(30);

        foreach (var input in inputs)
        {
            for (var i = 0; i < 1000; i++)
            {
                NatsConsumerClient.NextBackoff(input).Should().BeLessThanOrEqualTo(ceiling);
            }
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
    public async Task should_nak_valid_nats_message_when_reject_async()
    {
        await using var client = _CreateClient("test-group");
        var msg = Substitute.For<INatsJSMsg<ReadOnlyMemory<byte>>>();

        await client.RejectAsync(msg, AbortToken);

        await msg.Received(1).NakAsync(cancellationToken: Arg.Any<CancellationToken>());
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
        msg.NakAsync(cancellationToken: Arg.Any<CancellationToken>())
            .Returns(x => throw new InvalidOperationException("nak failed"));

        await client.RejectAsync(msg, AbortToken);

        loggedArgs.Should().NotBeNull();
        loggedArgs!.LogType.Should().Be(MqLogType.AsyncErrorEvent);
        loggedArgs.Reason.Should().Contain("nak failed");
    }

    [Fact]
    public async Task should_nak_when_custom_headers_builder_throws()
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
        msg.Headers.Returns((NatsHeaders?)null);

        var callbackInvoked = false;
        var nakCalled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var consumer = Substitute.For<INatsJSConsumer>();
        var callCount = 0;
        consumer
            .NextAsync(
                Arg.Any<INatsDeserialize<ReadOnlyMemory<byte>>>(),
                Arg.Any<NatsJSNextOpts?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
            {
                var token = call.Arg<CancellationToken>();
                if (Interlocked.Increment(ref callCount) == 1)
                {
                    return new ValueTask<INatsJSMsg<ReadOnlyMemory<byte>>?>(msg);
                }

                // Block until cancellation — throws OCE which the consumer loop handles
                return new ValueTask<INatsJSMsg<ReadOnlyMemory<byte>>?>(
                    Task.Delay(Timeout.InfiniteTimeSpan, token)
                        .ContinueWith<INatsJSMsg<ReadOnlyMemory<byte>>?>(
                            static (t, _) =>
                            {
                                t.GetAwaiter().GetResult(); // propagate OCE
                                return null;
                            },
                            null,
                            CancellationToken.None,
                            TaskContinuationOptions.ExecuteSynchronously,
                            TaskScheduler.Default
                        )
                );
            });

        msg.NakAsync(cancellationToken: Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                nakCalled.TrySetResult();
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
            await nakCalled.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);

            // then — message should be nacked, callback should not be invoked
            callbackInvoked.Should().BeFalse();
            await msg.Received(1).NakAsync(cancellationToken: Arg.Any<CancellationToken>());
            loggedArgs.Should().NotBeNull();
            loggedArgs!.Reason.Should().Contain("bad header builder");
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
        consumer
            .NextAsync(
                Arg.Any<INatsDeserialize<ReadOnlyMemory<byte>>>(),
                Arg.Any<NatsJSNextOpts?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(_ =>
            {
                Interlocked.Increment(ref nextCallCount);
                return new ValueTask<INatsJSMsg<ReadOnlyMemory<byte>>?>((INatsJSMsg<ReadOnlyMemory<byte>>?)null);
            });

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
    public async Task should_cancel_inflight_fetch_when_pause_async()
    {
        // given
        var nextStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var fetchCanceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var consumer = Substitute.For<INatsJSConsumer>();
        consumer
            .NextAsync(
                Arg.Any<INatsDeserialize<ReadOnlyMemory<byte>>>(),
                Arg.Any<NatsJSNextOpts?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(async call =>
            {
                var token = call.Arg<CancellationToken>();
                nextStarted.TrySetResult();

                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                    return null;
                }
                catch (OperationCanceledException)
                {
                    fetchCanceled.TrySetResult();
                    throw;
                }
            });

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
        failedConsumer
            .NextAsync(
                Arg.Any<INatsDeserialize<ReadOnlyMemory<byte>>>(),
                Arg.Any<NatsJSNextOpts?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(_ => new ValueTask<INatsJSMsg<ReadOnlyMemory<byte>>?>(
                Task.FromException<INatsJSMsg<ReadOnlyMemory<byte>>?>(connectionFailure)
            ));
        var siblingCanceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var siblingConsumer = Substitute.For<INatsJSConsumer>();
        siblingConsumer
            .NextAsync(
                Arg.Any<INatsDeserialize<ReadOnlyMemory<byte>>>(),
                Arg.Any<NatsJSNextOpts?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(async call =>
            {
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, call.Arg<CancellationToken>());
                    return null;
                }
                catch (OperationCanceledException)
                {
                    siblingCanceled.TrySetResult();
                    throw;
                }
            });

        await using var client = new NatsConsumerClient(
            "test-group",
            1,
            _options,
            _serviceProvider,
            (_, config, _) =>
                Task.FromResult(
                    string.Equals(config.FilterSubject, "orders.created", StringComparison.Ordinal)
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
        await failedConsumer
            .Received(1)
            .NextAsync(
                Arg.Any<INatsDeserialize<ReadOnlyMemory<byte>>>(),
                Arg.Any<NatsJSNextOpts?>(),
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
        var callCount = 0;
        consumer
            .NextAsync(
                Arg.Any<INatsDeserialize<ReadOnlyMemory<byte>>>(),
                Arg.Any<NatsJSNextOpts?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
            {
                if (Interlocked.Increment(ref callCount) == 1)
                {
                    return ValueTask.FromException<INatsJSMsg<ReadOnlyMemory<byte>>?>(protocolFailure);
                }

                secondAttempt.TrySetResult();
                return new ValueTask<INatsJSMsg<ReadOnlyMemory<byte>>?>(
                    Task.Delay(Timeout.InfiniteTimeSpan, call.Arg<CancellationToken>())
                        .ContinueWith<INatsJSMsg<ReadOnlyMemory<byte>>?>(
                            static task =>
                            {
                                task.GetAwaiter().GetResult();
                                return null;
                            },
                            CancellationToken.None,
                            TaskContinuationOptions.ExecuteSynchronously,
                            TaskScheduler.Default
                        )
                );
            });
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
    public async Task should_restart_fetch_with_a_fresh_receive_token_when_pause_async_and_resume_async()
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
        var nextCallCount = 0;
        var consumer = Substitute.For<INatsJSConsumer>();
        consumer
            .NextAsync(
                Arg.Any<INatsDeserialize<ReadOnlyMemory<byte>>>(),
                Arg.Any<NatsJSNextOpts?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(async call =>
            {
                var callIndex = Interlocked.Increment(ref nextCallCount) - 1;
                var token = call.Arg<CancellationToken>();

                lock (seenTokens)
                {
                    seenTokens.Add(token);
                }

                if (callIndex < startedSignals.Length)
                {
                    startedSignals[callIndex].TrySetResult();
                }

                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                    return null;
                }
                catch (OperationCanceledException)
                {
                    if (callIndex < canceledSignals.Length)
                    {
                        canceledSignals[callIndex].TrySetResult();
                    }

                    throw;
                }
            });

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
    public void build_stream_subjects_and_build_consumer_subjects_agree_for_duplicate_sharded_names()
    {
        // A sharded message name appearing more than once (e.g. two consumers of the same type) must
        // yield the same {base, base.>} set from both builders: the JetStream stream config and the
        // consumer FilterSubjects have to cover identical subjects or sharded messages are dropped.
        var sharded = new HashSet<string>(StringComparer.Ordinal) { "orders" };
        string[] names = ["orders", "orders"];

        var streamSubjects = NatsConsumerClient.BuildStreamSubjects(names, sharded);
        var consumerSubjects = NatsConsumerClient.BuildConsumerSubjects(names, sharded);

        streamSubjects.Should().Equal("orders", "orders.>");
        consumerSubjects.Should().Equal("orders", "orders.>");
    }

    [Fact]
    public async Task dispose_async_drains_in_flight_concurrent_handler_before_completing()
    {
        // given — one message delivered, then NextAsync blocks until cancellation
        var msg = Substitute.For<INatsJSMsg<ReadOnlyMemory<byte>>>();
        msg.Data.Returns(new ReadOnlyMemory<byte>("test"u8.ToArray()));
        msg.Headers.Returns((NatsHeaders?)null);

        var delivered = 0;
        var consumer = Substitute.For<INatsJSConsumer>();
        consumer
            .NextAsync(
                Arg.Any<INatsDeserialize<ReadOnlyMemory<byte>>>(),
                Arg.Any<NatsJSNextOpts?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
            {
                var token = call.Arg<CancellationToken>();
                if (Interlocked.Increment(ref delivered) == 1)
                {
                    return new ValueTask<INatsJSMsg<ReadOnlyMemory<byte>>?>(msg);
                }

                return new ValueTask<INatsJSMsg<ReadOnlyMemory<byte>>?>(
                    Task.Delay(Timeout.InfiniteTimeSpan, token)
                        .ContinueWith<INatsJSMsg<ReadOnlyMemory<byte>>?>(
                            static (t, _) =>
                            {
                                t.GetAwaiter().GetResult();
                                return null;
                            },
                            null,
                            CancellationToken.None,
                            TaskContinuationOptions.ExecuteSynchronously,
                            TaskScheduler.Default
                        )
                );
            });

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
        var inFlightHandlers =
            (ConcurrentDictionary<Task, byte>)
                typeof(NatsConsumerClient)
                    .GetField(
                        "_inFlightHandlers",
                        BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly
                    )!
                    .GetValue(client)!;
        inFlightHandlers.TryAdd(stuckHandler.Task, 0).Should().BeTrue();

        var shutdown = ((IConsumerClient)client).ShutdownAsync(TimeSpan.FromSeconds(2), AbortToken).AsTask();
        shutdown.IsCompleted.Should().BeFalse();

        timeProvider.Advance(TimeSpan.FromSeconds(2));
        await shutdown.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);

        stuckHandler.TrySetResult();
    }

    [Fact]
    public async Task should_terminate_after_max_consecutive_consume_failures_when_listening_async()
    {
        // given — every fetch throws an unclassified (non-connection) error, so only the consecutive-failure
        // cap can stop the loop spinning in place on a non-reconnecting connection.
        var timeProvider = new FakeTimeProvider();
        var options = MsOptions.Options.Create(
            new NatsMessagingOptions { Servers = "nats://localhost:4222", MaxConsecutiveConsumeFailures = 2 }
        );

        var firstFailureLogged = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var terminationLogged = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var consumer = Substitute.For<INatsJSConsumer>();
        consumer
            .NextAsync(
                Arg.Any<INatsDeserialize<ReadOnlyMemory<byte>>>(),
                Arg.Any<NatsJSNextOpts?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(_ =>
                ValueTask.FromException<INatsJSMsg<ReadOnlyMemory<byte>>?>(new InvalidOperationException("boom"))
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
            timeProvider.Advance(TimeSpan.FromSeconds(5)); // release the backoff so the second fetch runs
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
    public async Task should_reset_failure_count_and_backoff_after_a_successful_fetch_when_listening_async()
    {
        // given — fail, succeed, fail: with a reset on the successful heartbeat the streak never reaches the
        // cap of 2, so the listener must keep running instead of terminating.
        var timeProvider = new FakeTimeProvider();
        var options = MsOptions.Options.Create(
            new NatsMessagingOptions { Servers = "nats://localhost:4222", MaxConsecutiveConsumeFailures = 2 }
        );

        var firstFailureLogged = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondFailureLogged = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var idled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var prematureTermination = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var callCount = 0;
        var consumer = Substitute.For<INatsJSConsumer>();
        consumer
            .NextAsync(
                Arg.Any<INatsDeserialize<ReadOnlyMemory<byte>>>(),
                Arg.Any<NatsJSNextOpts?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
                Interlocked.Increment(ref callCount) switch
                {
                    1 => ValueTask.FromException<INatsJSMsg<ReadOnlyMemory<byte>>?>(
                        new InvalidOperationException("boom-1")
                    ),
                    // a returned heartbeat (null) is a successful fetch that resets the streak
                    2 => new ValueTask<INatsJSMsg<ReadOnlyMemory<byte>>?>((INatsJSMsg<ReadOnlyMemory<byte>>?)null),
                    3 => ValueTask.FromException<INatsJSMsg<ReadOnlyMemory<byte>>?>(
                        new InvalidOperationException("boom-2")
                    ),
                    _ => _Idle(idled, call.Arg<CancellationToken>()),
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
            // when — first failure, then release its backoff so the heartbeat and second failure run
            await firstFailureLogged.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
            timeProvider.Advance(TimeSpan.FromSeconds(5));
            await secondFailureLogged.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
            timeProvider.Advance(TimeSpan.FromSeconds(2));

            // then — the loop reached the idle 4th fetch after only the initial backoff, proving both the
            // failure streak and the retry delay reset on the heartbeat
            await idled.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
            prematureTermination.Task.IsCompleted.Should().BeFalse("the streak reset on the heartbeat fetch");
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

    private static ValueTask<INatsJSMsg<ReadOnlyMemory<byte>>?> _Idle(
        TaskCompletionSource idled,
        CancellationToken cancellationToken
    )
    {
        idled.TrySetResult();
        return new ValueTask<INatsJSMsg<ReadOnlyMemory<byte>>?>(
            Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken)
                .ContinueWith<INatsJSMsg<ReadOnlyMemory<byte>>?>(
                    static task =>
                    {
                        task.GetAwaiter().GetResult();
                        return null;
                    },
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default
                )
        );
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
}
