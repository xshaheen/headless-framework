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
/// The save pipeline hands over the unit that owns its transaction (its own, or the caller's begun with
/// <c>IUnitOfWorkFactory.BeginAsync(db)</c> / <c>RunAsync(db, …)</c>). Publishing each event through that unit's
/// <c>Outbox</c> places the stored row inside the unit's transaction; the unit dispatches it to the broker after
/// the commit and discards it on rollback. <see cref="IBus"/> is deliberately not used here: it publishes
/// autonomously, so its rows would survive the save's rollback. Register via
/// <c>AddHeadlessDbContextServices(...).AddIntegrationEventOutbox()</c>. Requires a messaging setup
/// (<c>AddHeadlessMessaging</c>) with an outbox storage provider.
/// </remarks>
internal sealed class OutboxIntegrationEventDispatcher(IntegrationEventPublishInvokerCache invokerCache)
    : IHeadlessOutboxDispatcher
{
    public async Task DispatchAsync(
        IUnitOfWork unitOfWork,
        IReadOnlyList<EventContext<object>> integrationEvents,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(unitOfWork);
        Argument.IsNotNull(integrationEvents);

        // An empty list can't dispatch anything non-atomically, so the resource guard only matters when there is
        // real work — bail before it.
        if (integrationEvents.Count == 0)
        {
            return;
        }

        _EnsureOwnsATransaction(unitOfWork);

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
    public void Dispatch(IUnitOfWork unitOfWork, IReadOnlyList<EventContext<object>> integrationEvents)
    {
        DispatchAsync(unitOfWork, integrationEvents, CancellationToken.None).GetAwaiter().GetResult();
    }

    // Fail loud rather than dispatch non-atomically. A resource-less unit (BeginAsync() with no db) coordinates
    // nothing transactional, so an outbox write under it would still be autonomous; the guard is on the resource,
    // not on the unit's presence. Surface the mis-wire instead of silently shipping a message a caller rollback
    // can no longer recall.
    private static void _EnsureOwnsATransaction(IUnitOfWork unitOfWork)
    {
        if (unitOfWork.Resource is null)
        {
            throw new InvalidOperationException(HeadlessUnitOfWorkMessages.CallerOwnedTransactionWithoutUnitOfWork);
        }
    }
}
