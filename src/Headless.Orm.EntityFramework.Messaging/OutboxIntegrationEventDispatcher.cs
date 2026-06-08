// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Domain;
using Headless.AmbientTransactions;
using Headless.Messaging;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.EntityFramework;

/// <summary>
/// Default <see cref="IHeadlessOutboxDispatcher"/>: writes integration events to the messaging outbox enlisted
/// in the EF save transaction, so outbox rows persist atomically with the business data. The events are buffered
/// (not published to the broker) inside the dispatch; the EF pipeline commits the transaction and the outbox
/// relay — or, on SQL Server, the connection-commit diagnostic — dispatches the rows post-commit.
/// </summary>
/// <remarks>
/// Register via <c>AddHeadlessDbContextServices(...).AddIntegrationEventOutbox()</c>. Requires a messaging
/// setup (<c>AddHeadlessMessaging</c>) with an outbox storage provider.
/// </remarks>
internal sealed class OutboxIntegrationEventDispatcher(
    IServiceProvider serviceProvider,
    IOutboxBus outboxBus,
    IntegrationEventPublishInvokerCache invokerCache
) : IHeadlessOutboxDispatcher
{
    public async Task DispatchAsync(
        IReadOnlyList<IIntegrationEvent> integrationEvents,
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

        // A fresh transient outbox transaction per dispatch keeps the in-memory buffer scoped to this save
        // (no cross-save accumulation). Attaching the EF transaction sets the ambient accessor the outbox
        // writer reads; AutoCommit = false buffers the rows instead of dispatching to the broker in-band.
        var outboxTransaction = serviceProvider.GetRequiredService<IAmbientTransaction>();
        outboxTransaction.DbTransaction = currentTransaction;
        outboxTransaction.AutoCommit = false;

        try
        {
            foreach (var integrationEvent in integrationEvents)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var publish = invokerCache.GetPublishInvoker(integrationEvent.GetType());
                await publish(outboxBus, integrationEvent, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            // Detach WITHOUT disposing outboxTransaction here — this is deliberate, do not "fix" it with
            // `await using`/`Dispose`. On SQL Server the provider registers the same outboxTransaction instance
            // in the diagnostic TransBuffer and the connection-commit diagnostic drains + disposes it AFTER the
            // EF transaction commits; disposing it now would tear it down before the post-commit flush and drop
            // the integration events. The EF pipeline owns currentTransaction's commit/dispose lifecycle, and the
            // transient outboxTransaction is released at DI scope end. Nulling DbTransaction also clears the
            // ambient accessor so it never points at a committed transaction after this dispatch returns.
            outboxTransaction.DbTransaction = null;
        }
    }

    // Single contained sync-over-async: IOutboxBus only exposes an async publish, and the EF sync save path
    // calls this. No synchronization context is present on the EF save path, so blocking here cannot deadlock.
    public void Dispatch(
        IReadOnlyList<IIntegrationEvent> integrationEvents,
        IDbContextTransaction currentTransaction
    ) => DispatchAsync(integrationEvents, currentTransaction, CancellationToken.None).GetAwaiter().GetResult();
}
