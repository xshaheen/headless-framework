// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Headless.CommitCoordination.EntityFramework;

/// <summary>
/// EF Core <see cref="DbTransactionInterceptor" /> that bridges the post-commit and post-rollback transaction
/// edges to the commit coordination infrastructure.
/// </summary>
/// <remarks>
/// Registered on the <c>DbContext</c> options (automatically by the Headless ORM setup, or manually via
/// <c>AddInterceptors</c> for plain <c>AddDbContext</c> registrations). When a transaction that was enrolled
/// via <c>DatabaseFacade.EnlistCommitCoordination</c> commits or rolls back, the interceptor drives the
/// corresponding signal on <see cref="EntityFrameworkCommitSignalSource" />, which drains registered callbacks.
/// <para>
/// The synchronous overrides (<see cref="TransactionCommitted" />, <see cref="TransactionRolledBack" />) fire
/// the signal in the background (fire-and-forget) so the EF commit/rollback call does not block on the drain.
/// The async overrides await the drain directly.
/// </para>
/// <para>
/// Drain faults are logged and swallowed here: by the time these methods fire, the transaction outcome is
/// already durable. Propagating a drain fault would surface a phantom failure to the caller (and, inside an EF
/// execution strategy, cause the operation to be retried and double-applied). The enlisted durable rows are
/// relay-recoverable.
/// </para>
/// <para>
/// The interceptor is a no-op for transactions that were never enrolled in commit coordination — absent keys
/// are silently ignored by <see cref="EntityFrameworkCommitSignalSource" />.
/// </para>
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
