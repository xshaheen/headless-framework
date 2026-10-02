// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;
using Headless.Messaging;
using Headless.Messaging.CircuitBreaker;
using Headless.Messaging.Configuration;
using Headless.Messaging.Coordination;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.Persistence;
using Headless.Messaging.Processor;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Tests.Helpers;

namespace Tests.Processor;

/// <summary>
/// The relay of an additional outbox runs apart from the primary's: its rows keep their own storage through every
/// processor, and one unreachable database never stops the others from relaying.
/// </summary>
public sealed class AdditionalOutboxRelayTests : TestBase
{
    private readonly IDataStorage _primary = AdditionalOutboxDoubles.CreateRelationalStorage("orders");
    private readonly MessagingOutbox _billing = AdditionalOutboxDoubles.CreateOutbox("billing");
    private readonly CancellationTokenSource _cancellation = new();

    public AdditionalOutboxRelayTests()
    {
        // Every cycle also runs the primary's quadrant. Left unstubbed, the concurrent calls race on NSubstitute's
        // auto-value and can hand the processor a null pickup, so tests that need primary rows override this.
        _primary
            .GetPublishedMessagesOfNeedRetryAsync(Arg.Any<MessageLane>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<IEnumerable<MediumMessage>>([]));
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        _cancellation.Dispose();
        await base.DisposeAsyncCore();
    }

    [Fact]
    public async Task retry_should_back_off_only_the_unreachable_outbox_and_keep_relaying_the_primary()
    {
        // given
        var primaryMessage = _CreateMessage();
        _primary
            .GetPublishedMessagesOfNeedRetryAsync(MessageLane.Bus, Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<IEnumerable<MediumMessage>>([primaryMessage]));
        _billing
            .Storage.GetPublishedMessagesOfNeedRetryAsync(MessageLane.Bus, Arg.Any<CancellationToken>())
            .Returns<ValueTask<IEnumerable<MediumMessage>>>(_ => throw new TimeoutException("billing is down"));
        var dispatcher = Substitute.For<IDispatcher>();
        var sut = _CreateRetryProcessor(dispatcher);
        await using var context = _CreateContext();

        // when
        await _RunPublishBusCycleAsync(sut, context);

        // then
        sut.GetPickupFailureCountForTest(MessageType.Publish, MessageLane.Bus, outbox: 1).Should().Be(1);
        sut.GetPickupFailureCountForTest(MessageType.Publish, MessageLane.Bus).Should().Be(0);
        await dispatcher.Received(1).EnqueueToPublish(primaryMessage, Arg.Any<CancellationToken>());
        primaryMessage.OutboxStorage.Should().BeNull();
    }

    [Fact]
    public async Task retry_should_hand_an_outbox_row_to_the_dispatcher_carrying_its_own_storage()
    {
        // given
        var billingMessage = _CreateMessage();
        _billing
            .Storage.GetPublishedMessagesOfNeedRetryAsync(MessageLane.Bus, Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<IEnumerable<MediumMessage>>([billingMessage]));
        var dispatcher = Substitute.For<IDispatcher>();
        var sut = _CreateRetryProcessor(dispatcher);
        await using var context = _CreateContext();

        // when
        await _RunPublishBusCycleAsync(sut, context);

        // then
        await dispatcher
            .Received(1)
            .EnqueueToPublish(
                Arg.Is<MediumMessage>(message =>
                    message == billingMessage && ReferenceEquals(message.OutboxStorage, _billing.Storage)
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task retry_should_not_poll_an_outbox_until_it_is_initialized()
    {
        // given — shipping's database was down at startup
        var shipping = AdditionalOutboxDoubles.CreateOutbox("shipping", initialized: false);
        shipping
            .Storage.GetPublishedMessagesOfNeedRetryAsync(MessageLane.Bus, Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<IEnumerable<MediumMessage>>([]));
        var sut = _CreateRetryProcessor(Substitute.For<IDispatcher>(), shipping);
        await using var context = _CreateContext();

        // when
        await _RunPublishBusCycleAsync(sut, context);

        // then
        await shipping
            .Storage.DidNotReceive()
            .GetPublishedMessagesOfNeedRetryAsync(Arg.Any<MessageLane>(), Arg.Any<CancellationToken>());
        sut.GetPickupFailureCountForTest(MessageType.Publish, MessageLane.Bus, outbox: 1).Should().Be(0);

        // and when — its initialization succeeds
        await shipping.EnsureInitializedAsync(AbortToken);
        await _RunPublishBusCycleAsync(sut, context);

        // then
        await shipping
            .Storage.Received(1)
            .GetPublishedMessagesOfNeedRetryAsync(MessageLane.Bus, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task delayed_and_collector_should_skip_an_outbox_until_it_is_initialized()
    {
        // given
        var shipping = AdditionalOutboxDoubles.CreateOutbox("shipping", initialized: false);
        var initializer = Substitute.For<IStorageTableNames>();
        initializer.GetPublishedTableName().Returns("orders.published");
        initializer.GetReceivedTableName().Returns("orders.received");
        await using var provider = new ServiceCollection()
            .AddSingleton(TimeProvider.System)
            .AddSingleton(initializer)
            .AddSingleton(_primary)
            .AddSingleton(AdditionalOutboxDoubles.CreateOutboxes(_primary, shipping))
            .BuildServiceProvider();
        var delayed = new MessageDelayedProcessor(
            NullLogger<MessageDelayedProcessor>.Instance,
            Substitute.For<IDispatcher, ICommittedDelayedMessageDispatcher>()
        );
        var collector = new CollectorProcessor(
            NullLogger<CollectorProcessor>.Instance,
            Options.Create(new MessagingOptions()),
            provider
        );
        ((IDelayedMessageClaimStorage)_primary)
            .ClaimDelayedMessagesAsync(Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<IReadOnlyList<MediumMessage>>([]));

        // when — each runs one pass, then is stopped while it waits for the next
        using var delayedCancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        await using (var context = new ProcessingContext(provider, TimeProvider.System, delayedCancellation.Token))
        {
            var runDelayed = () => delayed.ProcessAsync(context);
            await runDelayed.Should().ThrowAsync<OperationCanceledException>();
        }

        using var collectorCancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        await using (var context = new ProcessingContext(provider, TimeProvider.System, collectorCancellation.Token))
        {
            var runCollector = () => collector.ProcessAsync(context);
            await runCollector.Should().ThrowAsync<OperationCanceledException>();
        }

        // then
        await ((IDelayedMessageClaimStorage)shipping.Storage)
            .DidNotReceive()
            .ClaimDelayedMessagesAsync(Arg.Any<CancellationToken>());
        await shipping
            .Storage.DidNotReceive()
            .DeleteExpiresAsync(
                Arg.Any<string>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>()
            );
        await ((IDelayedMessageClaimStorage)_primary)
            .Received(1)
            .ClaimDelayedMessagesAsync(Arg.Any<CancellationToken>());
        await _primary
            .Received(1)
            .DeleteExpiresAsync(
                "orders.published",
                Arg.Any<DateTimeOffset>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public void retry_should_give_each_outbox_its_own_publish_lock_and_leave_the_primary_lock_unchanged()
    {
        var sut = _CreateRetryProcessor(Substitute.For<IDispatcher>());
        var version = new MessagingOptions().Version;

        sut.GetLockResourceForTest(MessageType.Publish, MessageLane.Bus)
            .Should()
            .Be(MessagingKeys.PublishRetryResource(version, MessageLane.Bus));
        sut.GetLockResourceForTest(MessageType.Publish, MessageLane.Bus, outbox: 1)
            .Should()
            .Be(MessagingKeys.PublishRetryResource(version, MessageLane.Bus, _billing.LockKey));
        sut.GetLockResourceForTest(MessageType.Publish, MessageLane.Queue, outbox: 1)
            .Should()
            .NotBe(sut.GetLockResourceForTest(MessageType.Publish, MessageLane.Bus, outbox: 1));
    }

    [Fact]
    public async Task delayed_should_claim_from_every_outbox_when_the_primary_fails()
    {
        // given
        var billingMessage = _CreateMessage();
        ((IDelayedMessageClaimStorage)_primary)
            .ClaimDelayedMessagesAsync(Arg.Any<CancellationToken>())
            .Returns<ValueTask<IReadOnlyList<MediumMessage>>>(_ => throw new TimeoutException("orders is down"));
        ((IDelayedMessageClaimStorage)_billing.Storage)
            .ClaimDelayedMessagesAsync(Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<IReadOnlyList<MediumMessage>>([billingMessage]));
        var dispatcher = Substitute.For<IDispatcher, ICommittedDelayedMessageDispatcher>();
        var sut = new MessageDelayedProcessor(NullLogger<MessageDelayedProcessor>.Instance, dispatcher);
        await using var context = _CreateContext(TimeSpan.FromMilliseconds(200));

        // when
        var act = () => sut.ProcessAsync(context);
        await act.Should().ThrowAsync<OperationCanceledException>();

        // then
        ((ICommittedDelayedMessageDispatcher)dispatcher)
            .Received(1)
            .EnqueueCommittedDelayedMessage(
                Arg.Is<MediumMessage>(message =>
                    message == billingMessage && ReferenceEquals(message.OutboxStorage, _billing.Storage)
                )
            );
    }

    [Fact]
    public async Task collector_should_sweep_the_primary_when_an_outbox_database_fails()
    {
        // given
        var initializer = Substitute.For<IStorageTableNames>();
        initializer.GetPublishedTableName().Returns("orders.published");
        initializer.GetReceivedTableName().Returns("orders.received");
        _billing
            .Storage.DeleteExpiresAsync(
                Arg.Any<string>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>()
            )
            .Returns<ValueTask<int>>(_ => throw new TimeoutException("billing is down"));
        await using var provider = new ServiceCollection()
            .AddSingleton(TimeProvider.System)
            .AddSingleton(initializer)
            .AddSingleton(_primary)
            .AddSingleton(AdditionalOutboxDoubles.CreateOutboxes(_primary, _billing))
            .BuildServiceProvider();
        var sut = new CollectorProcessor(
            NullLogger<CollectorProcessor>.Instance,
            Options.Create(new MessagingOptions()),
            provider
        );
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        await using var context = new ProcessingContext(provider, TimeProvider.System, cancellation.Token);

        // when
        var act = () => sut.ProcessAsync(context);
        await act.Should().ThrowAsync<OperationCanceledException>();

        // then
        await _billing
            .Storage.Received(1)
            .DeleteExpiresAsync(
                "billing.published",
                Arg.Any<DateTimeOffset>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>()
            );
        await _primary
            .Received(1)
            .DeleteExpiresAsync(
                "orders.published",
                Arg.Any<DateTimeOffset>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>()
            );
        await _primary
            .Received(1)
            .DeleteExpiresAsync(
                "orders.received",
                Arg.Any<DateTimeOffset>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task reclaimer_should_reclaim_every_table_and_then_report_the_outbox_failure()
    {
        // given
        var failure = new TimeoutException("billing is down");
        _billing
            .Storage.ReclaimDeadPublishedOwnersAsync(
                Arg.Any<IReadOnlyCollection<string>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns<ValueTask<int>>(_ => throw failure);
        var shipping = AdditionalOutboxDoubles.CreateOutbox("shipping");
        var sut = new MessagingDeadOwnerReclaimer(
            _primary,
            Options.Create(new MessagingOptions()),
            NullLogger<MessagingDeadOwnerReclaimer>.Instance,
            AdditionalOutboxDoubles.CreateOutboxes(_primary, _billing, shipping)
        );

        // when
        var act = () => sut.ReclaimAsync(["node@5"], AbortToken);

        // then
        (await act.Should().ThrowAsync<TimeoutException>())
            .Which.Should()
            .BeSameAs(failure);
        await _primary
            .Received(1)
            .ReclaimDeadPublishedOwnersAsync(Arg.Any<IReadOnlyCollection<string>>(), CancellationToken.None);
        await _primary
            .Received(1)
            .ReclaimDeadReceivedOwnersAsync(Arg.Any<IReadOnlyCollection<string>>(), CancellationToken.None);
        await shipping
            .Storage.Received(1)
            .ReclaimDeadPublishedOwnersAsync(Arg.Any<IReadOnlyCollection<string>>(), CancellationToken.None);
        await _billing
            .Storage.DidNotReceive()
            .ReclaimDeadReceivedOwnersAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(MessageRevocationResult.Revoked)]
    [InlineData(MessageRevocationResult.AttemptReserved)]
    public async Task revoker_should_answer_from_the_outbox_that_holds_the_row(MessageRevocationResult result)
    {
        // given
        var handle = Guid.NewGuid();
        ((IMessageRevocationStorage)_primary).RevokeAsync(handle, AbortToken).Returns(MessageRevocationResult.NotFound);
        ((IMessageRevocationStorage)_billing.Storage).RevokeAsync(handle, AbortToken).Returns(result);
        var sut = new MessageRevoker(_primary, AdditionalOutboxDoubles.CreateOutboxes(_primary, _billing));

        // when
        var revoked = await sut.RevokeAsync(handle, AbortToken);

        // then
        revoked.Should().Be(result);
    }

    [Fact]
    public async Task revoker_should_not_probe_an_outbox_once_the_primary_knows_the_row()
    {
        var handle = Guid.NewGuid();
        ((IMessageRevocationStorage)_primary).RevokeAsync(handle, AbortToken).Returns(MessageRevocationResult.Revoked);
        var sut = new MessageRevoker(_primary, AdditionalOutboxDoubles.CreateOutboxes(_primary, _billing));

        (await sut.RevokeAsync(handle, AbortToken)).Should().Be(MessageRevocationResult.Revoked);

        await ((IMessageRevocationStorage)_billing.Storage)
            .DidNotReceive()
            .RevokeAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    private MessageNeedToRetryProcessor _CreateRetryProcessor(IDispatcher dispatcher, MessagingOutbox? outbox = null)
    {
        return new MessageNeedToRetryProcessor(
            Options.Create(new MessagingOptions()),
            Options.Create(new RetryProcessorOptions { BaseInterval = TimeSpan.FromMilliseconds(50) }),
            NullLogger<MessageNeedToRetryProcessor>.Instance,
            dispatcher,
            Substitute.For<IDistributedLock>(),
            outboxes: AdditionalOutboxDoubles.CreateOutboxes(_primary, outbox ?? _billing)
        );
    }

    private ProcessingContext _CreateContext(TimeSpan? cancelAfter = null)
    {
        var provider = new ServiceCollection()
            .AddSingleton(_primary)
            .AddSingleton(AdditionalOutboxDoubles.CreateOutboxes(_primary, _billing))
            .BuildServiceProvider();
        if (cancelAfter is { } delay)
        {
            _cancellation.CancelAfter(delay);
        }

        return new ProcessingContext(provider, TimeProvider.System, _cancellation.Token);
    }

    private static async Task _RunPublishBusCycleAsync(MessageNeedToRetryProcessor sut, ProcessingContext context)
    {
        sut.MarkQuadrantDueForTest(MessageType.Publish, MessageLane.Bus);
        sut.MarkQuadrantDueForTest(MessageType.Publish, MessageLane.Bus, outbox: 1);
        await sut.ProcessAsync(context);
        await Task.WhenAll(
            sut.WaitForQuadrantIdleForTestAsync(MessageType.Publish, MessageLane.Bus),
            sut.WaitForQuadrantIdleForTestAsync(MessageType.Publish, MessageLane.Bus, outbox: 1)
        );
    }

    private static MediumMessage _CreateMessage()
    {
        return new MediumMessage
        {
            StorageId = Guid.NewGuid(),
            Origin = new Message(),
            Content = "{}",
            Lane = MessageLane.Bus,
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(30),
        };
    }
}
