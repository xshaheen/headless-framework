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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Tests.Internal;

public sealed class CommitCoordinatorOutboxTests : TestBase
{
    [Fact]
    public async Task should_buffer_message_on_commit_coordinator_and_dispatch_after_commit()
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
                NullLogger<MessageOutboxBuffer>.Instance
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
                NullLogger<MessageOutboxBuffer>.Instance
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

    [Fact]
    public async Task flush_should_swallow_timeout_when_dispatcher_exceeds_flush_timeout()
    {
        // A broker that never completes must not hold the post-commit drain (and its DI scope + DB connection)
        // open forever: the independent flush timeout cancels the dispatch, the OCE is swallowed, and the drain
        // completes. The undispatched message stays durable for the relay sweep.
        var timeProvider = new FakeTimeProvider();
        var flushTimeout = TimeSpan.FromSeconds(30);
        var coordinator = new CommitCoordinator();
        var logger = new RecordingLogger<MessageOutboxBuffer>();

        var dispatchEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var dispatcher = Substitute.For<IDispatcher>();
        dispatcher
            .EnqueueToPublish(Arg.Any<MediumMessage>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                dispatchEntered.TrySetResult();

                return new ValueTask(Task.Delay(Timeout.Infinite, call.Arg<CancellationToken>()));
            });

        var buffer = new MessageOutboxBuffer(coordinator, dispatcher, flushTimeout, timeProvider, logger);
        buffer.Add(_BuildMessage());

        // Commit drives FlushAsync, which blocks in the (hanging) dispatcher until the flush timeout fires.
        var drain = coordinator.SignalAsync(CommitOutcome.Committed, new EmptyServiceProvider()).AsTask();

        await dispatchEntered.Task;
        timeProvider.Advance(flushTimeout);

        // The drain completes (it would throw if the timeout propagated) and the timeout-swallow branch logged.
        await drain;
        logger.Warnings.Should().ContainSingle().Which.Should().Contain("exceeded");
    }

    [Fact]
    public async Task flush_should_keep_dispatching_and_rethrow_a_single_message_broker_fault()
    {
        var coordinator = new CommitCoordinator();
        var dispatched = new List<Guid>();
        var failing = _BuildMessage();
        var ok = _BuildMessage();

        var dispatcher = Substitute.For<IDispatcher>();
        dispatcher
            .EnqueueToPublish(Arg.Any<MediumMessage>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var msg = call.Arg<MediumMessage>();
                if (msg.StorageId == failing.StorageId)
                {
                    throw new InvalidOperationException("broker down");
                }

                dispatched.Add(msg.StorageId);

                return ValueTask.CompletedTask;
            });

        var buffer = new MessageOutboxBuffer(
            coordinator,
            dispatcher,
            TimeSpan.FromSeconds(30),
            new FakeTimeProvider(),
            NullLogger<MessageOutboxBuffer>.Instance
        );
        buffer.Add(failing);
        buffer.Add(ok);

        var act = async () => await coordinator.SignalAsync(CommitOutcome.Committed, new EmptyServiceProvider());

        // Single fault rethrown as the original type (via ExceptionDispatchInfo); the later message still dispatched.
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("broker down");
        dispatched
            .Should()
            .Contain(ok.StorageId, "a single message's broker fault must not abandon the rest of the buffer");
    }

    [Fact]
    public async Task flush_should_aggregate_multiple_message_broker_faults()
    {
        var coordinator = new CommitCoordinator();
        var dispatched = new List<Guid>();
        var fail1 = _BuildMessage();
        var ok = _BuildMessage();
        var fail2 = _BuildMessage();

        var dispatcher = Substitute.For<IDispatcher>();
        dispatcher
            .EnqueueToPublish(Arg.Any<MediumMessage>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var msg = call.Arg<MediumMessage>();
                if (msg.StorageId == fail1.StorageId || msg.StorageId == fail2.StorageId)
                {
                    throw new InvalidOperationException("broker down");
                }

                dispatched.Add(msg.StorageId);

                return ValueTask.CompletedTask;
            });

        var buffer = new MessageOutboxBuffer(
            coordinator,
            dispatcher,
            TimeSpan.FromSeconds(30),
            new FakeTimeProvider(),
            NullLogger<MessageOutboxBuffer>.Instance
        );
        buffer.Add(fail1);
        buffer.Add(ok);
        buffer.Add(fail2);

        var act = async () => await coordinator.SignalAsync(CommitOutcome.Committed, new EmptyServiceProvider());

        // Two faults aggregate; the interleaved good message still dispatched.
        (await act.Should().ThrowAsync<AggregateException>())
            .Which.InnerExceptions.Should()
            .HaveCount(2);
        dispatched.Should().Contain(ok.StorageId);
    }

    [Fact]
    public async Task flush_should_parse_offsetless_delayed_sent_time_as_utc()
    {
        var coordinator = new CommitCoordinator();
        var expectedPublishTime = new DateTime(2026, 7, 6, 9, 30, 0, DateTimeKind.Utc);
        var capturedPublishTimes = new List<DateTime>();

        var dispatcher = Substitute.For<IDispatcher>();
        dispatcher
            .EnqueueToScheduler(
                Arg.Any<MediumMessage>(),
                Arg.Do<DateTime>(publishTime => capturedPublishTimes.Add(publishTime)),
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.CompletedTask);

        var buffer = new MessageOutboxBuffer(
            coordinator,
            dispatcher,
            TimeSpan.FromSeconds(30),
            new FakeTimeProvider(),
            NullLogger<MessageOutboxBuffer>.Instance
        );
        var delayed = _BuildMessage();
        delayed.Origin.Headers[Headers.SentTime] = expectedPublishTime.ToString(CultureInfo.InvariantCulture);
        delayed.Origin.Headers[Headers.DelayTime] = TimeSpan
            .FromMinutes(30)
            .ToString("c", CultureInfo.InvariantCulture);
        buffer.Add(delayed);

        await coordinator.SignalAsync(CommitOutcome.Committed, new EmptyServiceProvider());

        capturedPublishTimes.Should().ContainSingle();
        capturedPublishTimes[0].Should().Be(expectedPublishTime);
        capturedPublishTimes[0].Kind.Should().Be(DateTimeKind.Utc);
    }

    private static MediumMessage _BuildMessage() =>
        new()
        {
            StorageId = Guid.NewGuid(),
            Origin = new Message(new Dictionary<string, string?>(StringComparer.Ordinal), value: null),
            Content = "{}",
            IntentType = IntentType.Bus,
            Added = DateTime.UtcNow,
        };

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

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<string> Warnings { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            if (logLevel == LogLevel.Warning)
            {
                Warnings.Add(formatter(state, exception));
            }
        }
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
