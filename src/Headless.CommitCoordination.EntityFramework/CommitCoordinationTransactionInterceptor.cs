// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

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
/// is a no-op for uncoordinated transactions. Drain faults never escape these edges: the transaction outcome is
/// already durable when they fire, so a propagating fault would surface a phantom failure (and, inside an EF
/// execution strategy, re-run the delegate and double-apply); faults are logged and the work is relay-recovered —
/// mirroring the sync paths and the PostgreSql/SqlServer helper guards.
/// </remarks>
[PublicAPI]
public sealed partial class CommitCoordinationTransactionInterceptor(
    EntityFrameworkCommitSignalSource signalSource,
    ILogger<CommitCoordinationTransactionInterceptor>? logger = null
) : DbTransactionInterceptor
{
    private readonly ILogger _logger = logger ?? NullLogger<CommitCoordinationTransactionInterceptor>.Instance;

    /// <inheritdoc />
    public override void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData)
    {
        signalSource.SignalCommittedInBackground(transaction);
    }

    /// <inheritdoc />
    public override async Task TransactionCommittedAsync(
        DbTransaction transaction,
        TransactionEndEventData eventData,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            await signalSource.SignalCommittedAsync(transaction, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // The commit is ALREADY durable; the drain is acceleration. Propagating would fail the caller (or
            // re-run an execution-strategy delegate) for committed work — log and let the relay recover.
            LogPostCommitDrainFaulted(_logger, ex);
        }
    }

    /// <inheritdoc />
    public override void TransactionRolledBack(DbTransaction transaction, TransactionEndEventData eventData)
    {
        signalSource.SignalRolledBackInBackground(transaction);
    }

    /// <inheritdoc />
    public override async Task TransactionRolledBackAsync(
        DbTransaction transaction,
        TransactionEndEventData eventData,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            await signalSource.SignalRolledBackAsync(transaction, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // The rollback already happened and the enlisted work is discarded either way; a rollback-callback
            // fault must not replace the caller's rollback flow with a phantom error.
            LogPostRollbackDrainFaulted(_logger, ex);
        }
    }

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Error,
        Message = "Post-commit drain faulted after a durable EF Core commit; the relay will recover any uncommitted work."
    )]
    private static partial void LogPostCommitDrainFaulted(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Error,
        Message = "Rollback drain faulted after an EF Core rollback; the enlisted work was already discarded."
    )]
    private static partial void LogPostRollbackDrainFaulted(ILogger logger, Exception exception);
}
