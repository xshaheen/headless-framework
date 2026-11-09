// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Fencing;
using Headless.Jobs.Internal;
using Microsoft.EntityFrameworkCore;

namespace Headless.Jobs.Infrastructure;

/// <summary>
/// The lease transitions behind the writes a run makes on a row its claim granted: each write is fenced on the claim's
/// generation, and the transition it rides is chosen from the status the row ends in.
/// </summary>
/// <remarks>
/// Every out-of-band write that ends a run (a cancel, a delete, an edit, a sweep) also ends the lease in the same
/// statement, so a current generation implies a non-terminal row. That is why a settle or release, which take no
/// condition of their own, need no status check.
/// </remarks>
internal static class JobsClaimedWrite
{
    /// <summary>Applies <paramref name="set"/> on <paramref name="row"/> through the transition <paramref name="state"/> calls for.</summary>
    /// <returns>Whether the row took the write.</returns>
    public static async Task<bool> ApplyAsync(
        FencedRowTable<Guid> table,
        DbContext context,
        FencedRow<Guid> row,
        JobExecutionState state,
        JobsRowSet set,
        CancellationToken cancellationToken
    )
    {
        var connection = JobsLeaseRows.Connection(context);
        var transaction = JobsLeaseRows.Transaction(context);
        row = JobsLeaseRows.On(table, row);
        var writesStatus = state.PropertiesToUpdate.Contains(nameof(JobExecutionState.Status));

        // A terminal status ends the run, so it ends the lease as settled; a retry of the same settlement reports
        // success, so a write whose commit landed but whose answer was lost does not read as refused.
        if (writesStatus && ChainRunConditionRules.IsTerminal(state.Status))
        {
            var settled = await FencedRows
                .SettleAsync(table, connection, transaction, row, set.ToWrite(), cancellationToken)
                .ConfigureAwait(false);

            return settled == LeaseSettlementStatus.Settled;
        }

        // Back to Idle, or an explicit lock release, hands the row back so the next claim can take it at once.
        if (
            (writesStatus && state.Status == JobStatus.Idle)
            || state.PropertiesToUpdate.Contains(nameof(JobExecutionState.ReleaseLock))
        )
        {
            var released = await FencedRows
                .ReleaseAsync(table, connection, transaction, row, set.ToWrite(), cancellationToken)
                .ConfigureAwait(false);

            return released == LeaseSettlementStatus.Released;
        }

        // A start must find the row still Queued, so a duplicate start of the same claim cannot restart a running row.
        var condition =
            writesStatus && state.Status == JobStatus.InProgress
                ? $"{set.Column(nameof(TimeJobEntity.Status))} = {set.Parameter(nameof(TimeJobEntity.Status), JobStatus.Queued)}"
                : null;
        var written = await FencedRows
            .WriteAsync<Guid, bool>(
                table,
                connection,
                transaction,
                row,
                set.ToWrite(condition),
                static (_, _) => ValueTask.FromResult(true),
                cancellationToken
            )
            .ConfigureAwait(false);

        return written.IsAccepted;
    }

    /// <summary>
    /// Ends an interrupted run by its death policy: Retry hands the row back to Idle, due again after
    /// <paramref name="retryAfter"/>; MarkFailed and Skip settle it with <paramref name="reason"/>.
    /// </summary>
    /// <returns>1 when the row took the write, else 0.</returns>
    public static async Task<int> EndInterruptedAsync(
        FencedRowTable<Guid> table,
        DbContext context,
        FencedRow<Guid> row,
        NodeDeathPolicy policy,
        JobsRowSet set,
        string reason,
        TimeSpan? retryAfter,
        CancellationToken cancellationToken
    )
    {
        var connection = JobsLeaseRows.Connection(context);
        var transaction = JobsLeaseRows.Transaction(context);
        row = JobsLeaseRows.On(table, row);

        if (policy == NodeDeathPolicy.Retry)
        {
            set.Value(nameof(TimeJobEntity.Status), JobStatus.Idle);

            if (retryAfter is { } remaining)
            {
                set.DatabaseNowPlus(nameof(TimeJobEntity.ExecutionTime), remaining);
            }

            set.DatabaseNow(nameof(TimeJobEntity.UpdatedAt));

            var released = await FencedRows
                .ReleaseAsync(table, connection, transaction, row, set.ToWrite(), cancellationToken)
                .ConfigureAwait(false);

            return released == LeaseSettlementStatus.Released ? 1 : 0;
        }

        if (policy == NodeDeathPolicy.MarkFailed)
        {
            set.Value(nameof(TimeJobEntity.Status), JobStatus.Failed)
                .Value(nameof(TimeJobEntity.ExceptionMessage), reason);
        }
        else
        {
            set.Value(nameof(TimeJobEntity.Status), JobStatus.Skipped)
                .Value(nameof(TimeJobEntity.SkippedReason), reason);
        }

        set.DatabaseNow(nameof(TimeJobEntity.ExecutedAt)).DatabaseNow(nameof(TimeJobEntity.UpdatedAt));

        var settled = await FencedRows
            .SettleAsync(table, connection, transaction, row, set.ToWrite(), cancellationToken)
            .ConfigureAwait(false);

        return settled == LeaseSettlementStatus.Settled ? 1 : 0;
    }

    /// <summary>Slides the lease of a running row forward by <paramref name="duration"/> from the database clock.</summary>
    /// <returns>1 when the lease was renewed, else 0.</returns>
    public static async Task<int> RenewAsync(
        FencedRowTable<Guid> table,
        DbContext context,
        FencedRow<Guid> row,
        TimeSpan duration,
        CancellationToken cancellationToken
    )
    {
        var renewed = await FencedRows
            .RenewAsync(
                table,
                JobsLeaseRows.Connection(context),
                JobsLeaseRows.Transaction(context),
                JobsLeaseRows.On(table, row),
                duration,
                cancellationToken
            )
            .ConfigureAwait(false);

        return renewed.Status == LeaseRenewalStatus.Renewed ? 1 : 0;
    }

    /// <summary>Writes <paramref name="set"/> on <paramref name="row"/> while its generation is current and it is running, keeping the lease.</summary>
    public static async Task<bool> WriteRunningAsync(
        FencedRowTable<Guid> table,
        DbContext context,
        FencedRow<Guid> row,
        JobsRowSet set,
        CancellationToken cancellationToken
    )
    {
        var condition =
            $"{set.Column(nameof(TimeJobEntity.Status))} = {set.Parameter(nameof(TimeJobEntity.Status), JobStatus.InProgress)}";
        var written = await FencedRows
            .WriteAsync<Guid, bool>(
                table,
                JobsLeaseRows.Connection(context),
                JobsLeaseRows.Transaction(context),
                JobsLeaseRows.On(table, row),
                set.ToWrite(condition),
                static (_, _) => ValueTask.FromResult(true),
                cancellationToken
            )
            .ConfigureAwait(false);

        return written.IsAccepted;
    }
}
