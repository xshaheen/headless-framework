// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.CommitCoordination;
using Headless.Domain;
using Headless.Messaging;

namespace Headless.EntityFramework;

/// <summary>
/// Default <see cref="IHeadlessOutboxDispatcher"/>: writes integration events to the messaging outbox enlisted
/// in the EF save transaction, so outbox rows persist atomically with the business data.
/// </summary>
/// <remarks>
/// The save pipeline opens a coordinated EF transaction before this dispatcher runs, so a commit coordinator is
/// already ambient. Publishing each event through <see cref="IOutboxBus"/> lets the outbox writer enlist the
/// stored rows on that coordinator; the registered EF transaction interceptor dispatches them to the broker
/// post-commit and discards them on rollback. This dispatcher therefore only fans the events out to the bus.
/// Register via <c>AddHeadlessDbContextServices(...).AddIntegrationEventOutbox()</c>. Requires a messaging
/// setup (<c>AddHeadlessMessaging</c>) with an outbox storage provider.
/// </remarks>
internal sealed class OutboxIntegrationEventDispatcher(
    IOutboxBus outboxBus,
    ICurrentCommitCoordinator currentCommitCoordinator,
    IntegrationEventPublishInvokerCache invokerCache
) : IHeadlessOutboxDispatcher
{
    public async Task DispatchAsync(
        IReadOnlyList<IIntegrationEvent> integrationEvents,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(integrationEvents);

        // An empty list can't dispatch anything non-atomically, so the coordination guard only matters when there
        // is real work — bail before it.
        if (integrationEvents.Count == 0)
        {
            return;
        }

        _EnsureCoordinated();

        foreach (var integrationEvent in integrationEvents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var publish = invokerCache.GetPublishInvoker(integrationEvent.GetType());
            await publish(outboxBus, integrationEvent, cancellationToken).ConfigureAwait(false);
        }
    }

    // Single contained sync-over-async: IOutboxBus only exposes an async publish, and the EF sync save path
    // calls this. No synchronization context is present on the EF save path, so blocking here cannot deadlock.
    public void Dispatch(IReadOnlyList<IIntegrationEvent> integrationEvents)
    {
        DispatchAsync(integrationEvents, CancellationToken.None).GetAwaiter().GetResult();
    }

    // Fail loud rather than dispatch non-atomically. The save pipeline enlists commit coordination only when it
    // owns the transaction (the new-transaction path); when the caller opened the transaction itself, no
    // coordinator is ambient and publishing here would store + enqueue the integration event immediately —
    // breaking the atomic "dispatch on commit, discard on rollback" guarantee. Surface the mis-wire instead of
    // silently shipping a message a caller rollback can no longer recall.
    private void _EnsureCoordinated()
    {
        if (currentCommitCoordinator.Current is not null)
        {
            return;
        }

        throw new InvalidOperationException(
            "Integration events were emitted while saving inside a caller-managed transaction that is not "
                + "enlisted in commit coordination, so the outbox would dispatch non-atomically. Either let the "
                + "Headless save pipeline own the transaction (do not open your own before SaveChanges), or call "
                + "Database.EnlistCommitCoordination(transaction, services) on your transaction before saving."
        );
    }
}
