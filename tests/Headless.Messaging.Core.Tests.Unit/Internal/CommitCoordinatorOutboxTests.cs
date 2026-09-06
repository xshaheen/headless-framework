// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Data.Common;
using Headless.Abstractions;
using Headless.CommitCoordination;
using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.Persistence;
using Headless.Messaging.Transactions;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;
using Microsoft.Extensions.Options;

namespace Tests.Internal;

public sealed class CommitCoordinatorOutboxTests : TestBase
{
    [Fact]
    public async Task should_buffer_message_and_only_signal_committed_dispatch_after_commit()
    {
        await using var transaction = new TestDbTransaction();
        var stack = new CommitScopeStack();
        var scope = new CommitScopeFactory(stack).Begin(
            new EmptyServiceProvider(),
            [new RelationalCommitContext(() => null, () => transaction)]
        );

        await using (scope)
        {
            var storage = Substitute.For<IDataStorage>();
            MediumMessage? stored = null;
            storage
                .StoreMessageAsync(
                    Arg.Any<string>(),
                    Arg.Any<MediumMessage>(),
                    Arg.Any<System.Data.Common.DbTransaction?>(),
                    Arg.Any<CancellationToken>()
                )
                .Returns(call =>
                {
                    call[2].Should().BeSameAs(transaction);
                    var mediumMessage = new MediumMessage
                    {
                        StorageId = Guid.NewGuid(),
                        Origin = ((MediumMessage)call[1]).Origin,
                        Content = "{}",
                        Lane = MessageLane.Bus,
                        Added = DateTimeOffset.UtcNow,
                    };
                    stored = mediumMessage;

                    return ValueTask.FromResult(mediumMessage);
                });

            await using var dispatcher = new RecordingCommittedDispatcher();
            var writer = new OutboxMessageWriter(storage, dispatcher, TimeProvider.System);
            var request = _CreatePublishRequestFactory().Create(new CoordinatorMessage("value"), lane: MessageLane.Bus);
            var decision = DeliveryDecisionResolver.Resolve(
                MessageLane.Bus,
                DeliveryMode.Durable,
                delay: null,
                DeliveryCoordination.Compatible(stack.Current!, transaction),
                TimeProvider.System.GetUtcNow()
            );

            await writer.WriteAsync(request, decision, AbortToken);

            dispatcher.CommittedMessages.Should().BeEmpty();

            await scope.SignalAsync(CommitOutcome.Committed);

            dispatcher.CommittedMessages.Should().ContainSingle().Which.Should().BeSameAs(stored);
            dispatcher.PublishCalls.Should().Be(0, "post-commit acceleration must not wait on transport dispatch");
        }
    }

    [Fact]
    public async Task should_capture_bus_and_queue_work_in_the_same_transaction_and_release_together()
    {
        await using var transaction = new TestDbTransaction();
        var stack = new CommitScopeStack();
        var scope = new CommitScopeFactory(stack).Begin(
            new EmptyServiceProvider(),
            [new RelationalCommitContext(() => null, () => transaction)]
        );

        await using (scope)
        {
            var storage = Substitute.For<IDataStorage>();
            var stored = new List<MediumMessage>();
            storage
                .StoreMessageAsync(
                    Arg.Any<string>(),
                    Arg.Any<MediumMessage>(),
                    Arg.Any<DbTransaction?>(),
                    Arg.Any<CancellationToken>()
                )
                .Returns(call =>
                {
                    call.ArgAt<DbTransaction?>(2).Should().BeSameAs(transaction);
                    var message = call.ArgAt<MediumMessage>(1);
                    stored.Add(message);
                    return ValueTask.FromResult(message);
                });

            await using var dispatcher = new RecordingCommittedDispatcher();
            var writer = new OutboxMessageWriter(storage, dispatcher, TimeProvider.System);
            var factory = _CreatePublishRequestFactory();

            foreach (var lane in new[] { MessageLane.Bus, MessageLane.Queue })
            {
                var request = factory.Create(new CoordinatorMessage(lane.ToString()), lane: lane);
                var decision = DeliveryDecisionResolver.Resolve(
                    lane,
                    DeliveryMode.Durable,
                    delay: null,
                    DeliveryCoordination.Compatible(stack.Current!, transaction),
                    TimeProvider.System.GetUtcNow()
                );
                await writer.WriteAsync(request, decision, AbortToken);
            }

            stored.Select(message => message.Lane).Should().Equal(MessageLane.Bus, MessageLane.Queue);
            dispatcher.CommittedMessages.Should().BeEmpty();

            await scope.SignalAsync(CommitOutcome.Committed);

            dispatcher.CommittedMessages.Should().Equal(stored);
        }
    }

    [Fact]
    public async Task should_reject_active_coordination_with_null_transaction_before_side_effects()
    {
        // Ambient coordination is authoritative. A torn-down relational capability must reject instead of silently
        // falling back to a non-transactional durable write.
        var stack = new CommitScopeStack();
        var scope = new CommitScopeFactory(stack).Begin(
            new EmptyServiceProvider(),
            [new RelationalCommitContext(() => null, () => null)]
        );

        await using (scope)
        {
            var storage = Substitute.For<IDataStorage>();
            var dispatcher = Substitute.For<IDispatcher>();

            var coordination = DeliveryCoordination.Incompatible(
                DeliveryCoordinationMismatch.MissingRelationalCapability
            );
            var act = () =>
                DeliveryDecisionResolver.Resolve(
                    MessageLane.Bus,
                    DeliveryMode.Durable,
                    delay: null,
                    coordination,
                    TimeProvider.System.GetUtcNow()
                );

            act.Should().Throw<InvalidOperationException>().WithMessage("*coordination*MissingRelationalCapability*");
            _ = storage
                .DidNotReceive()
                .StoreMessageAsync(
                    Arg.Any<string>(),
                    Arg.Any<MediumMessage>(),
                    Arg.Any<DbTransaction?>(),
                    Arg.Any<CancellationToken>()
                );
#pragma warning disable xUnit1051 // The default token is part of the verified call shape; substituting the test token would change the assertion.
            _ = dispatcher.DidNotReceiveWithAnyArgs().EnqueueToPublish(default!, default);
#pragma warning restore xUnit1051
        }
    }

    [Fact]
    public async Task should_signal_delayed_message_after_commit_without_scheduler_io()
    {
        var coordinator = new CommitCoordinator();
        await using var dispatcher = new RecordingCommittedDispatcher();
        var buffer = new MessageOutboxBuffer(coordinator, dispatcher);
        var message = _BuildMessage();
        message.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30);
        buffer.Add(message);

        await coordinator.SignalAsync(CommitOutcome.Committed, new EmptyServiceProvider());

        dispatcher.CommittedDelayedMessages.Should().ContainSingle().Which.Should().BeSameAs(message);
        dispatcher.SchedulerCalls.Should().Be(0, "the schedule state was already committed with the durable row");
    }

    [Fact]
    public async Task standalone_durable_should_return_after_storage_commit_without_transport_io()
    {
        var storage = Substitute.For<IDataStorage>();
        var stored = _BuildMessage();
        storage
            .StoreMessageAsync(
                Arg.Any<string>(),
                Arg.Any<MediumMessage>(),
                transaction: null,
                Arg.Any<CancellationToken>()
            )
            .Returns(stored);
        await using var dispatcher = new RecordingCommittedDispatcher();
        var writer = new OutboxMessageWriter(storage, dispatcher, TimeProvider.System);
        var request = _CreatePublishRequestFactory().Create(new CoordinatorMessage("value"), lane: MessageLane.Bus);
        var decision = DeliveryDecisionResolver.Resolve(
            MessageLane.Bus,
            DeliveryMode.Durable,
            delay: null,
            DeliveryCoordination.None,
            TimeProvider.System.GetUtcNow()
        );

        await writer.WriteAsync(request, decision, AbortToken);

        dispatcher.CommittedMessages.Should().ContainSingle().Which.Should().BeSameAs(stored);
        dispatcher.PublishCalls.Should().Be(0, "durable acceptance ends when the row commit succeeds");
    }

    [Fact]
    public async Task coordinated_delay_should_store_schedule_state_atomically_and_signal_after_commit()
    {
        await using var transaction = new TestDbTransaction();
        var stack = new CommitScopeStack();
        var scope = new CommitScopeFactory(stack).Begin(
            new EmptyServiceProvider(),
            [new RelationalCommitContext(() => null, () => transaction)]
        );

        await using (scope)
        {
            var now = TimeProvider.System.GetUtcNow();
            var publishAt = now.AddMinutes(30);
            var storage = Substitute.For<IDataStorage>();
            var stored = _BuildMessage();
            stored.ExpiresAt = publishAt;
            var commitTransaction = transaction;
            storage
                .StoreScheduledMessageAsync(
                    Arg.Any<string>(),
                    Arg.Any<MediumMessage>(),
                    publishAt,
                    commitTransaction,
                    Arg.Any<CancellationToken>()
                )
                .Returns(stored);
            await using var dispatcher = new RecordingCommittedDispatcher();
            var writer = new OutboxMessageWriter(storage, dispatcher, TimeProvider.System);
            var request = _CreatePublishRequestFactory().Create(new CoordinatorMessage("value"), lane: MessageLane.Bus);
            var decision = DeliveryDecisionResolver.Resolve(
                MessageLane.Bus,
                DeliveryMode.Durable,
                TimeSpan.FromMinutes(30),
                DeliveryCoordination.Compatible(stack.Current!, transaction),
                now
            );

            await writer.WriteAsync(request, decision, AbortToken);

            dispatcher.CommittedDelayedMessages.Should().BeEmpty();

            await scope.SignalAsync(CommitOutcome.Committed);

            dispatcher.CommittedDelayedMessages.Should().ContainSingle().Which.Should().BeSameAs(stored);
            dispatcher.SchedulerCalls.Should().Be(0);
        }
    }

    private static MediumMessage _BuildMessage()
    {
        return new()
        {
            StorageId = Guid.NewGuid(),
            Origin = new Message(new Dictionary<string, string?>(StringComparer.Ordinal), value: null),
            Content = "{}",
            Lane = MessageLane.Bus,
            Added = DateTimeOffset.UtcNow,
        };
    }

    private static MessagePublishRequestFactory _CreatePublishRequestFactory()
    {
        var registry = new ConsumerRegistry();
        registry.RegisterMessageName(typeof(CoordinatorMessage), "coordinator.message");

        return new MessagePublishRequestFactory(
            new SequentialGuidGenerator(SequentialGuidType.SqlServer),
            TimeProvider.System,
            Options.Create(new MessagingOptions()),
            registry,
            new NullCurrentTenant()
        );
    }

    private sealed record CoordinatorMessage(string Value);

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType)
        {
            return null;
        }
    }

    private sealed class RecordingCommittedDispatcher
        : IDispatcher,
            ICommittedMessageDispatcher,
            ICommittedDelayedMessageDispatcher
    {
        public List<MediumMessage> CommittedMessages { get; } = [];

        public List<MediumMessage> CommittedDelayedMessages { get; } = [];

        public int PublishCalls { get; private set; }

        public int SchedulerCalls { get; private set; }

        public void EnqueueCommittedMessage(MediumMessage message)
        {
            CommittedMessages.Add(message);
        }

        public void EnqueueCommittedDelayedMessage(MediumMessage message)
        {
            CommittedDelayedMessages.Add(message);
        }

        public ValueTask EnqueueToPublish(MediumMessage message, CancellationToken cancellationToken = default)
        {
            PublishCalls++;
            return ValueTask.CompletedTask;
        }

        public ValueTask EnqueueToExecute(
            MediumMessage message,
            ConsumerExecutorDescriptor? descriptor = null,
            CancellationToken cancellationToken = default
        )
        {
            return ValueTask.CompletedTask;
        }

        public Task EnqueueToScheduler(
            MediumMessage message,
            DateTimeOffset publishTime,
            DbTransaction? transaction = null,
            CancellationToken cancellationToken = default
        )
        {
            SchedulerCalls++;
            return Task.CompletedTask;
        }

        public ValueTask StartAsync(CancellationToken stoppingToken)
        {
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class NoopPublishMiddlewarePipeline(bool expectTransactional = true) : IPublishMiddlewarePipeline
    {
        public Task ExecuteAsync(
            object? contentObj,
            Type declaredMessageType,
            MessageLane lane,
            MessageOptions? messageOptions,
            DeliveryDecision decision,
            Func<MessageOptions?, CancellationToken, Task> innerPublish,
            CancellationToken cancellationToken = default
        )
        {
            decision.IsTransactional.Should().Be(expectTransactional);

            return innerPublish(messageOptions, cancellationToken);
        }

        public Task ExecuteAsync<T>(
            T? contentObj,
            MessageLane lane,
            MessageOptions? messageOptions,
            DeliveryDecision decision,
            Func<MessageOptions?, CancellationToken, Task> innerPublish,
            CancellationToken cancellationToken = default
        )
        {
            decision.IsTransactional.Should().Be(expectTransactional);

            return innerPublish(messageOptions, cancellationToken);
        }
    }

    private sealed class TestDbTransaction : DbTransaction
    {
        public override IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;

        protected override DbConnection? DbConnection => null;

        public override void Commit() { }

        public override void Rollback() { }
    }
}
