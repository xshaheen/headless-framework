// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Headless.CommitCoordination.EntityFramework;

/// <summary>
/// Bridges EF Core's true post-commit/rollback transaction edges to the commit coordinator. Registered on the
/// DbContext so a transaction enlisted via <c>DatabaseFacade.EnlistCommitCoordination</c> drains its enlisted
/// work when the EF transaction commits, and discards it when the transaction rolls back.
/// </summary>
/// <remarks>
/// Keyed off <see cref="IDbTransactionInterceptor.TransactionCommitted" /> (true post-commit on explicit
/// transactions), not <c>ISaveChangesInterceptor.SavedChanges</c> which fires before an explicit
/// <c>CommitAsync</c>. The signal source silently ignores transactions it never attached, so this interceptor
/// is a no-op for uncoordinated transactions.
/// </remarks>
[PublicAPI]
public sealed class CommitCoordinationTransactionInterceptor(EntityFrameworkCommitSignalSource signalSource)
    : DbTransactionInterceptor
{
    /// <inheritdoc />
    public override void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData)
    {
        signalSource.SignalCommittedAsync(transaction, CancellationToken.None).AsTask().GetAwaiter().GetResult();
    }

    /// <inheritdoc />
    public override async Task TransactionCommittedAsync(
        DbTransaction transaction,
        TransactionEndEventData eventData,
        CancellationToken cancellationToken = default
    )
    {
        await signalSource.SignalCommittedAsync(transaction, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override void TransactionRolledBack(DbTransaction transaction, TransactionEndEventData eventData)
    {
        signalSource.SignalRolledBackAsync(transaction, CancellationToken.None).AsTask().GetAwaiter().GetResult();
    }

    /// <inheritdoc />
    public override async Task TransactionRolledBackAsync(
        DbTransaction transaction,
        TransactionEndEventData eventData,
        CancellationToken cancellationToken = default
    )
    {
        await signalSource.SignalRolledBackAsync(transaction, cancellationToken).ConfigureAwait(false);
    }
}
