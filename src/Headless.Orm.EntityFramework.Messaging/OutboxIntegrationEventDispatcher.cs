// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Domain;
using Headless.Messaging;
using Microsoft.EntityFrameworkCore.Storage;

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
    IntegrationEventPublishInvokerCache invokerCache
) : IHeadlessOutboxDispatcher
{
    public async Task DispatchAsync(
        IReadOnlyList<IIntegrationEvent> integrationEvents,
        // currentTransaction is unused — the coordinated transaction the pipeline opened makes the commit
        // coordinator ambient, so the outbox writer enlists without this dispatcher touching the transaction.
        // The parameter stays for the IHeadlessOutboxDispatcher contract.
        IDbContextTransaction currentTransaction,
        CancellationToken cancellationToken
    )
    {
        Argument.IsNotNull(integrationEvents);
        Argument.IsNotNull(currentTransaction);

        if (integrationEvents.Count == 0)
        {
            return;
        }

        foreach (var integrationEvent in integrationEvents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var publish = invokerCache.GetPublishInvoker(integrationEvent.GetType());
            await publish(outboxBus, integrationEvent, cancellationToken).ConfigureAwait(false);
        }
    }

    // Single contained sync-over-async: IOutboxBus only exposes an async publish, and the EF sync save path
    // calls this. No synchronization context is present on the EF save path, so blocking here cannot deadlock.
    public void Dispatch(
        IReadOnlyList<IIntegrationEvent> integrationEvents,
        IDbContextTransaction currentTransaction
    ) => DispatchAsync(integrationEvents, currentTransaction, CancellationToken.None).GetAwaiter().GetResult();
}
