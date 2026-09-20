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
/// through that unit's <c>Outbox</c> places the stored row inside the unit's transaction; the unit dispatches it
/// to the broker after the commit and discards it on rollback. <see cref="IBus"/> is deliberately not used here:
/// it publishes autonomously, so its rows would survive the save's rollback. Register via
/// <c>AddHeadlessDbContextServices(...).AddIntegrationEventOutbox()</c>. Requires a messaging setup
/// (<c>AddHeadlessMessaging</c>) with an outbox storage provider.
/// </remarks>
internal sealed class OutboxIntegrationEventDispatcher(
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

        var unitOfWork = _UnitOfWorkOwningTheTransaction();

        foreach (var integrationEvent in integrationEvents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var context = integrationEvent;
            var options = new OutboxOptions
            {
                MessageId = context.EventId,
                CorrelationId = context.CorrelationId,
                CausationId = context.CausationId,
                TenantId = context.TenantId,
                SuppressAmbientBusinessContext = true,
                IsRetainedForTransactionReplay = true,
            };
            var publish = invokerCache.GetPublishInvoker(integrationEvent.Payload.GetType());
            // Read at the call site: the binding checks the handle's liveness per publish, and taking it is free.
            await publish(unitOfWork.Outbox, integrationEvent.Payload, options, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    // Single contained sync-over-async: the outbox only exposes an async publish, and the EF sync save path
    // calls this. No synchronization context is present on the EF save path, so blocking here cannot deadlock.
    public void Dispatch(IReadOnlyList<EventContext<object>> integrationEvents)
    {
        DispatchAsync(integrationEvents, CancellationToken.None).GetAwaiter().GetResult();
    }

    // Fail loud rather than dispatch non-atomically, and hand back the unit the publishes enlist in. The save
    // pipeline guards this before the domain-event drain, but handlers can add integration events during the
    // drain, so the check is repeated at dispatch time: with no resource-bearing unit of work current in this
    // scope there is nothing to enlist in, and the events would have to ship through an autonomous publisher —
    // breaking the atomic "dispatch on commit, discard on rollback" guarantee. Surface the mis-wire instead of
    // silently shipping a message a caller rollback can no longer recall.
    private IUnitOfWork _UnitOfWorkOwningTheTransaction()
    {
        var unitOfWork = unitOfWorkManager.Current;

        if (unitOfWork?.Resource is null)
        {
            throw new InvalidOperationException(HeadlessUnitOfWorkMessages.CallerOwnedTransactionWithoutUnitOfWork);
        }

        return unitOfWork;
    }
}
