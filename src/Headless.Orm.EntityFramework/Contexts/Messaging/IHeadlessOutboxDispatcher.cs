// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Domain;
using Microsoft.EntityFrameworkCore.Storage;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.EntityFramework;

/// <summary>
/// Transactionally enqueues integration events collected during EF saves into the outbox.
/// </summary>
/// <remarks>
/// Invoked after entities persist but before the EF transaction commits, and may be retried by the EF
/// execution strategy. Implementations MUST NOT perform non-transactional external broker publishes from
/// these methods. Enlist the events in <c>currentTransaction</c> (for example, a transactional outbox),
/// or make enqueueing idempotent by each event <c>UniqueId</c> and publish to external infrastructure
/// only after the transaction commits. The real implementation ships in the
/// <c>Headless.Orm.EntityFramework.Messaging</c> bridge package; register it with
/// <c>AddHeadlessDbContextServices(...).AddIntegrationEventOutbox()</c>.
/// </remarks>
public interface IHeadlessOutboxDispatcher
{
    /// <summary>Enqueues integration events into transaction-bound storage for post-commit delivery.</summary>
    Task DispatchAsync(
        IReadOnlyList<IIntegrationEvent> integrationEvents,
        IDbContextTransaction currentTransaction,
        CancellationToken cancellationToken
    );

    /// <summary>Enqueues integration events into transaction-bound storage for post-commit delivery.</summary>
    void Dispatch(IReadOnlyList<IIntegrationEvent> integrationEvents, IDbContextTransaction currentTransaction);
}
