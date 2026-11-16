// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.UnitOfWork;

namespace Headless.Messaging;

/// <summary>
/// A run of broadcast publishes into one unit's outbox whose rows the storage may write together, in one round trip,
/// when <see cref="FlushAsync" /> runs.
/// </summary>
/// <remarks>
/// <para>
/// Meant for a caller that holds every message of the run at once, such as the integration events of one save, and
/// flushes before it hands control back, so a store failure still surfaces inside that caller and before the unit
/// commits. Rows are written in publish order. A publish that publish middleware wraps, or that the storage cannot
/// batch, first writes the rows held so far and then stores its own row as an ordinary enlisted publish, so its
/// middleware sees only its own store, as without a batch.
/// </para>
/// <para>
/// Disposing without a flush drops the rows not yet written. That happens only when the run already failed, and the
/// unit then rolls back what it had written.
/// </para>
/// </remarks>
internal interface IOutboxStoreBatch : IAsyncDisposable
{
    /// <summary>Publishes a broadcast (bus lane) message into the batch's unit, as <c>unit.Outbox.PublishAsync</c> does.</summary>
    /// <returns>A receipt with the resolved message identity and the identifier its row is, or will be, stored under.</returns>
    Task<PublishReceipt> PublishAsync<T>(T? contentObj, OutboxOptions? options, CancellationToken cancellationToken);

    /// <summary>Writes the rows held so far, in publish order, and hands them to the unit's post-commit dispatch.</summary>
    Task FlushAsync(CancellationToken cancellationToken);
}

/// <summary>Implemented by the outbox feature that can open an <see cref="IOutboxStoreBatch" /> on a unit.</summary>
internal interface IUnitOfWorkOutboxBatching
{
    IOutboxStoreBatch BeginStoreBatch(IUnitOfWork unitOfWork);
}
