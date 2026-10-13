// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Messaging;
using Headless.Messaging.Persistence;
using Headless.Messaging.Processor;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Tests.Processor;

public sealed class MessageDelayedProcessorTests : TestBase
{
    [Fact]
    public async Task should_preserve_callback_path_for_legacy_storage_provider()
    {
        var storage = Substitute.For<IDataStorage>();
        var dispatcher = Substitute.For<IDispatcher>();
        var message = _CreateDelayedMessage();
        storage
            .ScheduleMessagesOfDelayedAsync(
                Arg.Any<Func<DbTransaction?, IEnumerable<MediumMessage>, ValueTask>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call => call.Arg<Func<DbTransaction?, IEnumerable<MediumMessage>, ValueTask>>()(null, [message]));
        await using var context = _CreateContext(storage);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await using var cancellableContext = new ProcessingContext(
            context.Provider,
            TimeProvider.System,
            cancellation.Token
        );
        var sut = new MessageDelayedProcessor(
            Substitute.For<ILogger<MessageDelayedProcessor>>(),
            dispatcher,
            Options.Create(new MessagingOptions())
        );

        Func<Task> act = () => sut.ProcessAsync(cancellableContext);
        await act.Should().ThrowAsync<OperationCanceledException>();

        await dispatcher
            .Received(1)
            .EnqueueToScheduler(message, message.ExpiresAt!.Value, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_enqueue_capability_winners_without_rewriting_storage()
    {
        var storage = Substitute.For<IDataStorage, IDelayedMessageClaimStorage>();
        var claimStorage = (IDelayedMessageClaimStorage)storage;
        var dispatcher = Substitute.For<IDispatcher, ICommittedDelayedMessageDispatcher>();
        var committedDispatcher = (ICommittedDelayedMessageDispatcher)dispatcher;
        var message = _CreateDelayedMessage();
        claimStorage
            .ClaimDelayedMessagesAsync(Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<IReadOnlyList<MediumMessage>>([message]));
        await using var context = _CreateContext(storage);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await using var cancellableContext = new ProcessingContext(
            context.Provider,
            TimeProvider.System,
            cancellation.Token
        );
        var sut = new MessageDelayedProcessor(
            Substitute.For<ILogger<MessageDelayedProcessor>>(),
            dispatcher,
            Options.Create(new MessagingOptions())
        );

        Func<Task> act = () => sut.ProcessAsync(cancellableContext);
        await act.Should().ThrowAsync<OperationCanceledException>();

        committedDispatcher.Received(1).EnqueueCommittedDelayedMessage(message);
        await dispatcher
            .DidNotReceive()
            .EnqueueToScheduler(
                Arg.Any<MediumMessage>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<DbTransaction?>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_drain_committed_claim_winners_when_cancellation_arrives_after_commit()
    {
        var storage = Substitute.For<IDataStorage, IDelayedMessageClaimStorage>();
        var claimStorage = (IDelayedMessageClaimStorage)storage;
        var dispatcher = Substitute.For<IDispatcher, ICommittedDelayedMessageDispatcher>();
        var committedDispatcher = (ICommittedDelayedMessageDispatcher)dispatcher;
        var messages = new[] { _CreateDelayedMessage(), _CreateDelayedMessage() };
        var cancellationToken = new CancellationToken(canceled: true);
        claimStorage
            .ClaimDelayedMessagesAsync(Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<IReadOnlyList<MediumMessage>>(messages));
        await using var context = _CreateContext(storage);
        await using var cancellableContext = new ProcessingContext(
            context.Provider,
            TimeProvider.System,
            cancellationToken
        );
        var sut = new MessageDelayedProcessor(
            Substitute.For<ILogger<MessageDelayedProcessor>>(),
            dispatcher,
            Options.Create(new MessagingOptions())
        );

        Func<Task> act = () => sut.ProcessAsync(cancellableContext);
        await act.Should().ThrowAsync<OperationCanceledException>();

        foreach (var message in messages)
        {
            committedDispatcher.Received(1).EnqueueCommittedDelayedMessage(message);
        }
    }

    [Fact]
    public async Task should_hand_back_only_the_claimed_rows_the_dispatcher_refuses()
    {
        // given — the scheduler takes the first claimed row and refuses the second.
        var storage = Substitute.For<IDataStorage, IDelayedMessageClaimStorage>();
        var claimStorage = (IDelayedMessageClaimStorage)storage;
        var dispatcher = Substitute.For<IDispatcher, ICommittedDelayedMessageDispatcher>();
        var committedDispatcher = (ICommittedDelayedMessageDispatcher)dispatcher;
        var accepted = _CreateDelayedMessage();
        var refused = _CreateDelayedMessage();
        claimStorage
            .ClaimDelayedMessagesAsync(Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<IReadOnlyList<MediumMessage>>([accepted, refused]));
        committedDispatcher.EnqueueCommittedDelayedMessage(accepted).Returns(true);
        committedDispatcher.EnqueueCommittedDelayedMessage(refused).Returns(false);

        // when
        await _ProcessOnceAsync(storage, dispatcher);

        // then — the refused row goes back under the lease it was claimed with; the accepted one stays queued.
        await storage
            .Received(1)
            .ChangePublishStateToDelayedAsync(
                Arg.Is<IReadOnlyCollection<MediumMessage>>(messages =>
                    messages.Count == 1 && ReferenceEquals(messages.Single(), refused)
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_not_hand_back_when_the_dispatcher_accepts_every_claimed_row()
    {
        // given
        var storage = Substitute.For<IDataStorage, IDelayedMessageClaimStorage>();
        var claimStorage = (IDelayedMessageClaimStorage)storage;
        var dispatcher = Substitute.For<IDispatcher, ICommittedDelayedMessageDispatcher>();
        var committedDispatcher = (ICommittedDelayedMessageDispatcher)dispatcher;
        var message = _CreateDelayedMessage();
        claimStorage
            .ClaimDelayedMessagesAsync(Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<IReadOnlyList<MediumMessage>>([message]));
        committedDispatcher.EnqueueCommittedDelayedMessage(message).Returns(true);

        // when
        await _ProcessOnceAsync(storage, dispatcher);

        // then — the poll ran and enqueued the row, and nothing came back.
        committedDispatcher.Received(1).EnqueueCommittedDelayedMessage(message);
        await storage
            .DidNotReceive()
            .ChangePublishStateToDelayedAsync(
                Arg.Any<IReadOnlyCollection<MediumMessage>>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_not_claim_while_the_scheduler_queue_is_full()
    {
        // given
        var storage = Substitute.For<IDataStorage, IDelayedMessageClaimStorage>();
        var claimStorage = (IDelayedMessageClaimStorage)storage;
        var dispatcher = Substitute.For<IDispatcher, ICommittedDelayedMessageDispatcher>();
        ((ICommittedDelayedMessageDispatcher)dispatcher).IsSchedulerFull.Returns(true);

        // when
        await _ProcessOnceAsync(storage, dispatcher);

        // then — claiming would only lease rows the scheduler must refuse and hand back.
        _ = ((ICommittedDelayedMessageDispatcher)dispatcher).Received().IsSchedulerFull;
        await claimStorage.DidNotReceive().ClaimDelayedMessagesAsync(Arg.Any<CancellationToken>());
        await storage
            .DidNotReceive()
            .ChangePublishStateToDelayedAsync(
                Arg.Any<IReadOnlyCollection<MediumMessage>>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_not_log_an_error_when_the_claim_is_cancelled_by_shutdown()
    {
        // given — the host stops while the claim is running.
        var storage = Substitute.For<IDataStorage, IDelayedMessageClaimStorage>();
        var claimStorage = (IDelayedMessageClaimStorage)storage;
        var dispatcher = Substitute.For<IDispatcher, ICommittedDelayedMessageDispatcher>();
        using var cancellation = new CancellationTokenSource();
        claimStorage
            .ClaimDelayedMessagesAsync(Arg.Any<CancellationToken>())
            .Returns<ValueTask<IReadOnlyList<MediumMessage>>>(async call =>
            {
                await cancellation.CancelAsync();
                call.Arg<CancellationToken>().ThrowIfCancellationRequested();
                return [];
            });
        var logger = Substitute.For<ILogger<MessageDelayedProcessor>>();
        logger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);
        await using var context = _CreateContext(storage);
        await using var stoppingContext = new ProcessingContext(
            context.Provider,
            TimeProvider.System,
            cancellation.Token
        );
        var sut = new MessageDelayedProcessor(logger, dispatcher, Options.Create(new MessagingOptions()));

        // when
        Func<Task> act = () => sut.ProcessAsync(stoppingContext);
        await act.Should().ThrowAsync<OperationCanceledException>();

        // then — stopping is expected, not a failed schedule.
        logger
            .ReceivedCalls()
            .Where(call => string.Equals(call.GetMethodInfo().Name, "Log", StringComparison.Ordinal))
            .Should()
            .NotContain(call => (LogLevel)call.GetArguments()[0]! >= LogLevel.Warning);
    }

    [Fact]
    public async Task should_preserve_callback_path_when_custom_dispatcher_lacks_committed_enqueue_capability()
    {
        var storage = Substitute.For<IDataStorage, IDelayedMessageClaimStorage>();
        var claimStorage = (IDelayedMessageClaimStorage)storage;
        var dispatcher = Substitute.For<IDispatcher>();
        var message = _CreateDelayedMessage();
        storage
            .ScheduleMessagesOfDelayedAsync(
                Arg.Any<Func<DbTransaction?, IEnumerable<MediumMessage>, ValueTask>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call => call.Arg<Func<DbTransaction?, IEnumerable<MediumMessage>, ValueTask>>()(null, [message]));
        await using var context = _CreateContext(storage);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await using var cancellableContext = new ProcessingContext(
            context.Provider,
            TimeProvider.System,
            cancellation.Token
        );
        var sut = new MessageDelayedProcessor(
            Substitute.For<ILogger<MessageDelayedProcessor>>(),
            dispatcher,
            Options.Create(new MessagingOptions())
        );

        Func<Task> act = () => sut.ProcessAsync(cancellableContext);
        await act.Should().ThrowAsync<OperationCanceledException>();

        await claimStorage.DidNotReceive().ClaimDelayedMessagesAsync(Arg.Any<CancellationToken>());
        await storage
            .Received(1)
            .ScheduleMessagesOfDelayedAsync(
                Arg.Any<Func<DbTransaction?, IEnumerable<MediumMessage>, ValueTask>>(),
                Arg.Any<CancellationToken>()
            );
        await dispatcher
            .Received(1)
            .EnqueueToScheduler(message, message.ExpiresAt!.Value, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_wait_the_configured_poll_interval_before_the_next_poll()
    {
        // given
        var storage = Substitute.For<IDataStorage>();
        var timeProvider = new FakeTimeProvider();
        await using var services = new ServiceCollection().AddSingleton(storage).BuildServiceProvider();
        await using var context = new ProcessingContext(services, timeProvider, AbortToken);
        var sut = new MessageDelayedProcessor(
            Substitute.For<ILogger<MessageDelayedProcessor>>(),
            Substitute.For<IDispatcher>(),
            Options.Create(new MessagingOptions { DelayedMessagePollInterval = TimeSpan.FromSeconds(5) })
        );

        // when
        var poll = sut.ProcessAsync(context);
        timeProvider.Advance(TimeSpan.FromSeconds(4));
        var waitingBeforeInterval = !poll.IsCompleted;
        timeProvider.Advance(TimeSpan.FromSeconds(1));

        // then
        waitingBeforeInterval.Should().BeTrue();
        await poll.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
    }

    private static async Task _ProcessOnceAsync(IDataStorage storage, IDispatcher dispatcher)
    {
        await using var context = _CreateContext(storage);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await using var cancellableContext = new ProcessingContext(
            context.Provider,
            TimeProvider.System,
            cancellation.Token
        );
        var sut = new MessageDelayedProcessor(
            Substitute.For<ILogger<MessageDelayedProcessor>>(),
            dispatcher,
            Options.Create(new MessagingOptions())
        );

        // One poll, then the poll-interval wait observes the cancellation.
        Func<Task> act = () => sut.ProcessAsync(cancellableContext);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private static ProcessingContext _CreateContext(IDataStorage storage)
    {
        var services = new ServiceCollection().AddSingleton(storage).BuildServiceProvider();
        return new ProcessingContext(services, TimeProvider.System, CancellationToken.None);
    }

    private static MediumMessage _CreateDelayedMessage()
    {
        return new MediumMessage
        {
            StorageId = Guid.NewGuid(),
            Origin = new Message(),
            Content = "{}",
            Lane = MessageLane.Bus,
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(30),
            LockedUntil = DateTimeOffset.UtcNow.AddMinutes(5),
        };
    }
}
