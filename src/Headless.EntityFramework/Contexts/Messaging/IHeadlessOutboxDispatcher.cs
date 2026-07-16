// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Domain;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.EntityFramework;

/// <summary>
/// Transactionally enqueues integration events collected during EF saves into the outbox.
/// </summary>
/// <remarks>
/// Invoked after entities persist but before the EF transaction commits, and may be retried by the EF
/// execution strategy. The save pipeline opens a <b>commit-coordinated</b> transaction before invoking this,
/// so the commit coordinator is already ambient: publish each event through the messaging outbox
/// (<c>IOutboxBus</c>) and its outbox rows enlist on that transaction automatically — dispatched post-commit
/// and discarded on rollback. Implementations MUST NOT perform non-transactional external broker publishes
/// from these methods. (A custom implementation that needs the raw transaction handle can read
/// <c>DbContext.Database.CurrentTransaction</c>.) The real implementation ships in the
/// <c>Headless.EntityFramework.Messaging</c> bridge package; register it with
/// <c>AddHeadlessDbContextServices(...).AddIntegrationEventOutbox()</c>.
/// </remarks>
[PublicAPI]
public interface IHeadlessOutboxDispatcher
{
    /// <summary>Enqueues integration events into transaction-bound storage for post-commit delivery.</summary>
    Task DispatchAsync(
        IReadOnlyList<IIntegrationEvent> integrationEvents,
        CancellationToken cancellationToken = default
    );

    /// <summary>Enqueues integration events into transaction-bound storage for post-commit delivery.</summary>
    void Dispatch(IReadOnlyList<IIntegrationEvent> integrationEvents);
}
