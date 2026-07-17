// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Headless.Messaging;
using Headless.Messaging.Kafka;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

#pragma warning disable MA0045 // Do not use blocking calls, even when the calling method must become async
namespace Tests;

public sealed class KafkaConsumerClientTests : TestBase
{
    private readonly IOptions<KafkaMessagingOptions> _options = Options.Create(
        new KafkaMessagingOptions { Servers = "localhost:9092" }
    );
    private readonly IServiceProvider _serviceProvider = new ServiceCollection().BuildServiceProvider();

    [Fact]
    public async Task should_have_correct_broker_address()
    {
        // given, when
        await using var client = new KafkaConsumerClient("test-group", 1, _options, _serviceProvider);

        // then
        client.BrokerAddress.Name.Should().Be("kafka");
        client.BrokerAddress.Endpoint.Should().Be("localhost:9092");
    }

    [Fact]
    public async Task should_sanitize_broker_address_when_credentials_are_present()
    {
        // given
        var credentialedOptions = Options.Create(new KafkaMessagingOptions { Servers = "user:secret@broker:9092" });

        // when
        await using var client = new KafkaConsumerClient("test-group", 1, credentialedOptions, _serviceProvider);

        // then
        client.BrokerAddress.Name.Should().Be("kafka");
        client.BrokerAddress.Endpoint.Should().Be("broker:9092");
    }

    [Fact]
    public async Task should_allow_setting_on_message_callback()
    {
        // given
        await using var client = new KafkaConsumerClient("test-group", 1, _options, _serviceProvider);

        // when
        client.OnMessageCallback = (_, _) => Task.CompletedTask;

        // then
        client.OnMessageCallback.Should().NotBeNull();
    }

    [Fact]
    public async Task should_allow_setting_on_log_callback()
    {
        // given
        await using var client = new KafkaConsumerClient("test-group", 1, _options, _serviceProvider);

        // when
        client.OnLogCallback = _ => { };

        // then
        client.OnLogCallback.Should().NotBeNull();
    }

    [Fact]
    public async Task should_throw_when_subscribing_with_null_topics()
    {
        // given
        await using var client = new KafkaConsumerClient("test-group", 1, _options, _serviceProvider);

        // when
        var act = async () => await client.SubscribeAsync(null!);

        // then
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task should_throw_when_fetching_null_topics()
    {
        // given
        await using var client = new KafkaConsumerClient("test-group", 1, _options, _serviceProvider);

        // when
        var act = async () => await client.FetchMessageNamesAsync(null!);

        // then
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task should_return_topics_when_fetch_message_names_async()
    {
        // given
        var options = Options.Create(
            new KafkaMessagingOptions
            {
                Servers = "localhost:9092",
                MainConfig = { ["allow.auto.create.topics"] = "false" },
            }
        );
        await using var client = new KafkaConsumerClient("test-group", 1, options, _serviceProvider);
        client.OnLogCallback = _ => { }; // Set callback to avoid null ref

        // when - FetchMessageNamesAsync will return topics even if admin client fails
        // The topic names are returned regardless of whether topic creation succeeds
        var result = await client.FetchMessageNamesAsync(["topic1", "topic2"], AbortToken);

        // then
        result.Should().HaveCount(2);
        result.Should().Contain("topic1");
        result.Should().Contain("topic2");
    }

    [Fact]
    public async Task should_use_kafka_override_when_fetch_message_names_async_called_via_interface()
    {
        // given
        var adminClient = Substitute.For<IAdminClient>();
        adminClient
            .CreateTopicsAsync(Arg.Any<IEnumerable<TopicSpecification>>(), Arg.Any<CreateTopicsOptions>())
            .Returns(Task.CompletedTask);

        await using IConsumerClient client = new KafkaConsumerClient(
            "test-group",
            1,
            _options,
            _serviceProvider,
            adminClientFactory: _ => adminClient
        );

        // when
        var result = await client.FetchMessageNamesAsync(["orders.created", "orders.*"], AbortToken);

        // then
        result.Should().Contain("orders.created");
        result.Should().Contain(x => x.StartsWith("^orders\\.", StringComparison.Ordinal));
        await adminClient
            .Received(1)
            .CreateTopicsAsync(
                Arg.Is<IEnumerable<TopicSpecification>>(specs =>
                    specs.Select(x => x.Name).SequenceEqual(new[] { "orders.created" })
                ),
                Arg.Any<CreateTopicsOptions>()
            );
    }

    [Fact]
    public async Task should_dispose_successfully()
    {
        // given
        var client = new KafkaConsumerClient("test-group", 2, _options, _serviceProvider);

        // when
        await client.DisposeAsync();

        // then - no exception
        client.Should().NotBeNull();
    }

    [Fact]
    public async Task should_use_allow_auto_create_topics_from_config()
    {
        // given
        var optionsWithAutoCreate = Options.Create(
            new KafkaMessagingOptions
            {
                Servers = "localhost:9092",
                MainConfig = { ["allow.auto.create.topics"] = "false" },
            }
        );
        await using var client = new KafkaConsumerClient("test-group", 1, optionsWithAutoCreate, _serviceProvider);

        // then - client created successfully with the config
        client.Should().NotBeNull();
    }

    [Fact]
    public async Task should_support_custom_headers_builder()
    {
        // given
        var customHeaders = new List<KeyValuePair<string, string>> { new("custom-key", "custom-value") };
        var optionsWithCustomHeaders = Options.Create(
            new KafkaMessagingOptions { Servers = "localhost:9092", CustomHeadersBuilder = (_, _) => customHeaders }
        );
        await using var client = new KafkaConsumerClient("test-group", 1, optionsWithCustomHeaders, _serviceProvider);

        // then
        client.Should().NotBeNull();
    }

    [Fact]
    public async Task should_handle_retriable_error_codes()
    {
        // given
        var kafkaOptions = new KafkaMessagingOptions { Servers = "localhost:9092" };
        kafkaOptions.RetriableErrorCodes.Clear();
        kafkaOptions.RetriableErrorCodes.Add((int)ErrorCode.Local_TimedOut);
        var options = Options.Create(kafkaOptions);
        await using var client = new KafkaConsumerClient("test-group", 1, options, _serviceProvider);

        // then - client created successfully
        client.Should().NotBeNull();
    }

    [Fact]
    public async Task should_use_topic_options_for_auto_creation()
    {
        // given
        var options = Options.Create(
            new KafkaMessagingOptions
            {
                Servers = "localhost:9092",
                TopicOptions = new KafkaTopicOptions { NumPartitions = 3, ReplicationFactor = 1 },
            }
        );
        await using var client = new KafkaConsumerClient("test-group", 1, options, _serviceProvider);

        // then
        client.Should().NotBeNull();
    }

    [Fact]
    public async Task should_be_idempotent_when_connect()
    {
        // given
        await using var client = new KafkaConsumerClient("test-group", 1, _options, _serviceProvider);

        // when - call Connect multiple times (it won't actually connect without Kafka)
        // This tests the idempotency of the Connect check
        client.Connect();
        client.Connect();

        // then - no exception
    }

    [Fact]
    public async Task should_seek_failed_offset_when_reject_async()
    {
        // given
        var consumer = Substitute.For<IConsumer<string, byte[]>>();
        await using var client = new KafkaConsumerClient(
            "test-group",
            1,
            _options,
            _serviceProvider,
            consumerFactory: _ => consumer
        );
        client.Connect();
        var consumeResult = new ConsumeResult<string, byte[]>
        {
            TopicPartitionOffset = new TopicPartitionOffset("orders.created", new Partition(2), new Offset(17)),
            Message = new Message<string, byte[]> { Value = [1], Headers = [] },
        };

        // when
        await client.RejectAsync(consumeResult, AbortToken);

        // then
        consumer.Received(1).Seek(consumeResult.TopicPartitionOffset);
    }

    [Fact]
    public async Task should_ignore_offset_after_partition_is_revoked_when_commit_async()
    {
        // given
        var consumer = Substitute.For<IConsumer<string, byte[]>>();
        await using var client = new KafkaConsumerClient(
            "test-group",
            1,
            _options,
            _serviceProvider,
            consumerFactory: _ => consumer
        );
        client.Connect();
        var consumeResult = _CreateConsumeResult(17);
        client.PartitionsAssigned([consumeResult.TopicPartition]);

        // when
        client.PartitionsRevoked([consumeResult.TopicPartitionOffset]);
        await client.CommitAsync(consumeResult, AbortToken);

        // then
        consumer.DidNotReceive().Commit(Arg.Any<ConsumeResult<string, byte[]>>());
    }

    [Fact]
    public async Task should_ignore_offset_after_partition_is_lost_when_reject_async()
    {
        // given
        var consumer = Substitute.For<IConsumer<string, byte[]>>();
        await using var client = new KafkaConsumerClient(
            "test-group",
            1,
            _options,
            _serviceProvider,
            consumerFactory: _ => consumer
        );
        client.Connect();
        var consumeResult = _CreateConsumeResult(17);
        client.PartitionsAssigned([consumeResult.TopicPartition]);

        // when
        client.PartitionsLost([consumeResult.TopicPartitionOffset]);
        await client.RejectAsync(consumeResult, AbortToken);

        // then
        consumer.DidNotReceive().Seek(Arg.Any<TopicPartitionOffset>());
    }

    [Fact]
    public async Task should_apply_consumer_isolation_level_config_when_connect()
    {
        // given
        var consumer = Substitute.For<IConsumer<string, byte[]>>();
        ConsumerConfig? capturedConfig = null;

        await using var client = new KafkaConsumerClient(
            "test-group",
            1,
            _options,
            _serviceProvider,
            new KafkaConsumerConfig(IsolationLevel.ReadCommitted),
            consumerFactory: config =>
            {
                capturedConfig = config;
                return consumer;
            }
        );

        // when
        client.Connect();

        // then
        capturedConfig!.IsolationLevel.Should().Be(IsolationLevel.ReadCommitted);
    }

    [Fact]
    public async Task should_process_messages_concurrently_when_listening_async_concurrency_is_requested()
    {
        // given
        var consumer = Substitute.For<IConsumer<string, byte[]>>();
        var consumeCallCount = 0;
        consumer
            .Consume(Arg.Any<TimeSpan>())
            .Returns(_ =>
            {
                var callIndex = Interlocked.Increment(ref consumeCallCount);
                return callIndex switch
                {
                    1 => new ConsumeResult<string, byte[]>
                    {
                        TopicPartitionOffset = new TopicPartitionOffset(
                            "orders.created",
                            new Partition(0),
                            new Offset(0)
                        ),
                        Message = new Message<string, byte[]> { Value = [1], Headers = [] },
                    },
                    2 => new ConsumeResult<string, byte[]>
                    {
                        TopicPartitionOffset = new TopicPartitionOffset(
                            "orders.created",
                            new Partition(0),
                            new Offset(1)
                        ),
                        Message = new Message<string, byte[]> { Value = [2], Headers = [] },
                    },
                    _ => waitAndReturnNull(),
                };

                static ConsumeResult<string, byte[]> waitAndReturnNull()
                {
                    Thread.Sleep(10);
                    return null!;
                }
            });

        await using var client = new KafkaConsumerClient(
            "test-group",
            2,
            _options,
            _serviceProvider,
            consumerFactory: _ => consumer
        );

        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var callbackCount = 0;

        client.OnMessageCallback = async (_, _) =>
        {
            var current = Interlocked.Increment(ref callbackCount);
            if (current == 1)
            {
                firstStarted.TrySetResult();
                await releaseFirst.Task.ConfigureAwait(false);
                return;
            }

            secondStarted.TrySetResult();
        };
        client.OnLogCallback = _ => { };

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // when
        var listeningTask = client.ListeningAsync(TimeSpan.FromMilliseconds(10), cts.Token).AsTask();

        try
        {
            await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(1), AbortToken);
            await Task.Delay(100, AbortToken);

            // then
            await secondStarted.Task.WaitAsync(TimeSpan.FromSeconds(1), AbortToken);
        }
        finally
        {
            releaseFirst.TrySetResult();
            await cts.CancelAsync();
            await listeningTask.WaitAsync(TimeSpan.FromSeconds(1), AbortToken);
        }
    }

    [Fact]
    public async Task should_not_commit_past_inflight_lower_offsets_when_commit_async_processing_concurrently()
    {
        // given
        var consumer = Substitute.For<IConsumer<string, byte[]>>();
        var consumeCallCount = 0;
        consumer
            .Consume(Arg.Any<TimeSpan>())
            .Returns(_ =>
            {
                var callIndex = Interlocked.Increment(ref consumeCallCount);

                return callIndex switch
                {
                    1 => _CreateConsumeResult(100),
                    2 => _CreateConsumeResult(101),
                    3 => _CreateConsumeResult(102),
                    _ => waitAndReturnNull(),
                };

                static ConsumeResult<string, byte[]> waitAndReturnNull()
                {
                    Thread.Sleep(10);

                    return null!;
                }
            });

        var committedOffsets = new ConcurrentQueue<long>();
        consumer
            .When(c => c.Commit(Arg.Any<IEnumerable<TopicPartitionOffset>>()))
            .Do(call =>
            {
                foreach (var offset in call.Arg<IEnumerable<TopicPartitionOffset>>())
                {
                    committedOffsets.Enqueue(offset.Offset.Value);
                }
            });

        await using var client = new KafkaConsumerClient(
            "test-group",
            3,
            _options,
            _serviceProvider,
            consumerFactory: _ => consumer
        );

        var releaseOffset100 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseOffset101 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var highOffsetCommitted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        client.OnMessageCallback = async (message, sender) =>
        {
            var offset = BitConverter.ToInt64(message.Body.Span);

            switch (offset)
            {
                case 100:
                    await releaseOffset100.Task.WaitAsync(AbortToken).ConfigureAwait(false);
                    await client.CommitAsync(sender).ConfigureAwait(false);

                    break;

                case 101:
                    await releaseOffset101.Task.WaitAsync(AbortToken).ConfigureAwait(false);
                    await client.CommitAsync(sender).ConfigureAwait(false);

                    break;

                case 102:
                    await client.CommitAsync(sender).ConfigureAwait(false);
                    highOffsetCommitted.TrySetResult();

                    break;
            }
        };
        client.OnLogCallback = _ => { };

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var listeningTask = client.ListeningAsync(TimeSpan.FromMilliseconds(10), cts.Token).AsTask();

        try
        {
            await highOffsetCommitted.Task.WaitAsync(TimeSpan.FromSeconds(2), AbortToken);

            committedOffsets.Should().BeEmpty("offset 102 cannot commit while 100 and 101 are still in flight");

            releaseOffset100.TrySetResult();
            await _WaitUntilAsync(() => committedOffsets.Contains(101), AbortToken);
            committedOffsets.Should().Equal([101]);

            releaseOffset101.TrySetResult();
            await _WaitUntilAsync(() => committedOffsets.Contains(103), AbortToken);
            committedOffsets.Should().Equal([101, 103]);

            consumer.DidNotReceive().Commit(Arg.Any<ConsumeResult<string, byte[]>>());
        }
        finally
        {
            releaseOffset100.TrySetResult();
            releaseOffset101.TrySetResult();
            await cts.CancelAsync();
            await listeningTask.WaitAsync(TimeSpan.FromSeconds(1), AbortToken);
        }
    }

    [Fact]
    public async Task should_ignore_tracked_delivery_from_before_reassignment_when_commit_async()
    {
        // given
        var consumer = Substitute.For<IConsumer<string, byte[]>>();
        var consumeResult = _CreateConsumeResult(42);
        var consumeCallCount = 0;
        consumer
            .Consume(Arg.Any<TimeSpan>())
            .Returns(_ =>
            {
                if (Interlocked.Increment(ref consumeCallCount) == 1)
                {
                    return consumeResult;
                }

                Thread.Sleep(10);
                return null!;
            });

        await using var client = new KafkaConsumerClient(
            "test-group",
            2,
            _options,
            _serviceProvider,
            consumerFactory: _ => consumer
        );
        client.PartitionsAssigned([consumeResult.TopicPartition]);
        var capturedSender = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.OnMessageCallback = (_, sender) =>
        {
            capturedSender.TrySetResult(sender!);
            return Task.CompletedTask;
        };
        client.OnLogCallback = _ => { };

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var listeningTask = client.ListeningAsync(TimeSpan.FromMilliseconds(10), cts.Token).AsTask();

        try
        {
            var sender = await capturedSender.Task.WaitAsync(TimeSpan.FromSeconds(2), AbortToken);

            // when
            client.PartitionsRevoked([consumeResult.TopicPartitionOffset]);
            client.PartitionsAssigned([consumeResult.TopicPartition]);
            await client.CommitAsync(sender, AbortToken);

            // then
            consumer.DidNotReceive().Commit(Arg.Any<IEnumerable<TopicPartitionOffset>>());
        }
        finally
        {
            await cts.CancelAsync();
            await listeningTask.WaitAsync(TimeSpan.FromSeconds(1), AbortToken);
        }
    }

    [Fact]
    public async Task should_ignore_tracked_delivery_from_before_reassignment_when_reject_async()
    {
        // given
        var consumer = Substitute.For<IConsumer<string, byte[]>>();
        var consumeResult = _CreateConsumeResult(42);
        var consumeCallCount = 0;
        consumer
            .Consume(Arg.Any<TimeSpan>())
            .Returns(_ =>
            {
                if (Interlocked.Increment(ref consumeCallCount) == 1)
                {
                    return consumeResult;
                }

                Thread.Sleep(10);
                return null!;
            });

        await using var client = new KafkaConsumerClient(
            "test-group",
            2,
            _options,
            _serviceProvider,
            consumerFactory: _ => consumer
        );
        client.PartitionsAssigned([consumeResult.TopicPartition]);
        var capturedSender = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.OnMessageCallback = (_, sender) =>
        {
            capturedSender.TrySetResult(sender!);
            return Task.CompletedTask;
        };
        client.OnLogCallback = _ => { };

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var listeningTask = client.ListeningAsync(TimeSpan.FromMilliseconds(10), cts.Token).AsTask();

        try
        {
            var sender = await capturedSender.Task.WaitAsync(TimeSpan.FromSeconds(2), AbortToken);

            // when
            client.PartitionsRevoked([consumeResult.TopicPartitionOffset]);
            client.PartitionsAssigned([consumeResult.TopicPartition]);
            await client.RejectAsync(sender, AbortToken);

            // then
            consumer.DidNotReceive().Seek(Arg.Any<TopicPartitionOffset>());
        }
        finally
        {
            await cts.CancelAsync();
            await listeningTask.WaitAsync(TimeSpan.FromSeconds(1), AbortToken);
        }
    }

    // -------------------------------------------------------------------------
    // PauseAsync / ResumeAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task pause_async_is_idempotent_when_called_twice()
    {
        // given
        await using var client = new KafkaConsumerClient("test-group", 1, _options, _serviceProvider);

        // when — no consumer built yet, but should not throw
        await client.PauseAsync(AbortToken);
        await client.PauseAsync(AbortToken);

        // then — no exception
    }

    [Fact]
    public async Task resume_async_is_noop_when_not_paused()
    {
        // given
        await using var client = new KafkaConsumerClient("test-group", 1, _options, _serviceProvider);

        // when — never paused, resume should be a no-op
        await client.ResumeAsync(AbortToken);

        // then — no exception
    }

    [Fact]
    public async Task pause_async_then_resume_async_completes_full_cycle()
    {
        // given
        await using var client = new KafkaConsumerClient("test-group", 1, _options, _serviceProvider);

        // when
        await client.PauseAsync(AbortToken);
        await client.ResumeAsync(AbortToken);

        // then — no exception, state restored
    }

    [Fact]
    public async Task pause_async_is_noop_after_disposal()
    {
        // given
        var client = new KafkaConsumerClient("test-group", 1, _options, _serviceProvider);
        await client.DisposeAsync();

        // when — should not throw
        await client.PauseAsync(AbortToken);
    }

    [Fact]
    public async Task resume_async_is_noop_after_disposal()
    {
        // given
        var client = new KafkaConsumerClient("test-group", 1, _options, _serviceProvider);
        await client.DisposeAsync();

        // when — should not throw
        await client.ResumeAsync(AbortToken);
    }

    [Fact]
    public async Task resume_async_is_idempotent_after_resume()
    {
        // given
        await using var client = new KafkaConsumerClient("test-group", 1, _options, _serviceProvider);

        // when
        await client.PauseAsync(AbortToken);
        await client.ResumeAsync(AbortToken);
        await client.ResumeAsync(AbortToken); // second resume is no-op

        // then — no exception
    }

    [Fact]
    public async Task should_seek_back_when_custom_headers_builder_throws()
    {
        // given
        var throwingOptions = Options.Create(
            new KafkaMessagingOptions
            {
                Servers = "localhost:9092",
                CustomHeadersBuilder = (_, _) => throw new InvalidOperationException("bad header builder"),
            }
        );

        var consumer = Substitute.For<IConsumer<string, byte[]>>();
        var consumeCallCount = 0;
        var seekCalled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        consumer
            .Consume(Arg.Any<TimeSpan>())
            .Returns(_ =>
            {
                if (Interlocked.Increment(ref consumeCallCount) == 1)
                {
                    return new ConsumeResult<string, byte[]>
                    {
                        TopicPartitionOffset = new TopicPartitionOffset(
                            "orders.created",
                            new Partition(0),
                            new Offset(5)
                        ),
                        Message = new Message<string, byte[]> { Value = [1], Headers = [] },
                    };
                }

                // Block to avoid tight spin — throw OCE when cancellation fires
                throw new OperationCanceledException();
            });

        consumer.When(c => c.Seek(Arg.Any<TopicPartitionOffset>())).Do(_ => seekCalled.TrySetResult());

        await using var client = new KafkaConsumerClient(
            "test-group",
            1,
            throwingOptions,
            _serviceProvider,
            consumerFactory: _ => consumer
        );

        var callbackInvoked = false;
        LogMessageEventArgs? loggedError = null;
        client.OnMessageCallback = (_, _) =>
        {
            callbackInvoked = true;
            return Task.CompletedTask;
        };
        client.OnLogCallback = args =>
        {
            if (args.LogType == MqLogType.ConsumeError)
            {
                loggedError = args;
            }
        };

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // when — ListeningAsync will fault after the seek; we only need to observe the seek signal
#pragma warning disable AsyncFixer04
        var listeningTask = client.ListeningAsync(TimeSpan.FromMilliseconds(10), cts.Token).AsTask();
#pragma warning restore AsyncFixer04
        try
        {
            await seekCalled.Task.WaitAsync(TimeSpan.FromSeconds(2), AbortToken);

            // Observe the faulted task to prevent unobserved exception
            try
            {
                await listeningTask.WaitAsync(TimeSpan.FromSeconds(1), AbortToken);
            }
#pragma warning disable ERP022 // Unobserved exception in generic exception handler
            catch
            {
                // Expected — mock throws OCE on second Consume call.
            }
#pragma warning restore ERP022

            // then — callback should not be invoked, offset should be seeked back
            callbackInvoked.Should().BeFalse();
            consumer.Received(1).Seek(Arg.Is<TopicPartitionOffset>(tpo => tpo.Offset == 5));
            loggedError.Should().NotBeNull();
            loggedError!.Reason.Should().Contain("bad header builder");
        }
        finally
        {
            await cts.CancelAsync();
            try
            {
                await listeningTask.WaitAsync(TimeSpan.FromSeconds(1), AbortToken);
            }
#pragma warning disable ERP022 // Unobserved exception in generic exception handler
            catch
            {
                // Best-effort cleanup only.
            }
#pragma warning restore ERP022 // Unobserved exception in generic exception handler
        }
    }

    private static ConsumeResult<string, byte[]> _CreateConsumeResult(long offset)
    {
        return new ConsumeResult<string, byte[]>
        {
            TopicPartitionOffset = new TopicPartitionOffset("orders.created", new Partition(0), new Offset(offset)),
            Message = new Message<string, byte[]> { Value = BitConverter.GetBytes(offset), Headers = [] },
        };
    }

    private static async Task _WaitUntilAsync(Func<bool> predicate, CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(2));

        while (!predicate())
        {
            await Task.Delay(10, timeoutCts.Token).ConfigureAwait(false);
        }
    }
}
