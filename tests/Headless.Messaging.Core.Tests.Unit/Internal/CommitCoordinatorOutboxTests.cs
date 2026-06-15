// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Data.Common;
using Headless.Abstractions;
using Headless.CommitCoordination;
using Headless.Generator.Primitives;
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
    public async Task should_buffer_message_on_commit_coordinator_and_dispatch_after_commit()
    {
        var transaction = new TestDbTransaction();
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
                    Arg.Any<object?>(),
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
                        IntentType = IntentType.Bus,
                        Added = DateTime.UtcNow,
                    };
                    stored = mediumMessage;

                    return ValueTask.FromResult(mediumMessage);
                });

            var dispatcher = Substitute.For<IDispatcher>();
            dispatcher
                .EnqueueToPublish(Arg.Any<MediumMessage>(), Arg.Any<CancellationToken>())
                .Returns(ValueTask.CompletedTask);

            var writer = new OutboxMessageWriter(
                storage,
                dispatcher,
                _CreatePublishRequestFactory(),
                stack,
                new NoopPublishMiddlewarePipeline(),
                TimeProvider.System,
                Options.Create(new MessagingOptions()),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<MessageOutboxBuffer>.Instance
            );

            await writer.PublishAsync(
                new CoordinatorMessage("value"),
                options: null,
                intentType: IntentType.Bus,
                AbortToken
            );

            await dispatcher.DidNotReceive().EnqueueToPublish(Arg.Any<MediumMessage>(), Arg.Any<CancellationToken>());

            await scope.SignalAsync(CommitOutcome.Committed);

            await dispatcher.Received(1).EnqueueToPublish(stored!, Arg.Any<CancellationToken>());
        }
    }

    [Fact]
    public async Task should_fall_back_to_immediate_dispatch_when_relational_capability_has_null_transaction()
    {
        // Ambient coordinator present, relational capability present — but its Transaction is null (e.g. the EF
        // context has no active transaction). _TryCaptureCoordinatedContext must NOT enlist; the publish drops to
        // the immediate non-transactional path: stored with a null transaction and dispatched in-band.
        var stack = new CommitScopeStack();
        var scope = new CommitScopeFactory(stack).Begin(
            new EmptyServiceProvider(),
            [new RelationalCommitContext(() => null, () => null)]
        );

        await using (scope)
        {
            var storage = Substitute.For<IDataStorage>();
            MediumMessage? stored = null;
            storage
                .StoreMessageAsync(
                    Arg.Any<string>(),
                    Arg.Any<MediumMessage>(),
                    Arg.Any<object?>(),
                    Arg.Any<CancellationToken>()
                )
                .Returns(call =>
                {
                    call[2].Should().BeNull("a null relational transaction must store non-transactionally");
                    var mediumMessage = new MediumMessage
                    {
                        StorageId = Guid.NewGuid(),
                        Origin = ((MediumMessage)call[1]).Origin,
                        Content = "{}",
                        IntentType = IntentType.Bus,
                        Added = DateTime.UtcNow,
                    };
                    stored = mediumMessage;

                    return ValueTask.FromResult(mediumMessage);
                });

            var dispatcher = Substitute.For<IDispatcher>();
            dispatcher
                .EnqueueToPublish(Arg.Any<MediumMessage>(), Arg.Any<CancellationToken>())
                .Returns(ValueTask.CompletedTask);

            var writer = new OutboxMessageWriter(
                storage,
                dispatcher,
                _CreatePublishRequestFactory(),
                stack,
                new NoopPublishMiddlewarePipeline(expectTransactional: false),
                TimeProvider.System,
                Options.Create(new MessagingOptions()),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<MessageOutboxBuffer>.Instance
            );

            await writer.PublishAsync(
                new CoordinatorMessage("value"),
                options: null,
                intentType: IntentType.Bus,
                AbortToken
            );

            // Dispatched in-band, not buffered on the coordinator.
            await dispatcher.Received(1).EnqueueToPublish(stored!, Arg.Any<CancellationToken>());

            await scope.SignalAsync(CommitOutcome.Committed);

            // Committing the scope must not re-dispatch — the message was never enlisted.
            await dispatcher.Received(1).EnqueueToPublish(Arg.Any<MediumMessage>(), Arg.Any<CancellationToken>());
        }
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
        public object? GetService(Type serviceType) => null;
    }

    private sealed class NoopPublishMiddlewarePipeline(bool expectTransactional = true) : IPublishMiddlewarePipeline
    {
        public Task ExecuteAsync<T>(
            T? contentObj,
            IntentType intentType,
            MessageOptions? messageOptions,
            TimeSpan? delayTime,
            Func<MessageOptions?, TimeSpan?, CancellationToken, Task> innerPublish,
            bool isTransactional = false,
            CancellationToken cancellationToken = default
        )
        {
            isTransactional.Should().Be(expectTransactional);

            return innerPublish(messageOptions, delayTime, cancellationToken);
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
