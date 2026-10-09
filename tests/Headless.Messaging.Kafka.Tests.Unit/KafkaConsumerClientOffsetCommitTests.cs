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

namespace Tests;

public sealed class KafkaConsumerClientOffsetCommitTests : TestBase
{
    private static readonly TopicPartition _Partition = new("orders.created", new Partition(0));

    private readonly IOptions<KafkaMessagingOptions> _options = Options.Create(
        new KafkaMessagingOptions { Servers = "localhost:9092" }
    );

    private readonly IServiceProvider _serviceProvider = new ServiceCollection().BuildServiceProvider();

    [Fact]
    public void should_default_to_cooperative_sticky_and_transport_owned_offset_commits()
    {
        // when
        var config = KafkaConsumerClient.BuildConfig(_options.Value, "orders", consumerConfig: null);

        // then
        config.GroupId.Should().Be("orders");
        config.PartitionAssignmentStrategy.Should().Be(PartitionAssignmentStrategy.CooperativeSticky);
        config.EnableAutoOffsetStore.Should().BeFalse();
        config.EnableAutoCommit.Should().BeTrue();
    }

    [Fact]
    public void should_keep_main_config_assignment_strategy_but_not_its_offset_commit_settings()
    {
        // given
        var options = new KafkaMessagingOptions
        {
            Servers = "localhost:9092",
            MainConfig =
            {
                ["partition.assignment.strategy"] = "range",
                ["enable.auto.commit"] = "false",
                ["enable.auto.offset.store"] = "true",
                ["auto.commit.interval.ms"] = "1000",
            },
        };

        // when
        var config = KafkaConsumerClient.BuildConfig(options, "orders", consumerConfig: null);

        // then
        config.PartitionAssignmentStrategy.Should().Be(PartitionAssignmentStrategy.Range);
        config.EnableAutoCommit.Should().BeTrue();
        config.EnableAutoOffsetStore.Should().BeFalse();
        config.AutoCommitIntervalMs.Should().Be(1000);
    }

    [Fact]
    public async Task should_store_offset_without_a_synchronous_commit_when_commit_async()
    {
        // given
        var consumer = Substitute.For<IConsumer<string, byte[]>>();
        await using var client = _CreateClient(consumer, concurrency: 1);
        client.Connect();
        var result = _CreateConsumeResult(41);

        // when
        await client.CommitAsync(result, AbortToken);

        // then
        consumer.Received(1).StoreOffset(Arg.Is<TopicPartitionOffset>(x => x.Offset == 42 && x.Partition == 0));
        consumer.DidNotReceive().Commit();
        consumer.DidNotReceive().Commit(Arg.Any<IEnumerable<TopicPartitionOffset>>());
        consumer.DidNotReceive().Commit(Arg.Any<ConsumeResult<string, byte[]>>());
    }

    [Fact]
    public async Task should_ignore_store_rejected_for_an_unassigned_partition()
    {
        // given
        var consumer = Substitute.For<IConsumer<string, byte[]>>();
        consumer
            .When(c => c.StoreOffset(Arg.Any<TopicPartitionOffset>()))
            .Do(_ => throw new KafkaException(ErrorCode.Local_State));
        await using var client = _CreateClient(consumer, concurrency: 1);
        client.Connect();

        // when
        var act = async () => await client.CommitAsync(_CreateConsumeResult(1), AbortToken);

        // then
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task should_wait_for_in_flight_handler_and_commit_its_offset_when_partition_is_revoked()
    {
        // given
        var consumer = _CreateConsumerYielding(_CreateConsumeResult(10));
        await using var client = _CreateClient(consumer, concurrency: 2);
        client.PartitionsAssigned([_Partition]);
        var handlerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseHandler = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client.AttachCallbacks(
            async (_, sender) =>
            {
                handlerStarted.TrySetResult();
                await releaseHandler.Task.ConfigureAwait(false);
                await client.CommitAsync(sender).ConfigureAwait(false);
            },
            _ => { }
        );

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var listening = client.ListeningAsync(TimeSpan.FromMilliseconds(10), cts.Token).AsTask();

        try
        {
            await handlerStarted.Task.WaitAsync(TimeSpan.FromSeconds(2), AbortToken);

            // when
            var revoke = Task.Run(
                () => client.PartitionsRevoked([new TopicPartitionOffset(_Partition, Offset.Unset)]),
                AbortToken
            );
            await Task.Delay(100, AbortToken);
            var completedBeforeHandler = revoke.IsCompleted;
            releaseHandler.TrySetResult();
            await revoke.WaitAsync(TimeSpan.FromSeconds(2), AbortToken);

            // then
            completedBeforeHandler.Should().BeFalse("the revoke waits for the handler still running on the partition");
            Received.InOrder(() =>
            {
                consumer.StoreOffset(Arg.Is<TopicPartitionOffset>(x => x.Offset == 11));
                consumer.Commit();
            });
        }
        finally
        {
            releaseHandler.TrySetResult();
            await cts.CancelAsync();
            await listening.WaitAsync(TimeSpan.FromSeconds(2), AbortToken);
        }
    }

    [Fact]
    public async Task should_not_wait_for_handlers_on_partitions_that_stay_assigned_when_revoked()
    {
        // given
        var consumer = _CreateConsumerYielding(_CreateConsumeResult(10));
        await using var client = _CreateClient(consumer, concurrency: 2);
        var otherPartition = new TopicPartition("orders.created", new Partition(1));
        client.PartitionsAssigned([_Partition, otherPartition]);
        var handlerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseHandler = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client.AttachCallbacks(
            async (_, _) =>
            {
                handlerStarted.TrySetResult();
                await releaseHandler.Task.ConfigureAwait(false);
            },
            _ => { }
        );

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var listening = client.ListeningAsync(TimeSpan.FromMilliseconds(10), cts.Token).AsTask();

        try
        {
            await handlerStarted.Task.WaitAsync(TimeSpan.FromSeconds(2), AbortToken);

            // when
            var revoke = Task.Run(
                () => client.PartitionsRevoked([new TopicPartitionOffset(otherPartition, Offset.Unset)]),
                AbortToken
            );

            // then
            await revoke.WaitAsync(TimeSpan.FromSeconds(2), AbortToken);
        }
        finally
        {
            releaseHandler.TrySetResult();
            await cts.CancelAsync();
            await listening.WaitAsync(TimeSpan.FromSeconds(2), AbortToken);
        }
    }

    [Fact]
    public async Task should_rewind_instead_of_dispatching_a_record_fetched_while_pausing()
    {
        // given
        var consumeEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseConsume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var record = _CreateConsumeResult(3);
        var consumer = Substitute.For<IConsumer<string, byte[]>>();
        var calls = 0;
        consumer
            .Consume(Arg.Any<TimeSpan>())
            .Returns(_ =>
            {
                if (Interlocked.Increment(ref calls) == 1)
                {
                    consumeEntered.TrySetResult();
                    releaseConsume.Task.Wait(TimeSpan.FromSeconds(5));
                    return record;
                }

                Thread.Sleep(5);

                return null!;
            });
        await using var client = _CreateClient(consumer, concurrency: 1);
        client.PartitionsAssigned([_Partition]);
        var dispatched = 0;
        client.AttachCallbacks(
            (_, _) =>
            {
                Interlocked.Increment(ref dispatched);
                return Task.CompletedTask;
            },
            _ => { }
        );

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        // The first Consume blocks, so the loop must not start on the test thread.
        var listening = Task.Run(
            () => client.ListeningAsync(TimeSpan.FromMilliseconds(10), cts.Token).AsTask(),
            AbortToken
        );

        try
        {
            await consumeEntered.Task.WaitAsync(TimeSpan.FromSeconds(2), AbortToken);

            // when
            await client.PauseAsync(AbortToken);
            releaseConsume.TrySetResult();
            await _WaitUntilAsync(() =>
                consumer
                    .ReceivedCalls()
                    .Any(c => string.Equals(c.GetMethodInfo().Name, "Seek", StringComparison.Ordinal))
            );

            // then
            consumer.Received(1).Seek(Arg.Is<TopicPartitionOffset>(x => x.Offset == 3 && x.Partition == 0));
            Volatile.Read(ref dispatched).Should().Be(0);
        }
        finally
        {
            releaseConsume.TrySetResult();
            await client.ResumeAsync(AbortToken);
            await cts.CancelAsync();

            // The loop may observe the cancellation while it is still waiting at the pause gate.
            var stopped = async () => await listening.WaitAsync(TimeSpan.FromSeconds(2), AbortToken);
            await stopped.Should().NotThrowAsync<TimeoutException>();
        }
    }

    [Fact]
    public async Task should_drain_in_flight_handler_then_commit_and_close_when_shutdown_async()
    {
        // given
        var consumer = _CreateConsumerYielding(_CreateConsumeResult(7));
        await using var client = _CreateClient(consumer, concurrency: 2);
        client.PartitionsAssigned([_Partition]);
        var handlerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseHandler = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client.AttachCallbacks(
            async (_, sender) =>
            {
                handlerStarted.TrySetResult();
                await releaseHandler.Task.ConfigureAwait(false);
                await client.CommitAsync(sender).ConfigureAwait(false);
            },
            _ => { }
        );

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var listening = client.ListeningAsync(TimeSpan.FromMilliseconds(10), cts.Token).AsTask();
        await handlerStarted.Task.WaitAsync(TimeSpan.FromSeconds(2), AbortToken);
        await cts.CancelAsync();
        await listening.WaitAsync(TimeSpan.FromSeconds(2), AbortToken);

        // when
        var shutdown = client.ShutdownAsync(TimeSpan.FromSeconds(5), AbortToken).AsTask();
        await Task.Delay(100, AbortToken);
        var completedBeforeHandler = shutdown.IsCompleted;
        releaseHandler.TrySetResult();
        await shutdown.WaitAsync(TimeSpan.FromSeconds(2), AbortToken);

        // then
        completedBeforeHandler.Should().BeFalse("shutdown waits for the in-flight handler");
        Received.InOrder(() =>
        {
            consumer.StoreOffset(Arg.Is<TopicPartitionOffset>(x => x.Offset == 8));
            consumer.Close();
            consumer.Dispose();
        });

        // Close commits the stored offsets itself when auto-commit is on.
        consumer.DidNotReceive().Commit();
    }

    [Fact]
    public async Task should_close_after_the_budget_and_drop_the_late_offset_when_handler_outlives_shutdown()
    {
        // given
        var consumer = _CreateConsumerYielding(_CreateConsumeResult(7));
        await using var client = _CreateClient(consumer, concurrency: 2);
        client.PartitionsAssigned([_Partition]);
        var logs = new ConcurrentQueue<LogMessageEventArgs>();
        var handlerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseHandler = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handlerCommitted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client.AttachCallbacks(
            async (_, sender) =>
            {
                handlerStarted.TrySetResult();
                await releaseHandler.Task.ConfigureAwait(false);
                await client.CommitAsync(sender).ConfigureAwait(false);
                handlerCommitted.TrySetResult();
            },
            logs.Enqueue
        );

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var listening = client.ListeningAsync(TimeSpan.FromMilliseconds(10), cts.Token).AsTask();
        await handlerStarted.Task.WaitAsync(TimeSpan.FromSeconds(2), AbortToken);
        await cts.CancelAsync();
        await listening.WaitAsync(TimeSpan.FromSeconds(2), AbortToken);

        // when
        await client.ShutdownAsync(TimeSpan.FromMilliseconds(50), AbortToken);
        releaseHandler.TrySetResult();
        await handlerCommitted.Task.WaitAsync(TimeSpan.FromSeconds(2), AbortToken);

        // then
        consumer.Received(1).Close();
        consumer.DidNotReceive().StoreOffset(Arg.Any<TopicPartitionOffset>());
        logs.Should().Contain(x => x.Reason!.Contains("draining in-flight Kafka handlers", StringComparison.Ordinal));
    }

    [Fact]
    public async Task should_log_and_still_dispose_when_close_fails()
    {
        // given
        var consumer = Substitute.For<IConsumer<string, byte[]>>();
        consumer.When(c => c.Close()).Do(_ => throw new KafkaException(ErrorCode.RequestTimedOut));
        var logs = new ConcurrentQueue<LogMessageEventArgs>();
        var client = _CreateClient(consumer, concurrency: 1);
        client.AttachCallbacks((_, _) => Task.CompletedTask, logs.Enqueue);
        client.Connect();

        // when
        await client.DisposeAsync();

        // then
        consumer.Received(1).Dispose();
        logs.Should().ContainSingle(x => x.Reason!.Contains("close failed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task should_commit_quietly_on_revoke_when_nothing_was_stored_since_the_last_commit()
    {
        // given
        var consumer = Substitute.For<IConsumer<string, byte[]>>();
        consumer.Commit().Returns(_ => throw new KafkaException(ErrorCode.Local_NoOffset));
        var logs = new ConcurrentQueue<LogMessageEventArgs>();
        await using var client = _CreateClient(consumer, concurrency: 2);
        client.AttachCallbacks((_, _) => Task.CompletedTask, logs.Enqueue);
        client.Connect();
        client.PartitionsAssigned([_Partition]);

        // when
        client.PartitionsRevoked([new TopicPartitionOffset(_Partition, Offset.Unset)]);

        // then
        consumer.Received(1).Commit();
        logs.Should().BeEmpty();
    }

    [Fact]
    public async Task should_log_when_the_revoke_commit_fails()
    {
        // given
        var consumer = Substitute.For<IConsumer<string, byte[]>>();
        consumer.Commit().Returns(_ => throw new KafkaException(ErrorCode.RequestTimedOut));
        var logs = new ConcurrentQueue<LogMessageEventArgs>();
        await using var client = _CreateClient(consumer, concurrency: 1);
        client.AttachCallbacks((_, _) => Task.CompletedTask, logs.Enqueue);
        client.Connect();
        client.PartitionsAssigned([_Partition]);

        // when
        client.PartitionsRevoked([new TopicPartitionOffset(_Partition, Offset.Unset)]);

        // then
        logs.Should().ContainSingle(x => x.Reason!.Contains("offset commit failed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task should_not_reopen_the_consumer_after_shutdown()
    {
        // given
        var builds = 0;
        var client = new KafkaConsumerClient(
            "test-group",
            1,
            _options,
            _serviceProvider,
            consumerFactory: _ =>
            {
                builds++;
                return Substitute.For<IConsumer<string, byte[]>>();
            }
        );
        await client.DisposeAsync();

        // when
        client.Connect();
        await client.ListeningAsync(TimeSpan.FromMilliseconds(10), AbortToken);

        // then
        builds.Should().Be(0);
    }

    [Fact]
    public async Task should_treat_an_already_existing_topic_as_created_when_fetch_message_names_async()
    {
        // given
        var adminClient = Substitute.For<IAdminClient>();
        adminClient
            .CreateTopicsAsync(Arg.Any<IEnumerable<TopicSpecification>>(), Arg.Any<CreateTopicsOptions>())
            .Returns<Task>(_ =>
                throw new CreateTopicsException([
                    new CreateTopicReport { Topic = "orders", Error = new Error(ErrorCode.TopicAlreadyExists) },
                ])
            );
        var logs = new ConcurrentQueue<LogMessageEventArgs>();
        await using var client = _CreateClient(Substitute.For<IConsumer<string, byte[]>>(), 1, adminClient);
        client.AttachCallbacks((_, _) => Task.CompletedTask, logs.Enqueue);

        // when
        var names = await client.FetchMessageNamesAsync(["orders"], AbortToken);

        // then
        names.Should().Equal("orders");
        logs.Should().BeEmpty();
    }

    [Fact]
    public async Task should_log_topic_creation_failure_other_than_already_exists()
    {
        // given
        var adminClient = Substitute.For<IAdminClient>();
        adminClient
            .CreateTopicsAsync(Arg.Any<IEnumerable<TopicSpecification>>(), Arg.Any<CreateTopicsOptions>())
            .Returns<Task>(_ =>
                throw new CreateTopicsException([
                    new CreateTopicReport { Topic = "orders", Error = new Error(ErrorCode.TopicAlreadyExists) },
                    new CreateTopicReport { Topic = "payments", Error = new Error(ErrorCode.TopicAuthorizationFailed) },
                ])
            );
        var logs = new ConcurrentQueue<LogMessageEventArgs>();
        await using var client = _CreateClient(Substitute.For<IConsumer<string, byte[]>>(), 1, adminClient);
        client.AttachCallbacks((_, _) => Task.CompletedTask, logs.Enqueue);

        // when
        await client.FetchMessageNamesAsync(["orders", "payments"], AbortToken);

        // then
        logs.Should().ContainSingle(x => x.LogType == MqLogType.ConsumeError);
    }

    private KafkaConsumerClient _CreateClient(
        IConsumer<string, byte[]> consumer,
        byte concurrency,
        IAdminClient? adminClient = null
    )
    {
        return new KafkaConsumerClient(
            "test-group",
            concurrency,
            _options,
            _serviceProvider,
            consumerFactory: _ => consumer,
            adminClientFactory: adminClient is null ? null : _ => adminClient
        );
    }

    private static async Task _WaitUntilAsync(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        while (!predicate())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    private static IConsumer<string, byte[]> _CreateConsumerYielding(ConsumeResult<string, byte[]> first)
    {
        var consumer = Substitute.For<IConsumer<string, byte[]>>();
        var calls = 0;
        consumer
            .Consume(Arg.Any<TimeSpan>())
            .Returns(_ =>
            {
                if (Interlocked.Increment(ref calls) == 1)
                {
                    return first;
                }

                Thread.Sleep(5);

                return null!;
            });

        return consumer;
    }

    private static ConsumeResult<string, byte[]> _CreateConsumeResult(long offset)
    {
        return new ConsumeResult<string, byte[]>
        {
            TopicPartitionOffset = new TopicPartitionOffset(_Partition, new Offset(offset)),
            Message = new Message<string, byte[]>
            {
                Value = BitConverter.GetBytes(offset),
                Headers =
                [
                    new Header(Headless.Messaging.Headers.MessageId, "msg-1"u8.ToArray()),
                    new Header(Headless.Messaging.Headers.MessageName, "TestEvent"u8.ToArray()),
                ],
            },
        };
    }
}
