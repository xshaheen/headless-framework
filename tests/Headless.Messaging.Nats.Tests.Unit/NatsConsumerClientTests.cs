// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Nats;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using NATS.Client.Core;
using NATS.Client.JetStream;
using MsOptions = Microsoft.Extensions.Options;

namespace Tests;

public sealed class NatsConsumerClientTests : TestBase
{
    private readonly MsOptions.IOptions<MessagingNatsOptions> _options;
    private readonly IServiceProvider _serviceProvider;

    public NatsConsumerClientTests()
    {
        _options = MsOptions.Options.Create(new MessagingNatsOptions { Servers = "nats://localhost:4222" });
        _serviceProvider = new ServiceCollection().BuildServiceProvider();
    }

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
            new MessagingNatsOptions { Servers = "nats://user:password@localhost:4222" }
        );
        await using var client = new NatsConsumerClient("test-group", 1, options, _serviceProvider);

        client.BrokerAddress.Endpoint.Should().Be("nats://localhost:4222");
    }

    [Fact]
    public void should_throw_when_options_value_is_null()
    {
        var nullOptions = Substitute.For<MsOptions.IOptions<MessagingNatsOptions>>();
        nullOptions.Value.Returns((MessagingNatsOptions)null!);

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
            new MessagingNatsOptions
            {
                Servers = "nats://localhost:4222",
                EnableSubscriberClientStreamAndSubjectCreation = false,
            }
        );
        await using var client = new NatsConsumerClient("test-group", 1, options, _serviceProvider);
        var topics = new[] { "topic1", "topic2", "topic3" };

        var result = await client.FetchTopicsAsync(topics);
        result.Should().BeEquivalentTo(topics);
    }

    [Fact]
    public void BuildStreamSubjects_should_use_exact_subject_for_single_token_topic()
    {
        NatsConsumerClient.BuildStreamSubjects("orders", ["orders"]).Should().BeEquivalentTo(["orders"]);
    }

    [Fact]
    public void BuildStreamSubjects_should_use_wildcard_for_hierarchical_subjects()
    {
        NatsConsumerClient.BuildStreamSubjects("orders", ["orders.created"]).Should().BeEquivalentTo(["orders.>"]);
    }

    [Fact]
    public void BuildStreamSubjects_should_include_bare_and_hierarchical_subjects_together()
    {
        NatsConsumerClient
            .BuildStreamSubjects("orders", ["orders", "orders.created"])
            .Should()
            .BeEquivalentTo(["orders", "orders.>"]);
    }

    [Fact]
    public void BuildStreamSubjects_should_deduplicate_topics()
    {
        NatsConsumerClient
            .BuildStreamSubjects("orders", ["orders.created", "orders.created"])
            .Should()
            .BeEquivalentTo(["orders.>"]);
    }

    [Fact]
    public void BuildStreamSubjects_should_preserve_non_prefix_topics()
    {
        NatsConsumerClient
            .BuildStreamSubjects("events", ["orders.created"])
            .Should()
            .BeEquivalentTo(["orders.created"]);
    }

    // _NextBackoff tests

    [Fact]
    public void NextBackoff_should_double_current_delay()
    {
        var result = NatsConsumerClient._NextBackoff(TimeSpan.FromSeconds(1));
        result.Should().Be(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void NextBackoff_should_cap_at_30_seconds()
    {
        var result = NatsConsumerClient._NextBackoff(TimeSpan.FromSeconds(20));
        result.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void NextBackoff_should_not_exceed_ceiling_even_with_large_input()
    {
        var result = NatsConsumerClient._NextBackoff(TimeSpan.FromSeconds(60));
        result.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void NextBackoff_should_enforce_floor_when_next_is_below()
    {
        var result = NatsConsumerClient._NextBackoff(TimeSpan.FromSeconds(1), floor: TimeSpan.FromSeconds(5));
        result.Should().Be(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void NextBackoff_should_not_enforce_floor_when_next_is_above()
    {
        var result = NatsConsumerClient._NextBackoff(TimeSpan.FromSeconds(5), floor: TimeSpan.FromSeconds(5));
        result.Should().Be(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void NextBackoff_should_produce_correct_exponential_sequence()
    {
        var delay = TimeSpan.FromSeconds(1);
        delay = NatsConsumerClient._NextBackoff(delay); // 2s
        delay.Should().Be(TimeSpan.FromSeconds(2));
        delay = NatsConsumerClient._NextBackoff(delay); // 4s
        delay.Should().Be(TimeSpan.FromSeconds(4));
        delay = NatsConsumerClient._NextBackoff(delay); // 8s
        delay.Should().Be(TimeSpan.FromSeconds(8));
        delay = NatsConsumerClient._NextBackoff(delay); // 16s
        delay.Should().Be(TimeSpan.FromSeconds(16));
        delay = NatsConsumerClient._NextBackoff(delay); // 30s (capped)
        delay.Should().Be(TimeSpan.FromSeconds(30));
        delay = NatsConsumerClient._NextBackoff(delay); // 30s (stays capped)
        delay.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void NextBackoff_with_floor_should_produce_correct_sequence()
    {
        var delay = TimeSpan.FromSeconds(1);
        var floor = TimeSpan.FromSeconds(5);
        delay = NatsConsumerClient._NextBackoff(delay, floor); // max(2, 5) = 5s
        delay.Should().Be(TimeSpan.FromSeconds(5));
        delay = NatsConsumerClient._NextBackoff(delay, floor); // max(10, 5) = 10s
        delay.Should().Be(TimeSpan.FromSeconds(10));
        delay = NatsConsumerClient._NextBackoff(delay, floor); // max(20, 5) = 20s
        delay.Should().Be(TimeSpan.FromSeconds(20));
        delay = NatsConsumerClient._NextBackoff(delay, floor); // max(30, 5) = 30s (capped)
        delay.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task should_dispose_without_connection()
    {
        var client = _CreateClient("test-group");

        var act = async () => await client.DisposeAsync();
        await act.Should().NotThrowAsync();
    }

    // Pause/Resume tests

    [Fact]
    public async Task PauseAsync_is_idempotent_when_called_twice()
    {
        await using var client = _CreateClient("test-group");

        await client.PauseAsync();
        await client.PauseAsync();
    }

    [Fact]
    public async Task ResumeAsync_is_noop_when_not_paused()
    {
        await using var client = _CreateClient("test-group");
        await client.ResumeAsync();
    }

    [Fact]
    public async Task PauseAsync_then_ResumeAsync_completes_full_cycle()
    {
        await using var client = _CreateClient("test-group");

        await client.PauseAsync();
        await client.ResumeAsync();
    }

    [Fact]
    public async Task ResumeAsync_is_idempotent_after_resume()
    {
        await using var client = _CreateClient("test-group");

        await client.PauseAsync();
        await client.ResumeAsync();
        await client.ResumeAsync();
    }

    [Fact]
    public async Task PauseAsync_is_noop_after_disposal()
    {
        var client = _CreateClient("test-group");
        await client.DisposeAsync();

        await client.PauseAsync();
    }

    [Fact]
    public async Task ResumeAsync_is_noop_after_disposal()
    {
        var client = _CreateClient("test-group");
        await client.DisposeAsync();

        await client.ResumeAsync();
    }

    // CommitAsync / RejectAsync tests

    [Fact]
    public async Task CommitAsync_should_ack_valid_nats_message()
    {
        await using var client = _CreateClient("test-group");
        var msg = Substitute.For<INatsJSMsg<ReadOnlyMemory<byte>>>();

        await client.CommitAsync(msg);

        await msg.Received(1).AckAsync(cancellationToken: Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RejectAsync_should_nak_valid_nats_message()
    {
        await using var client = _CreateClient("test-group");
        var msg = Substitute.For<INatsJSMsg<ReadOnlyMemory<byte>>>();

        await client.RejectAsync(msg);

        await msg.Received(1).NakAsync(cancellationToken: Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CommitAsync_should_not_throw_for_null_sender()
    {
        await using var client = _CreateClient("test-group");

        var act = async () => await client.CommitAsync(null);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task RejectAsync_should_not_throw_for_null_sender()
    {
        await using var client = _CreateClient("test-group");

        var act = async () => await client.RejectAsync(null);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task CommitAsync_should_not_throw_for_non_nats_sender()
    {
        await using var client = _CreateClient("test-group");

        var act = async () => await client.CommitAsync("not a nats message");
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task RejectAsync_should_not_throw_for_non_nats_sender()
    {
        await using var client = _CreateClient("test-group");

        var act = async () => await client.RejectAsync("not a nats message");
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task CommitAsync_should_log_on_ack_failure()
    {
        await using var client = _CreateClient("test-group");
        LogMessageEventArgs? loggedArgs = null;
        client.OnLogCallback = args => loggedArgs = args;

        var msg = Substitute.For<INatsJSMsg<ReadOnlyMemory<byte>>>();
        msg.AckAsync(cancellationToken: Arg.Any<CancellationToken>())
            .Returns(x => throw new InvalidOperationException("ack failed"));

        await client.CommitAsync(msg);

        loggedArgs.Should().NotBeNull();
        loggedArgs!.LogType.Should().Be(MqLogType.AsyncErrorEvent);
        loggedArgs.Reason.Should().Contain("ack failed");
    }

    [Fact]
    public async Task RejectAsync_should_log_on_nak_failure()
    {
        await using var client = _CreateClient("test-group");
        LogMessageEventArgs? loggedArgs = null;
        client.OnLogCallback = args => loggedArgs = args;

        var msg = Substitute.For<INatsJSMsg<ReadOnlyMemory<byte>>>();
        msg.NakAsync(cancellationToken: Arg.Any<CancellationToken>())
            .Returns(x => throw new InvalidOperationException("nak failed"));

        await client.RejectAsync(msg);

        loggedArgs.Should().NotBeNull();
        loggedArgs!.LogType.Should().Be(MqLogType.AsyncErrorEvent);
        loggedArgs.Reason.Should().Contain("nak failed");
    }

    [Fact]
    public async Task should_nak_when_custom_headers_builder_throws()
    {
        // given
        var options = MsOptions.Options.Create(
            new MessagingNatsOptions
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

        await client.SubscribeAsync(["orders.created"]);

        using var cts = new CancellationTokenSource();

        // when
        var listeningTask = client.ListeningAsync(TimeSpan.FromMilliseconds(50), cts.Token).AsTask();
        try
        {
            await nakCalled.Task.WaitAsync(TimeSpan.FromSeconds(2), AbortToken);

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
    public async Task ListeningAsync_should_not_fetch_messages_until_resumed()
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
        await client.SubscribeAsync(["orders.created"]);
        await client.PauseAsync();

        using var cts = new CancellationTokenSource();

        // when
        var listeningTask = client.ListeningAsync(TimeSpan.FromMilliseconds(50), cts.Token).AsTask();
        try
        {
            await Task.Delay(100, AbortToken);

            // then
            nextCallCount.Should().Be(0);

            await client.ResumeAsync();
            await WaitUntilAsync(() => Volatile.Read(ref nextCallCount) > 0, TimeSpan.FromSeconds(1));
        }
        finally
        {
            await _StopListeningAsync(listeningTask, cts);
        }
    }

    [Fact]
    public async Task PauseAsync_should_cancel_inflight_fetch()
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
        await client.SubscribeAsync(["orders.created"]);

        using var cts = new CancellationTokenSource();

        // when
        var listeningTask = client.ListeningAsync(TimeSpan.FromMilliseconds(50), cts.Token).AsTask();
        try
        {
            await nextStarted.Task.WaitAsync(TimeSpan.FromSeconds(1), AbortToken);
            await client.PauseAsync();

            // then
            await fetchCanceled.Task.WaitAsync(TimeSpan.FromSeconds(1), AbortToken);
        }
        finally
        {
            await _StopListeningAsync(listeningTask, cts);
        }
    }

    [Fact]
    public async Task PauseAsync_and_ResumeAsync_should_restart_fetch_with_a_fresh_receive_token()
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
        await client.SubscribeAsync(["orders.created"]);

        using var cts = new CancellationTokenSource();

        // when
        var listeningTask = client.ListeningAsync(TimeSpan.FromMilliseconds(50), cts.Token).AsTask();
        try
        {
            await startedSignals[0].Task.WaitAsync(TimeSpan.FromSeconds(1), AbortToken);
            await client.PauseAsync();
            await canceledSignals[0].Task.WaitAsync(TimeSpan.FromSeconds(1), AbortToken);

            await client.ResumeAsync();
            await startedSignals[1].Task.WaitAsync(TimeSpan.FromSeconds(2), AbortToken);
            await client.PauseAsync();
            await canceledSignals[1].Task.WaitAsync(TimeSpan.FromSeconds(2), AbortToken);

            // then
            seenTokens.Should().HaveCountGreaterThanOrEqualTo(2);
            seenTokens[0].Should().NotBe(seenTokens[1]);
        }
        finally
        {
            await _StopListeningAsync(listeningTask, cts);
        }
    }

    private NatsConsumerClient _CreateClient(string groupName, byte groupConcurrent = 1)
    {
        return new NatsConsumerClient(groupName, groupConcurrent, _options, _serviceProvider);
    }

    private async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
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
            await listeningTask.WaitAsync(TimeSpan.FromSeconds(1), AbortToken);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            // Normal shutdown.
        }
    }
}
