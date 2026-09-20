// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Domain;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.EntityFramework;

/// <summary>
/// Transactionally enqueues integration events collected during EF saves into the outbox.
/// </summary>
/// <remarks>
/// Preserve each occurrence's captured identity and business lineage, including absent causation and tenant,
/// across persistence retries. The Messaging bridge uses the occurrence EventId as the logical MessageId.
/// Invoked after entities persist but before the EF transaction commits, and may be retried by the EF
/// execution strategy. The save pipeline makes its transaction the current unit of work in the context's
/// scope before invoking this. Resolve that scope's <c>IUnitOfWorkManager.Current</c> and publish each event
/// through <c>unit.Outbox.PublishAsync</c> from <c>Headless.Messaging.UnitOfWork</c>, so its outbox rows join the
/// transaction — dispatched post-commit and discarded on rollback. <c>IBus</c> publishes autonomously even
/// with durable delivery, so its rows would survive a rollback. Implementations MUST NOT perform
/// non-transactional external broker publishes from these methods. (A custom implementation that needs the raw transaction handle can read
/// <c>DbContext.Database.CurrentTransaction</c>.) The real implementation ships in the
/// <c>Headless.EntityFramework.Messaging</c> bridge package; register it with
/// <c>AddHeadlessDbContextServices(...).AddIntegrationEventOutbox()</c>.
/// </remarks>
[PublicAPI]
public interface IHeadlessOutboxDispatcher
{
    /// <summary>Enqueues integration events into transaction-bound storage for post-commit delivery.</summary>
    Task DispatchAsync(
        IReadOnlyList<EventContext<object>> integrationEvents,
        CancellationToken cancellationToken = default
    );

    /// <summary>Enqueues integration events into transaction-bound storage for post-commit delivery.</summary>
    void Dispatch(IReadOnlyList<EventContext<object>> integrationEvents);
}
