// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Domain;
using Headless.EntityFramework.Contexts.Runtime;
using Headless.Messaging;
using Headless.UnitOfWork;

namespace Headless.EntityFramework;

/// <summary>
/// Default <see cref="IHeadlessOutboxDispatcher"/>: writes integration events to the messaging outbox enlisted
/// in the EF save's unit of work, so outbox rows persist atomically with the business data.
/// </summary>
/// <remarks>
/// The save pipeline makes its transaction the scope's current unit of work before this dispatcher runs (its own
/// transaction, or the caller's unit begun with <c>IUnitOfWorkManager.BeginAsync(db)</c>). Publishing each event
/// through <see cref="IBus"/> with durable delivery lets the outbox writer place the stored rows inside that unit's
/// transaction; the unit dispatches them to the broker after the commit and discards them on rollback. This
/// dispatcher therefore only fans the events out to the bus. Register via
/// <c>AddHeadlessDbContextServices(...).AddIntegrationEventOutbox()</c>. Requires a messaging setup
/// (<c>AddHeadlessMessaging</c>) with an outbox storage provider.
/// </remarks>
internal sealed class OutboxIntegrationEventDispatcher(
    IBus bus,
    IUnitOfWorkManager unitOfWorkManager,
    IntegrationEventPublishInvokerCache invokerCache
) : IHeadlessOutboxDispatcher
{
    public async Task DispatchAsync(
        IReadOnlyList<EventContext<object>> integrationEvents,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(integrationEvents);

        // An empty list can't dispatch anything non-atomically, so the unit-of-work guard only matters when there
        // is real work — bail before it.
        if (integrationEvents.Count == 0)
        {
            return;
        }

        _EnsureUnitOfWorkOwnsTheTransaction();

        foreach (var integrationEvent in integrationEvents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var context = integrationEvent;
            var options = new PublishOptions
            {
                DeliveryMode = DeliveryMode.Durable,
                MessageId = context.EventId,
                CorrelationId = context.CorrelationId,
                CausationId = context.CausationId,
                TenantId = context.TenantId,
                SuppressAmbientBusinessContext = true,
                IsRetainedForTransactionReplay = true,
            };
            var publish = invokerCache.GetPublishInvoker(integrationEvent.Payload.GetType());
            await publish(bus, integrationEvent.Payload, options, cancellationToken).ConfigureAwait(false);
        }
    }

    // Single contained sync-over-async: IBus only exposes an async publish, and the EF sync save path
    // calls this. No synchronization context is present on the EF save path, so blocking here cannot deadlock.
    public void Dispatch(IReadOnlyList<EventContext<object>> integrationEvents)
    {
        DispatchAsync(integrationEvents, CancellationToken.None).GetAwaiter().GetResult();
    }

    // Fail loud rather than dispatch non-atomically. The save pipeline guards this before the domain-event drain,
    // but handlers can add integration events during the drain, so the check is repeated at dispatch time: with no
    // resource-bearing unit of work current in this scope, publishing here would store + enqueue the integration
    // event immediately — breaking the atomic "dispatch on commit, discard on rollback" guarantee. Surface the
    // mis-wire instead of silently shipping a message a caller rollback can no longer recall.
    private void _EnsureUnitOfWorkOwnsTheTransaction()
    {
        if (unitOfWorkManager.Current?.Resource is not null)
        {
            return;
        }

        throw new InvalidOperationException(HeadlessUnitOfWorkMessages.CallerOwnedTransactionWithoutUnitOfWork);
    }
}
