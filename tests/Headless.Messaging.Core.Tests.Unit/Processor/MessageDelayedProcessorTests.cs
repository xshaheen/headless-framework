// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Messages;
using Headless.Messaging.Persistence;
using Headless.Messaging.Processor;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Headless.Messaging.Core.Tests.Unit.Processor;

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
                Arg.Any<Func<object?, IEnumerable<MediumMessage>, ValueTask>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call => call.Arg<Func<object?, IEnumerable<MediumMessage>, ValueTask>>()(null, [message]));
        using var context = _CreateContext(storage);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        using var cancellableContext = new ProcessingContext(context.Provider, TimeProvider.System, cancellation.Token);
        var sut = new MessageDelayedProcessor(Substitute.For<ILogger<MessageDelayedProcessor>>(), dispatcher);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sut.ProcessAsync(cancellableContext));

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
        using var context = _CreateContext(storage);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        using var cancellableContext = new ProcessingContext(context.Provider, TimeProvider.System, cancellation.Token);
        var sut = new MessageDelayedProcessor(Substitute.For<ILogger<MessageDelayedProcessor>>(), dispatcher);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sut.ProcessAsync(cancellableContext));

        committedDispatcher.Received(1).EnqueueCommittedDelayedMessage(message);
        await dispatcher
            .DidNotReceive()
            .EnqueueToScheduler(
                Arg.Any<MediumMessage>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<object?>(),
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
        using var context = _CreateContext(storage);
        using var cancellableContext = new ProcessingContext(context.Provider, TimeProvider.System, cancellationToken);
        var sut = new MessageDelayedProcessor(Substitute.For<ILogger<MessageDelayedProcessor>>(), dispatcher);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sut.ProcessAsync(cancellableContext));

        foreach (var message in messages)
        {
            committedDispatcher.Received(1).EnqueueCommittedDelayedMessage(message);
        }
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
                Arg.Any<Func<object?, IEnumerable<MediumMessage>, ValueTask>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call => call.Arg<Func<object?, IEnumerable<MediumMessage>, ValueTask>>()(null, [message]));
        using var context = _CreateContext(storage);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        using var cancellableContext = new ProcessingContext(context.Provider, TimeProvider.System, cancellation.Token);
        var sut = new MessageDelayedProcessor(Substitute.For<ILogger<MessageDelayedProcessor>>(), dispatcher);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sut.ProcessAsync(cancellableContext));

        await claimStorage.DidNotReceive().ClaimDelayedMessagesAsync(Arg.Any<CancellationToken>());
        await storage
            .Received(1)
            .ScheduleMessagesOfDelayedAsync(
                Arg.Any<Func<object?, IEnumerable<MediumMessage>, ValueTask>>(),
                Arg.Any<CancellationToken>()
            );
        await dispatcher
            .Received(1)
            .EnqueueToScheduler(message, message.ExpiresAt!.Value, null, Arg.Any<CancellationToken>());
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
            IntentType = IntentType.Bus,
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(30),
            LockedUntil = DateTimeOffset.UtcNow.AddMinutes(5),
        };
    }
}
