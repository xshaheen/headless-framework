// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Domain;
using Headless.UnitOfWork;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.EntityFramework;

/// <summary>
/// Transactionally enqueues integration events collected during EF saves into the outbox.
/// </summary>
/// <remarks>
/// Preserve each occurrence's captured identity and business lineage, including absent causation and tenant,
/// across persistence retries. The Messaging bridge uses the occurrence EventId as the logical MessageId.
/// Invoked after entities persist but before the EF transaction commits, and may be retried by the EF
/// execution strategy. The save pipeline hands over the unit of work that owns the save's transaction — its
/// own, or the caller's begun with <c>IUnitOfWorkFactory.BeginAsync(db)</c> / <c>RunAsync(db, …)</c>; publish
/// each event through that unit's <c>Outbox</c> so its outbox rows join the transaction — dispatched
/// post-commit and discarded on rollback. <c>IBus</c> publishes autonomously even with durable delivery, so
/// its rows would survive a rollback. Implementations MUST NOT perform non-transactional external broker
/// publishes from these methods. (A custom implementation that needs the raw transaction handle can read
/// <c>DbContext.Database.CurrentTransaction</c>.) The real implementation ships in the
/// <c>Headless.EntityFramework.Messaging</c> bridge package; register it with
/// <c>AddHeadlessDbContextServices(...).AddIntegrationEventOutbox()</c>.
/// </remarks>
[PublicAPI]
public interface IHeadlessOutboxDispatcher
{
    /// <summary>Enqueues integration events into transaction-bound storage for post-commit delivery.</summary>
    /// <param name="unitOfWork">The unit of work that owns the save's transaction; every row must enlist in it.</param>
    /// <param name="integrationEvents">The occurrences collected during the save.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task DispatchAsync(
        IUnitOfWork unitOfWork,
        IReadOnlyList<EventContext<object>> integrationEvents,
        CancellationToken cancellationToken = default
    );

    /// <summary>Enqueues integration events into transaction-bound storage for post-commit delivery.</summary>
    /// <param name="unitOfWork">The unit of work that owns the save's transaction; every row must enlist in it.</param>
    /// <param name="integrationEvents">The occurrences collected during the save.</param>
    void Dispatch(IUnitOfWork unitOfWork, IReadOnlyList<EventContext<object>> integrationEvents);
}
