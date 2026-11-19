// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Globalization;
using Headless.Checks;
using Headless.Fencing;
using Headless.Jobs.Internal;
using Headless.Sql;
using Headless.Sql.PostgreSql;
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

        // A start must find the row not yet running, so a duplicate start of the same claim cannot restart it. The SQL
        // restates ChainRunConditionRules.CanStart: a claimed row is Queued, and a chain child claimed under its root's
        // lease is still Idle. Cron occurrences have no parent column and are always claimed Queued.
        var condition = writesStatus && state.Status == JobStatus.InProgress ? _StartCondition(set, state.Type) : null;
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

    private static string _StartCondition(JobsRowSet set, JobType type)
    {
        var status = set.Column(nameof(TimeJobEntity.Status));
        var queued = $"{status} = {set.Parameter(nameof(TimeJobEntity.Status), JobStatus.Queued)}";

        if (type != JobType.TimeJob)
        {
            return queued;
        }

        var idle = set.Parameter(nameof(TimeJobEntity.Status), JobStatus.Idle);

        return $"({queued} OR ({status} = {idle} AND {set.Column(nameof(TimeJobEntity.ParentId))} IS NOT NULL))";
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

    // Not "Lease…": FencedRows reserves that prefix for its own parameters.
    private const string _RenewDurationParameter = "JobsRenewDuration";

    private static readonly string _ActiveLiteral = ((short)RowLeaseState.Active).ToString(
        CultureInfo.InvariantCulture
    );

    /// <summary>
    /// Slides the lease of a running row forward by <paramref name="duration"/> from the database clock, in one
    /// statement that commits on its own.
    /// </summary>
    /// <remarks>
    /// Every running job renews on each cadence, so the renewal is a single fenced <c>UPDATE</c> rather than a probe
    /// plus <see cref="FencedRows.RenewAsync{TKey}"/>'s transaction around a locking read: its <c>WHERE</c> carries the
    /// whole fence (the claim's generation, an active lease not yet expired by the database clock, and the row still
    /// <see cref="JobStatus.InProgress"/>), so the check and the write are one decision under the row's lock. A refused
    /// renewal reports no reason, which a renewal caller never needs: any refusal is a lost lease. Success is read from
    /// the row the statement returns, not an affected count that a dialect's clock prelude could inflate.
    /// </remarks>
    /// <returns>1 when the lease was renewed, else 0.</returns>
    public static async Task<int> RenewRunningAsync<TEntity>(
        ISqlDialect dialect,
        DbContext context,
        FencedRow<Guid> row,
        TimeSpan duration,
        CancellationToken cancellationToken
    )
        where TEntity : class
    {
        Argument.IsPositive(duration);

        var columns = JobsLeaseGrant.Columns<TEntity>(context);
        var set = JobsRowSet.For<TEntity>(context);
        var id = set.Parameter(nameof(TimeJobEntity.Id), row.Key);
        var generation = set.LeaseParameter(nameof(RowLeaseSnapshot.Generation), row.Generation);
        var running = set.Parameter(nameof(TimeJobEntity.Status), JobStatus.InProgress);
        var deadline = dialect.ShiftByDuration(SqlDialectTokens.Now, _RenewDurationParameter);
        var where =
            $"{columns.Id} = {id} AND {columns.Generation} = {generation}"
            + $" AND {columns.State} = {_ActiveLiteral} AND {columns.ExpiresAt} > {SqlDialectTokens.Now}"
            + $" AND {set.Column(nameof(TimeJobEntity.Status))} = {running}";
        var sql =
            dialect is PostgreSqlDialect || JobsSqlDialects.IsSqlite(dialect)
                ? $"UPDATE {columns.Table} SET {columns.ExpiresAt} = {deadline} WHERE {where} RETURNING {columns.Id};"
                : $"UPDATE {columns.Table} SET {columns.ExpiresAt} = {deadline} OUTPUT inserted.{columns.Id} WHERE {where};";

        var connection = JobsLeaseRows.Connection(context);
        var openedHere = connection.State != ConnectionState.Open;

        if (openedHere)
        {
            await context.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.Transaction = JobsLeaseRows.Transaction(context);
#pragma warning disable CA2100 // SQL identifiers are provider-delimited EF metadata; runtime values are parameters.
            command.CommandText = dialect.Render(new SqlClockedStatement(sql));
#pragma warning restore CA2100
            // Only the bindings: a renewal writes the expiry alone, so it does not stamp the change marker and a
            // running job does not read as changed on every tick.
            set.ToWrite().Bind?.Invoke(command);
            dialect.AddDuration(command, _RenewDurationParameter, duration);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

            return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? 1 : 0;
        }
        finally
        {
            if (openedHere)
            {
                await context.Database.CloseConnectionAsync().ConfigureAwait(false);
            }
        }
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
