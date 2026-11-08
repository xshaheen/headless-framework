// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Data.Common;
using System.Globalization;
using Headless.Checks;
using Headless.Sql;

namespace Headless.Fencing;

/// <summary>
/// Lease operations on a consumer's own table (<see cref="FencedRowTable{TKey}" />), run on the caller's connection and,
/// optionally, its transaction. The lease lives in five columns of the row it guards, so the work's own final write
/// and the lease's end are one fenced statement.
/// </summary>
/// <remarks>
/// <para>
/// Every operation on one row is the lease store's shape: a locking read of the row, then one conditional
/// <c>UPDATE</c> whose <c>WHERE</c> is the fence, in one batch. The database clock is read after the lock, once, by the
/// statement that decides, so a lock wait can never let an expired generation through. A refusal is classified from the
/// row the locking read found.
/// </para>
/// <para>
/// With a <see langword="null" /> transaction, an operation that writes opens a READ COMMITTED transaction on the
/// caller's connection and commits it before returning, opening the connection first if it is closed and closing it
/// again afterwards. With a transaction, it enlists and never commits: the caller's unit decides. The status read never
/// opens a transaction.
/// </para>
/// <para>
/// Nothing here registers in dependency injection or retries: the consumer's table is reached through the consumer's
/// own connection, and a failed statement has already rolled back whatever transaction it ran in.
/// </para>
/// </remarks>
#pragma warning disable CA2100 // SQL text is rendered from validated identifiers, dialect statements, and the consumer's trusted fragments; values are parameters.
[PublicAPI]
public static class FencedRows
{
    #region Grant and claim

    /// <summary>
    /// Grants the row to a new attempt when no live attempt holds it: a new generation, active until
    /// <paramref name="duration" /> from the database clock.
    /// </summary>
    /// <remarks>
    /// A grant whose <see cref="LeaseGrantOptions.Holder" /> matches the live attempt's holder (ordinal comparison)
    /// returns that attempt as <see cref="LeaseGrantStatus.Granted" /> and writes nothing, so a grant retried after a
    /// lost response is idempotent.
    /// </remarks>
    /// <returns>The grant's outcome, or <see langword="null" /> when no row has the key: the helper never inserts a row.</returns>
    public static async ValueTask<FencedRowGrantResult<TKey>?> GrantAsync<TKey>(
        FencedRowTable<TKey> table,
        DbConnection connection,
        DbTransaction? transaction,
        TKey key,
        TimeSpan duration,
        LeaseGrantOptions options,
        CancellationToken cancellationToken = default
    )
        where TKey : notnull
    {
        Argument.IsNotNull(table);
        Argument.IsNotNull(key);
        Argument.IsPositive(duration);
        LeaseRequestResolver.ValidateGrantOptions(options, table.HolderMaxLength);

        return await _RunAsync(
                table,
                connection,
                transaction,
                (tx, ct) => _GrantAsync(table, connection, tx, key, duration, options, ct),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Claims up to <see cref="FencedRowClaim.BatchSize" /> rows the claim's filter selects that no live attempt holds,
    /// granting each under its own new generation in one statement.
    /// </summary>
    /// <remarks>
    /// PostgreSQL and SQL Server skip rows another transaction has locked, so concurrent claimers never wait on each
    /// other. SQLite has no row locks: a claim takes the database write lock, so claimers run one after another and
    /// wait up to the connection's busy timeout.
    /// </remarks>
    /// <returns>A token for each claimed row, in no particular order; empty when nothing was claimable.</returns>
    public static async ValueTask<IReadOnlyList<FencedRow<TKey>>> ClaimAsync<TKey>(
        FencedRowTable<TKey> table,
        DbConnection connection,
        DbTransaction? transaction,
        FencedRowClaim claim,
        CancellationToken cancellationToken = default
    )
        where TKey : notnull
    {
        Argument.IsNotNull(table);
        Argument.IsNotNull(claim);
        Argument.IsNotNullOrWhiteSpace(claim.Filter, paramName: nameof(claim));
        Argument.IsNotNullOrEmpty(claim.OrderBy, paramName: nameof(claim));

        foreach (var order in claim.OrderBy)
        {
            Argument.IsNotNullOrWhiteSpace(order, paramName: nameof(claim));
        }

        Argument.IsPositive(claim.BatchSize, paramName: nameof(claim));
        Argument.IsPositive(claim.Duration, paramName: nameof(claim));
        LeaseRequestResolver.ValidateGrantOptions(
            new LeaseGrantOptions { Holder = claim.Holder, Takeover = claim.Takeover },
            table.HolderMaxLength
        );

        var fence = claim.Takeover == LeaseTakeover.AfterSweep ? table.AfterSweepFence : table.GrantableFence;
        var sql = table.Dialect.Render(
            new SqlClaimNext(
                table.Table,
                table.KeyColumns,
                $"({claim.Filter}) AND {fence}",
                claim.OrderBy,
                table.GrantAssignments(
                    table.Dialect.ShiftByDuration(SqlDialectTokens.Now, FencedRowTable<TKey>.DurationParameter),
                    FencedRowTable<TKey>.HolderParameter
                ),
                [.. table.KeyColumns, table.Generation],
                FencedRowTable<TKey>.BatchSizeParameter
            )
        );

        return await _RunAsync(
                table,
                connection,
                transaction,
                async (tx, ct) =>
                {
                    await using var command = _Command(connection, tx, sql);
                    table.Dialect.AddDuration(command, FencedRowTable<TKey>.DurationParameter, claim.Duration);
                    _AddHolder(table, command, claim.Holder);
                    table.Dialect.AddParameter(
                        command,
                        FencedRowTable<TKey>.BatchSizeParameter,
                        SqlColumnType.Int32,
                        claim.BatchSize
                    );
                    _BindConsumer(command, claim.Bind, nameof(claim));

                    await using var reader = await _ReaderAsync(command, ct).ConfigureAwait(false);
                    var generationOrdinal = table.Key.Parts.Count;
                    var claimed = new List<FencedRow<TKey>>();

                    while (await reader.ReadAsync(ct).ConfigureAwait(false))
                    {
                        claimed.Add(
                            new FencedRow<TKey>(
                                table.Name,
                                table.Key.Read(reader, 0),
                                reader.GetInt64(generationOrdinal)
                            )
                        );
                    }

                    return (IReadOnlyList<FencedRow<TKey>>)claimed;
                },
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Returns the grant assignments for a statement the consumer writes, with parameters named under
    /// <paramref name="prefix" />.
    /// </summary>
    /// <param name="table">The table the statement updates.</param>
    /// <param name="prefix">The parameter-name prefix: a letter or underscore, then letters, digits, or underscores.</param>
    /// <returns>The grant scope.</returns>
    /// <exception cref="ArgumentException"><paramref name="prefix" /> is not a parameter-name identifier.</exception>
    public static FencedRowGrantScope<TKey> GrantScope<TKey>(FencedRowTable<TKey> table, string prefix)
        where TKey : notnull
    {
        Argument.IsNotNull(table);
        Argument.IsNotNullOrWhiteSpace(prefix);

        if (
            !(char.IsAsciiLetter(prefix[0]) || prefix[0] == '_')
            || !prefix.All(static c => char.IsAsciiLetterOrDigit(c) || c == '_')
        )
        {
            throw new ArgumentException(
                "A grant scope's prefix becomes part of parameter names, so it must be a letter or underscore followed "
                    + "by letters, digits, or underscores.",
                nameof(prefix)
            );
        }

        return new FencedRowGrantScope<TKey>(table, prefix);
    }

    private static async Task<FencedRowGrantResult<TKey>?> _GrantAsync<TKey>(
        FencedRowTable<TKey> table,
        DbConnection connection,
        DbTransaction transaction,
        TKey key,
        TimeSpan duration,
        LeaseGrantOptions options,
        CancellationToken cancellationToken
    )
        where TKey : notnull
    {
        var afterSweep = options.Takeover == LeaseTakeover.AfterSweep;
        SqlFenced<LockedRowLease, GrantedRow> granted;

        await using (
            var command = _Command(
                table,
                connection,
                transaction,
                afterSweep ? table.GrantAfterSweepSql : table.GrantSql,
                key
            )
        )
        {
            table.Dialect.AddDuration(command, FencedRowTable<TKey>.DurationParameter, duration);
            _AddHolder(table, command, options.Holder);
            granted = await _ExecuteAsync(command, _ReadGrantedAsync, cancellationToken).ConfigureAwait(false);
        }

        if (granted.Before is not { Lease: var before })
        {
            // A missing row is refused by every fence, and the helper never inserts the consumer's row.
            return null;
        }

        if (granted.IsAccepted)
        {
            return granted.Match<FencedRowGrantResult<TKey>?>(
                accepted: (row, _) =>
                    before.State == RowLeaseState.Active
                        ? FencedRowGrantResult<TKey>.Takeover(
                            new FencedRow<TKey>(table.Name, key, row.Generation),
                            row.ExpiresAt,
                            before.Generation,
                            row.TakeoverCount
                        )
                        : FencedRowGrantResult<TKey>.Granted(
                            new FencedRow<TKey>(table.Name, key, row.Generation),
                            row.ExpiresAt,
                            row.TakeoverCount
                        ),
                rejected: static _ => null
            );
        }

        // Both fences refuse only an active attempt, so the refused row is active: live, or (after-sweep) expired.
        var expiresAt = before.ExpiresAt ?? DateTimeOffset.MinValue;
        var holderMatches =
            options.Holder is not null && string.Equals(before.Holder, options.Holder, StringComparison.Ordinal);

        if (!holderMatches && !afterSweep)
        {
            return FencedRowGrantResult<TKey>.Held(before.Generation, expiresAt, before.TakeoverCount);
        }

        // The row is still locked by this transaction, so the clock read now judges the same row the fence refused.
        var live = await _IsLiveAsync(table, connection, transaction, key, cancellationToken).ConfigureAwait(false);

        if (!live)
        {
            return afterSweep
                ? FencedRowGrantResult<TKey>.Expired(before.Generation, expiresAt, before.TakeoverCount)
                : FencedRowGrantResult<TKey>.Held(before.Generation, expiresAt, before.TakeoverCount);
        }

        // The live attempt is the caller's own: hand it back unchanged.
        return holderMatches
            ? FencedRowGrantResult<TKey>.Granted(
                new FencedRow<TKey>(table.Name, key, before.Generation),
                expiresAt,
                before.TakeoverCount
            )
            : FencedRowGrantResult<TKey>.Held(before.Generation, expiresAt, before.TakeoverCount);
    }

    #endregion

    #region Renew, settle, release, write

    /// <summary>Extends a live generation to <paramref name="duration" /> from the database clock.</summary>
    /// <returns>The renewal's outcome; a refusal says why the generation is no longer live.</returns>
    public static async ValueTask<LeaseRenewalResult> RenewAsync<TKey>(
        FencedRowTable<TKey> table,
        DbConnection connection,
        DbTransaction? transaction,
        FencedRow<TKey> row,
        TimeSpan duration,
        CancellationToken cancellationToken = default
    )
        where TKey : notnull
    {
        Argument.IsNotNull(table);
        _RequireTable(table, row);
        Argument.IsPositive(duration);

        return await _RunAsync(
                table,
                connection,
                transaction,
                async (tx, ct) =>
                {
                    await using var command = _Command(table, connection, tx, table.RenewSql, row.Key);
                    _AddGeneration(table, command, row.Generation);
                    table.Dialect.AddDuration(command, FencedRowTable<TKey>.DurationParameter, duration);
                    var renewed = await _ExecuteAsync(command, _ReadExpiryAsync, ct).ConfigureAwait(false);

                    return renewed.Match(
                        accepted: static (expiresAt, _) =>
                            new LeaseRenewalResult(LeaseRenewalStatus.Renewed, expiresAt),
                        rejected: before =>
                            _Rejection(before?.Lease, row.Generation) switch
                            {
                                // An expired attempt reports its unextended expiry, as the lease table's renewal does.
                                LeaseFenceStatus.Expired => new LeaseRenewalResult(
                                    LeaseRenewalStatus.Expired,
                                    before!.Lease.ExpiresAt
                                ),
                                LeaseFenceStatus.Settled => new(LeaseRenewalStatus.Settled, ExpiresAt: null),
                                LeaseFenceStatus.Released => new(LeaseRenewalStatus.Released, ExpiresAt: null),
                                LeaseFenceStatus.Abandoned => new(LeaseRenewalStatus.Abandoned, ExpiresAt: null),
                                LeaseFenceStatus.Revoked => new(LeaseRenewalStatus.Revoked, ExpiresAt: null),
                                _ => new(LeaseRenewalStatus.Stale, ExpiresAt: null),
                            }
                    );
                },
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Ends a live generation as settled, with the consumer's own final write composed into the same statement, so the
    /// write lands only if the generation is still current.
    /// </summary>
    /// <param name="table">The row's table.</param>
    /// <param name="connection">The caller's connection.</param>
    /// <param name="transaction">The caller's transaction, or <see langword="null" /> to run in one of the helper's own.</param>
    /// <param name="row">The token.</param>
    /// <param name="write">
    /// The consumer's assignments and their parameters, or <see langword="null" />. Only <see cref="FencedRowWrite.Set" />
    /// and <see cref="FencedRowWrite.Bind" /> apply: an end transition reports a status, not columns.
    /// </param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>
    /// <see cref="LeaseSettlementStatus.Settled" />, also for a generation already settled, so a retry reports success;
    /// otherwise why the generation could not settle.
    /// </returns>
    public static ValueTask<LeaseSettlementStatus> SettleAsync<TKey>(
        FencedRowTable<TKey> table,
        DbConnection connection,
        DbTransaction? transaction,
        FencedRow<TKey> row,
        FencedRowWrite? write = null,
        CancellationToken cancellationToken = default
    )
        where TKey : notnull
    {
        return _EndOneAsync(
            table,
            connection,
            transaction,
            row,
            write,
            RowLeaseState.Settled,
            LeaseSettlementStatus.Settled,
            cancellationToken
        );
    }

    /// <summary>
    /// Ends a live generation as released, so the row can be granted again at once, with the consumer's own write
    /// composed into the same statement.
    /// </summary>
    /// <param name="table">The row's table.</param>
    /// <param name="connection">The caller's connection.</param>
    /// <param name="transaction">The caller's transaction, or <see langword="null" /> to run in one of the helper's own.</param>
    /// <param name="row">The token.</param>
    /// <param name="write">
    /// The consumer's assignments and their parameters, or <see langword="null" />. Only <see cref="FencedRowWrite.Set" />
    /// and <see cref="FencedRowWrite.Bind" /> apply.
    /// </param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>
    /// <see cref="LeaseSettlementStatus.Released" />, also for a generation already released; otherwise why the
    /// generation could not be released, such as <see cref="LeaseSettlementStatus.Settled" /> after a settlement.
    /// </returns>
    public static ValueTask<LeaseSettlementStatus> ReleaseAsync<TKey>(
        FencedRowTable<TKey> table,
        DbConnection connection,
        DbTransaction? transaction,
        FencedRow<TKey> row,
        FencedRowWrite? write = null,
        CancellationToken cancellationToken = default
    )
        where TKey : notnull
    {
        return _EndOneAsync(
            table,
            connection,
            transaction,
            row,
            write,
            RowLeaseState.Released,
            LeaseSettlementStatus.Released,
            cancellationToken
        );
    }

    /// <summary>Releases several generations, each judged on its own, in one transaction.</summary>
    /// <remarks>
    /// Each token is its own fenced transition, so tokens of different generations never share a fence. When the key
    /// type is comparable the rows are locked in key order, so two batches over the same rows cannot deadlock; otherwise
    /// they are locked in the order given.
    /// </remarks>
    /// <param name="table">The rows' table.</param>
    /// <param name="connection">The caller's connection.</param>
    /// <param name="transaction">The caller's transaction, or <see langword="null" /> to run in one of the helper's own.</param>
    /// <param name="rows">The tokens.</param>
    /// <param name="write">
    /// The consumer's assignments and their parameters, applied to each row its release ends, or
    /// <see langword="null" />. Only <see cref="FencedRowWrite.Set" /> and <see cref="FencedRowWrite.Bind" /> apply, and
    /// <see cref="FencedRowWrite.Bind" /> runs once per row's statement.
    /// </param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>Each token's outcome, in the order <paramref name="rows" /> lists them.</returns>
    public static async ValueTask<IReadOnlyList<LeaseSettlementStatus>> ReleaseAsync<TKey>(
        FencedRowTable<TKey> table,
        DbConnection connection,
        DbTransaction? transaction,
        IReadOnlyList<FencedRow<TKey>> rows,
        FencedRowWrite? write = null,
        CancellationToken cancellationToken = default
    )
        where TKey : notnull
    {
        Argument.IsNotNull(table);
        Argument.IsNotNull(rows);

        var sql = _EndSql(table, write, RowLeaseState.Released);

        foreach (var row in rows)
        {
            _RequireTable(table, row, nameof(rows));
        }

        if (rows.Count == 0)
        {
            return [];
        }

        var order = _KeyOrder(rows.Count, i => rows[i].Key);

        return await _RunAsync(
                table,
                connection,
                transaction,
                async (tx, ct) =>
                {
                    var statuses = new LeaseSettlementStatus[rows.Count];

                    foreach (var i in order)
                    {
                        statuses[i] = await _EndAsync(
                                table,
                                connection,
                                tx,
                                sql,
                                rows[i],
                                write?.Bind,
                                LeaseSettlementStatus.Released,
                                ct
                            )
                            .ConfigureAwait(false);
                    }

                    return (IReadOnlyList<LeaseSettlementStatus>)statuses;
                },
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Runs the consumer's write on the row only while <paramref name="row" />'s generation is current, keeping the
    /// lease active: a progress update, a heartbeat that carries data, or any write the attempt makes before it ends.
    /// </summary>
    /// <param name="table">The row's table.</param>
    /// <param name="connection">The caller's connection.</param>
    /// <param name="transaction">The caller's transaction, or <see langword="null" /> to run in one of the helper's own.</param>
    /// <param name="row">The token.</param>
    /// <param name="write">
    /// The write: <see cref="FencedRowWrite.Set" /> is required; <see cref="FencedRowWrite.Condition" /> narrows the fence;
    /// <see cref="FencedRowWrite.Returning" /> names the columns to read back (the generation when omitted).
    /// </param>
    /// <param name="readReturning">Reads the returned columns, the first at ordinal 1.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>
    /// Whether the write applied, with the lease the locking read found and how it judged the generation. A refused
    /// write whose lease was still current failed the consumer's own condition; <see cref="SqlFenced{TRow,TAccepted}.Before" />
    /// is <see langword="null" /> when no row has the key.
    /// </returns>
    public static async ValueTask<SqlFenced<LockedRowLease, TAccepted>> WriteAsync<TKey, TAccepted>(
        FencedRowTable<TKey> table,
        DbConnection connection,
        DbTransaction? transaction,
        FencedRow<TKey> row,
        FencedRowWrite write,
        Func<DbDataReader, CancellationToken, ValueTask<TAccepted>> readReturning,
        CancellationToken cancellationToken = default
    )
        where TKey : notnull
    {
        Argument.IsNotNull(table);
        _RequireTable(table, row);
        Argument.IsNotNull(write);
        Argument.IsNotNullOrWhiteSpace(write.Set, paramName: nameof(write));
        Argument.IsNotNull(readReturning);

        return await _WriteAsync(
                table,
                connection,
                transaction,
                row,
                write,
                write.Set,
                readReturning,
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Ends a live generation as settled with the consumer's write composed into the same statement, narrowed by its
    /// condition and reading columns back, classified as <see cref="WriteAsync{TKey,TAccepted}" /> classifies a write.
    /// </summary>
    /// <param name="table">The row's table.</param>
    /// <param name="connection">The caller's connection.</param>
    /// <param name="transaction">The caller's transaction, or <see langword="null" /> to run in one of the helper's own.</param>
    /// <param name="row">The token.</param>
    /// <param name="write">
    /// The write: <see cref="FencedRowWrite.Set" /> joins the settlement's own assignments and may be omitted;
    /// <see cref="FencedRowWrite.Condition" /> narrows the fence; <see cref="FencedRowWrite.Returning" /> names the columns
    /// to read back (the generation when omitted).
    /// </param>
    /// <param name="readReturning">Reads the returned columns, the first at ordinal 1.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>
    /// Whether the settlement applied, with the lease the locking read found and how it judged the generation. A refused
    /// settlement whose lease was still current failed the consumer's own condition; a generation already settled is
    /// refused with <see cref="LeaseFenceStatus.Settled" />, so a retry can tell its first attempt landed.
    /// </returns>
    public static ValueTask<SqlFenced<LockedRowLease, TAccepted>> SettleAsync<TKey, TAccepted>(
        FencedRowTable<TKey> table,
        DbConnection connection,
        DbTransaction? transaction,
        FencedRow<TKey> row,
        FencedRowWrite write,
        Func<DbDataReader, CancellationToken, ValueTask<TAccepted>> readReturning,
        CancellationToken cancellationToken = default
    )
        where TKey : notnull
    {
        return _EndWritingAsync(
            table,
            connection,
            transaction,
            row,
            write,
            RowLeaseState.Settled,
            readReturning,
            cancellationToken
        );
    }

    /// <summary>
    /// Ends a live generation as released with the consumer's write composed into the same statement, narrowed by its
    /// condition and reading columns back, classified as <see cref="WriteAsync{TKey,TAccepted}" /> classifies a write.
    /// </summary>
    /// <param name="table">The row's table.</param>
    /// <param name="connection">The caller's connection.</param>
    /// <param name="transaction">The caller's transaction, or <see langword="null" /> to run in one of the helper's own.</param>
    /// <param name="row">The token.</param>
    /// <param name="write">
    /// The write: <see cref="FencedRowWrite.Set" /> joins the release's own assignments and may be omitted;
    /// <see cref="FencedRowWrite.Condition" /> narrows the fence; <see cref="FencedRowWrite.Returning" /> names the columns
    /// to read back (the generation when omitted).
    /// </param>
    /// <param name="readReturning">Reads the returned columns, the first at ordinal 1.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>
    /// Whether the release applied, with the lease the locking read found and how it judged the generation. A refused
    /// release whose lease was still current failed the consumer's own condition.
    /// </returns>
    public static ValueTask<SqlFenced<LockedRowLease, TAccepted>> ReleaseAsync<TKey, TAccepted>(
        FencedRowTable<TKey> table,
        DbConnection connection,
        DbTransaction? transaction,
        FencedRow<TKey> row,
        FencedRowWrite write,
        Func<DbDataReader, CancellationToken, ValueTask<TAccepted>> readReturning,
        CancellationToken cancellationToken = default
    )
        where TKey : notnull
    {
        return _EndWritingAsync(
            table,
            connection,
            transaction,
            row,
            write,
            RowLeaseState.Released,
            readReturning,
            cancellationToken
        );
    }

    private static async ValueTask<SqlFenced<LockedRowLease, TAccepted>> _EndWritingAsync<TKey, TAccepted>(
        FencedRowTable<TKey> table,
        DbConnection connection,
        DbTransaction? transaction,
        FencedRow<TKey> row,
        FencedRowWrite write,
        RowLeaseState state,
        Func<DbDataReader, CancellationToken, ValueTask<TAccepted>> readReturning,
        CancellationToken cancellationToken
    )
        where TKey : notnull
    {
        Argument.IsNotNull(table);
        _RequireTable(table, row);
        Argument.IsNotNull(write);
        Argument.IsNotNull(readReturning);

        if (write.Set is not null)
        {
            Argument.IsNotNullOrWhiteSpace(write.Set, paramName: nameof(write));
        }

        var set = write.Set is null ? table.EndAssignments(state) : table.EndAssignments(state) + ", " + write.Set;

        return await _WriteAsync(table, connection, transaction, row, write, set, readReturning, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Runs one fenced transition whose assignments are <paramref name="set" />, narrowed by the write's condition, and
    /// classifies a refusal: a lease-fence refusal reports its status, and a held lease whose condition failed reports
    /// <see cref="LeaseFenceStatus.Current" />.
    /// </summary>
    private static async Task<SqlFenced<LockedRowLease, TAccepted>> _WriteAsync<TKey, TAccepted>(
        FencedRowTable<TKey> table,
        DbConnection connection,
        DbTransaction? transaction,
        FencedRow<TKey> row,
        FencedRowWrite write,
        string set,
        Func<DbDataReader, CancellationToken, ValueTask<TAccepted>> readReturning,
        CancellationToken cancellationToken
    )
        where TKey : notnull
    {
        if (write.Condition is not null)
        {
            Argument.IsNotNullOrWhiteSpace(write.Condition, paramName: nameof(write));
        }

        var fence = write.Condition is null ? table.Current : $"{table.Current} AND ({write.Condition})";
        var sql = table.RenderTransition(fence, set, _Returning(table, write.Returning));

        return await _RunAsync(
                table,
                connection,
                transaction,
                async (tx, ct) =>
                {
                    SqlFenced<LockedRowLease, TAccepted> written;

                    await using (var command = _Command(table, connection, tx, sql, row.Key))
                    {
                        _AddGeneration(table, command, row.Generation);
                        _BindConsumer(command, write.Bind, nameof(write));
                        written = await _ExecuteAsync(command, readReturning, ct).ConfigureAwait(false);
                    }

                    if (written.Before is not { } before)
                    {
                        return written;
                    }

                    if (written.IsAccepted)
                    {
                        before.Status = LeaseFenceStatus.Current;

                        return written;
                    }

                    var status = _Rejection(before.Lease, row.Generation);

                    // An active row at this generation fails the lease fence only once expired, but with a condition
                    // the consumer's predicate may have refused a live lease; a second clock read under the same row
                    // lock tells them apart. It runs after the statement, so a lease expiring in between reads as
                    // Expired: the safe side, and true by the time the caller sees it.
                    if (
                        status == LeaseFenceStatus.Expired
                        && write.Condition is not null
                        && await _IsLiveAsync(table, connection, tx, row.Key, ct).ConfigureAwait(false)
                    )
                    {
                        status = LeaseFenceStatus.Current;
                    }

                    before.Status = status;

                    return written;
                },
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    private static async ValueTask<LeaseSettlementStatus> _EndOneAsync<TKey>(
        FencedRowTable<TKey> table,
        DbConnection connection,
        DbTransaction? transaction,
        FencedRow<TKey> row,
        FencedRowWrite? write,
        RowLeaseState state,
        LeaseSettlementStatus success,
        CancellationToken cancellationToken
    )
        where TKey : notnull
    {
        Argument.IsNotNull(table);
        _RequireTable(table, row);

        var sql = _EndSql(table, write, state);

        return await _RunAsync(
                table,
                connection,
                transaction,
                (tx, ct) => _EndAsync(table, connection, tx, sql, row, write?.Bind, success, ct),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <summary>The end transition for <paramref name="state" />, with the consumer's assignments when given.</summary>
    private static string _EndSql<TKey>(FencedRowTable<TKey> table, FencedRowWrite? write, RowLeaseState state)
        where TKey : notnull
    {
        if (write is null)
        {
            return state == RowLeaseState.Settled ? table.SettleSql : table.ReleaseSql;
        }

        Argument.IsNotNullOrWhiteSpace(write.Set, paramName: nameof(write));

        if (write.Condition is not null || write.Returning is not null)
        {
            throw new ArgumentException(
                "This settle or release reports a status, not columns, and its fence is the lease's alone; use the "
                    + "overload that reads the returned columns for a conditional end or one that reads columns back.",
                nameof(write)
            );
        }

        return table.RenderTransition(
            table.Current,
            table.EndAssignments(state) + ", " + write.Set,
            [table.Generation]
        );
    }

    private static async Task<LeaseSettlementStatus> _EndAsync<TKey>(
        FencedRowTable<TKey> table,
        DbConnection connection,
        DbTransaction transaction,
        string sql,
        FencedRow<TKey> row,
        Action<DbCommand>? bind,
        LeaseSettlementStatus success,
        CancellationToken cancellationToken
    )
        where TKey : notnull
    {
        await using var command = _Command(table, connection, transaction, sql, row.Key);
        _AddGeneration(table, command, row.Generation);
        _BindConsumer(command, bind, "write");
        var ended = await _ExecuteAsync(command, _ReadGenerationAsync, cancellationToken).ConfigureAwait(false);

        // A settled or released row at this generation reports its state whichever verb asked, so a retried settlement
        // reports success and a release after a settlement reports that the attempt settled. A revoked row is never
        // success: the attempt did not end it.
        return ended.Match(
            accepted: (_, _) => success,
            rejected: before =>
                _Rejection(before?.Lease, row.Generation) switch
                {
                    LeaseFenceStatus.Settled => LeaseSettlementStatus.Settled,
                    LeaseFenceStatus.Released => LeaseSettlementStatus.Released,
                    LeaseFenceStatus.Abandoned => LeaseSettlementStatus.Abandoned,
                    LeaseFenceStatus.Revoked => LeaseSettlementStatus.Revoked,
                    LeaseFenceStatus.Expired => LeaseSettlementStatus.Expired,
                    _ => LeaseSettlementStatus.Stale,
                }
        );
    }

    #endregion

    #region Revoke, fence, status, sweep

    /// <summary>
    /// Ends the row's active attempt, live or expired, as revoked, without its generation: an operator's or owner's
    /// cancellation of whoever holds the row.
    /// </summary>
    /// <param name="table">The row's table.</param>
    /// <param name="connection">The caller's connection.</param>
    /// <param name="transaction">The caller's transaction, or <see langword="null" /> for the helper to run its own.</param>
    /// <param name="key">The row's key.</param>
    /// <param name="write">
    /// The consumer's own assignments, applied in the same statement only when an attempt was active, such as the row's
    /// cancelled status; <see langword="null" /> to write the lease alone. Only <see cref="FencedRowWrite.Set" /> and
    /// <see cref="FencedRowWrite.Bind" /> apply.
    /// </param>
    /// <param name="cancellationToken">Token used to cancel the statement.</param>
    /// <returns>The token of the attempt revoked, or <see langword="null" /> when no attempt was active or no row has the key.</returns>
    public static async ValueTask<FencedRow<TKey>?> RevokeAsync<TKey>(
        FencedRowTable<TKey> table,
        DbConnection connection,
        DbTransaction? transaction,
        TKey key,
        FencedRowWrite? write = null,
        CancellationToken cancellationToken = default
    )
        where TKey : notnull
    {
        Argument.IsNotNull(table);
        Argument.IsNotNull(key);
        var sql = _RevokeSql(table, write);

        return await _RunAsync(
                table,
                connection,
                transaction,
                (tx, ct) => _RevokeOneAsync(table, connection, tx, sql, key, write?.Bind, ct),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <summary>Revokes the active attempts of several rows, each judged on its own, in one transaction.</summary>
    /// <remarks>
    /// When the key type is comparable the rows are locked in key order, so two batches over the same rows cannot
    /// deadlock; otherwise they are locked in the order given. <paramref name="write" /> applies to each row revoked.
    /// </remarks>
    /// <returns>
    /// Each key's revoked token, or <see langword="null" /> where no attempt was active or no row has the key, in the
    /// order <paramref name="keys" /> lists them.
    /// </returns>
    public static async ValueTask<IReadOnlyList<FencedRow<TKey>?>> RevokeAsync<TKey>(
        FencedRowTable<TKey> table,
        DbConnection connection,
        DbTransaction? transaction,
        IReadOnlyList<TKey> keys,
        FencedRowWrite? write = null,
        CancellationToken cancellationToken = default
    )
        where TKey : notnull
    {
        Argument.IsNotNull(table);
        Argument.IsNotNull(keys);

        foreach (var key in keys)
        {
            Argument.IsNotNull(key, paramName: nameof(keys));
        }

        var sql = _RevokeSql(table, write);

        if (keys.Count == 0)
        {
            return [];
        }

        var order = _KeyOrder(keys.Count, i => keys[i]);

        return await _RunAsync(
                table,
                connection,
                transaction,
                async (tx, ct) =>
                {
                    var revoked = new FencedRow<TKey>?[keys.Count];

                    foreach (var i in order)
                    {
                        revoked[i] = await _RevokeOneAsync(table, connection, tx, sql, keys[i], write?.Bind, ct)
                            .ConfigureAwait(false);
                    }

                    return (IReadOnlyList<FencedRow<TKey>?>)revoked;
                },
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    private static string _RevokeSql<TKey>(FencedRowTable<TKey> table, FencedRowWrite? write)
        where TKey : notnull
    {
        if (write is null)
        {
            return table.RevokeSql;
        }

        Argument.IsNotNullOrWhiteSpace(write.Set, paramName: nameof(write));

        if (write.Condition is not null || write.Returning is not null)
        {
            throw new ArgumentException(
                "A revocation reports the attempt it ended, not columns, and its fence is the active state alone.",
                nameof(write)
            );
        }

        return table.RenderRevoke(write.Set);
    }

    private static async Task<FencedRow<TKey>?> _RevokeOneAsync<TKey>(
        FencedRowTable<TKey> table,
        DbConnection connection,
        DbTransaction transaction,
        string sql,
        TKey key,
        Action<DbCommand>? bind,
        CancellationToken cancellationToken
    )
        where TKey : notnull
    {
        await using var command = _Command(table, connection, transaction, sql, key);
        _BindConsumer(command, bind, "write");
        var revoked = await _ExecuteAsync(command, _ReadGenerationAsync, cancellationToken).ConfigureAwait(false);

        // The fence is the active state alone, so an accepted transition ended the generation it locked.
        return revoked.Match<FencedRow<TKey>?>(
            accepted: (_, before) => new FencedRow<TKey>(table.Name, key, before!.Lease.Generation),
            rejected: static _ => null
        );
    }

    /// <summary>
    /// Locks the row inside the caller's transaction and judges the generation by the database clock, so writes the
    /// transaction then makes to other tables commit only while the row stays locked under a current generation.
    /// </summary>
    /// <param name="table">The row's table.</param>
    /// <param name="connection">The caller's connection, open.</param>
    /// <param name="transaction">The caller's transaction; the lock lasts until it ends.</param>
    /// <param name="row">The token.</param>
    /// <param name="cancellationToken">Token used to cancel the read.</param>
    /// <returns><see cref="LeaseFenceStatus.Current" />, or why the generation is no longer current.</returns>
    public static async ValueTask<LeaseFenceStatus> FenceAsync<TKey>(
        FencedRowTable<TKey> table,
        DbConnection connection,
        DbTransaction transaction,
        FencedRow<TKey> row,
        CancellationToken cancellationToken = default
    )
        where TKey : notnull
    {
        Argument.IsNotNull(table);
        Argument.IsNotNull(connection);
        // A fence outside a transaction would release its lock before the caller's next write, guarding nothing.
        Argument.IsNotNull(transaction);
        _RequireTable(table, row);
        _RequireCallerConnection(table, connection, transaction);

        await using var command = _Command(table, connection, transaction, table.FenceSql, row.Key);
        _AddGeneration(table, command, row.Generation);
        // A fence writes nothing, so its statement returns only whether the generation held: no column to read.
        var fenced = await _ExecuteAsync(command, static (_, _) => ValueTask.FromResult(true), cancellationToken)
            .ConfigureAwait(false);

        return fenced.Match(
            accepted: static (_, _) => LeaseFenceStatus.Current,
            rejected: before => _Rejection(before?.Lease, row.Generation)
        );
    }

    /// <summary>
    /// Reads whether the generation is current by the database clock, without a lock or a transaction of its own: a
    /// cheap liveness check that guards nothing after it returns.
    /// </summary>
    /// <param name="table">The row's table.</param>
    /// <param name="connection">The caller's connection; opened for the read and closed again if it was closed.</param>
    /// <param name="transaction">The caller's transaction to read in, or <see langword="null" /> to read outside one.</param>
    /// <param name="row">The token.</param>
    /// <param name="cancellationToken">Token used to cancel the read.</param>
    /// <returns><see cref="LeaseFenceStatus.Current" />, or why the generation is not; a missing row is stale.</returns>
    public static async ValueTask<LeaseFenceStatus> GetStatusAsync<TKey>(
        FencedRowTable<TKey> table,
        DbConnection connection,
        DbTransaction? transaction,
        FencedRow<TKey> row,
        CancellationToken cancellationToken = default
    )
        where TKey : notnull
    {
        Argument.IsNotNull(table);
        Argument.IsNotNull(connection);
        _RequireTable(table, row);
        _RequireCallerConnection(table, connection, transaction);

        // The caller owns the connection, so it gets it back in the state it passed: opened here only for this read.
        var openedHere = connection.State != ConnectionState.Open;

        if (openedHere)
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            await using var command = _Command(table, connection, transaction, table.StatusSql, row.Key);
            await using var reader = await _ReaderAsync(command, cancellationToken).ConfigureAwait(false);

            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return LeaseFenceStatus.Stale;
            }

            var lease = new RowLeaseSnapshot(
                reader.GetInt64(0),
                (RowLeaseState)reader.GetInt16(1),
                ExpiresAt: null,
                Holder: null,
                TakeoverCount: 0
            );
            var live = Convert.ToInt32(reader.GetValue(2), CultureInfo.InvariantCulture) == 1;

            return lease.Generation == row.Generation && live
                ? LeaseFenceStatus.Current
                : _Rejection(lease, row.Generation);
        }
        finally
        {
            if (openedHere)
            {
                await connection.CloseAsync().ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Ends up to <paramref name="batchSize" /> attempts that expired without ending as abandoned, skipping rows another
    /// transaction holds. Each abandoned row counts one more takeover, as a grant over the expired attempt would.
    /// </summary>
    /// <remarks>
    /// Opt in only for a table whose claims ask for <see cref="LeaseTakeover.AfterSweep" />, or whose consumer reads the
    /// abandoned state. Run it in a loop until it returns fewer than <paramref name="batchSize" />, each call a short
    /// transaction.
    /// </remarks>
    /// <param name="table">The rows' table.</param>
    /// <param name="connection">The caller's connection.</param>
    /// <param name="transaction">The caller's transaction, or <see langword="null" /> for the helper to run its own.</param>
    /// <param name="batchSize">The most rows to abandon.</param>
    /// <param name="write">
    /// The consumer's part of the sweep, or <see langword="null" /> for the lease alone:
    /// <see cref="FencedRowWrite.Condition" /> narrows which expired rows it takes, and <see cref="FencedRowWrite.Set" />
    /// lands in the same statement on each row it abandons, such as moving the row's own status back to pending.
    /// <see cref="FencedRowWrite.Returning" /> does not apply.
    /// </param>
    /// <param name="cancellationToken">Token used to cancel the sweep.</param>
    /// <returns>The number of rows abandoned.</returns>
    public static async ValueTask<int> AbandonExpiredAsync<TKey>(
        FencedRowTable<TKey> table,
        DbConnection connection,
        DbTransaction? transaction,
        int batchSize,
        FencedRowWrite? write = null,
        CancellationToken cancellationToken = default
    )
        where TKey : notnull
    {
        Argument.IsNotNull(table);
        Argument.IsPositive(batchSize);
        var sql = table.AbandonSql;

        if (write is not null)
        {
            if (write.Set is null && write.Condition is null)
            {
                throw new ArgumentException("A sweep's write needs assignments, a condition, or both.", nameof(write));
            }

            if (write.Returning is not null)
            {
                throw new ArgumentException("A sweep reports how many rows it abandoned, not columns.", nameof(write));
            }

            sql = table.RenderAbandon(write.Condition, write.Set);
        }

        return await _RunAsync(
                table,
                connection,
                transaction,
                async (tx, ct) =>
                {
                    await using var command = _Command(connection, tx, sql);
                    _BindConsumer(command, write?.Bind, nameof(write));
                    table.Dialect.AddParameter(
                        command,
                        FencedRowTable<TKey>.BatchSizeParameter,
                        SqlColumnType.Int32,
                        batchSize
                    );
                    await using var reader = await _ReaderAsync(command, ct).ConfigureAwait(false);
                    var abandoned = 0;

                    while (await reader.ReadAsync(ct).ConfigureAwait(false))
                    {
                        abandoned++;
                    }

                    return abandoned;
                },
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Revokes up to <paramref name="batchSize" /> active attempts, live or expired, whose recorded holder is one of
    /// <paramref name="holders" />, skipping rows another transaction holds, in one locked statement.
    /// </summary>
    /// <remarks>
    /// Use it when the holders are known to be gone, such as workers whose heartbeat stopped, so their rows can be
    /// granted again without waiting for each lease to expire. Each revoked generation is fenced: the holder's late
    /// write, renewal, or end is refused. Run it in a loop until it returns fewer than <paramref name="batchSize" />,
    /// each call a short transaction. A row whose last attempt ended keeps its holder as a record and is not touched.
    /// </remarks>
    /// <param name="table">The rows' table.</param>
    /// <param name="connection">The caller's connection.</param>
    /// <param name="transaction">The caller's transaction, or <see langword="null" /> for the helper to run its own.</param>
    /// <param name="holders">The holders whose active attempts to revoke; each is bound as its own parameter.</param>
    /// <param name="batchSize">The most rows to revoke.</param>
    /// <param name="write">
    /// The consumer's part of the revocation, or <see langword="null" /> for the lease alone:
    /// <see cref="FencedRowWrite.Condition" /> narrows which rows it takes, and <see cref="FencedRowWrite.Set" /> lands in
    /// the same statement on each row it revokes. <see cref="FencedRowWrite.Returning" /> does not apply.
    /// </param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The number of rows revoked; 0 without a round trip when <paramref name="holders" /> is empty.</returns>
    /// <exception cref="ArgumentException">A holder is blank or longer than its limit, or the write is invalid.</exception>
    public static async ValueTask<int> RevokeHeldByAsync<TKey>(
        FencedRowTable<TKey> table,
        DbConnection connection,
        DbTransaction? transaction,
        IReadOnlyCollection<string> holders,
        int batchSize,
        FencedRowWrite? write = null,
        CancellationToken cancellationToken = default
    )
        where TKey : notnull
    {
        Argument.IsNotNull(table);
        Argument.IsNotNull(holders);
        Argument.IsPositive(batchSize);

        foreach (var holder in holders)
        {
            Argument.IsNotNull(holder, paramName: nameof(holders));
            LeaseRequestResolver.ValidateGrantOptions(new LeaseGrantOptions { Holder = holder }, table.HolderMaxLength);
        }

        if (write is not null)
        {
            if (write.Set is null && write.Condition is null)
            {
                throw new ArgumentException(
                    "A revocation's write needs assignments, a condition, or both.",
                    nameof(write)
                );
            }

            if (write.Returning is not null)
            {
                throw new ArgumentException(
                    "A revocation reports how many rows it revoked, not columns.",
                    nameof(write)
                );
            }
        }

        if (holders.Count == 0)
        {
            return 0;
        }

        var sql = table.RenderRevokeHeldBy(holders.Count, write?.Condition, write?.Set);

        return await _RunAsync(
                table,
                connection,
                transaction,
                async (tx, ct) =>
                {
                    await using var command = _Command(connection, tx, sql);
                    var index = 0;

                    foreach (var holder in holders)
                    {
                        table.Dialect.AddParameter(
                            command,
                            FencedRowTable<TKey>.HolderListParameter(index++),
                            SqlColumnType.Text(table.HolderMaxLength),
                            holder
                        );
                    }

                    table.Dialect.AddParameter(
                        command,
                        FencedRowTable<TKey>.BatchSizeParameter,
                        SqlColumnType.Int32,
                        batchSize
                    );
                    _BindConsumer(command, write?.Bind, nameof(write));
                    await using var reader = await _ReaderAsync(command, ct).ConfigureAwait(false);
                    var revoked = 0;

                    while (await reader.ReadAsync(ct).ConfigureAwait(false))
                    {
                        revoked++;
                    }

                    return revoked;
                },
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    #endregion

    #region Plumbing

    private static async Task<T> _RunAsync<TKey, T>(
        FencedRowTable<TKey> table,
        DbConnection connection,
        DbTransaction? transaction,
        Func<DbTransaction, CancellationToken, Task<T>> work,
        CancellationToken cancellationToken
    )
        where TKey : notnull
    {
        Argument.IsNotNull(connection);
        _RequireCallerConnection(table, connection, transaction);

        if (transaction is not null)
        {
            return await work(transaction, cancellationToken).ConfigureAwait(false);
        }

        var openedHere = connection.State != ConnectionState.Open;

        if (openedHere)
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            await using var owned = await connection
                .BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
                .ConfigureAwait(false);
            var result = await work(owned, cancellationToken).ConfigureAwait(false);

            // Once the work has run, abandoning the commit would leave the outcome unknown; let it finish.
            await owned.CommitAsync(CancellationToken.None).ConfigureAwait(false);

            return result;
        }
        finally
        {
            if (openedHere)
            {
                await connection.CloseAsync().ConfigureAwait(false);
            }
        }
    }

    private static void _RequireCallerConnection<TKey>(
        FencedRowTable<TKey> table,
        DbConnection connection,
        DbTransaction? transaction
    )
        where TKey : notnull
    {
        if (!table.Dialect.ConnectionType.IsInstanceOfType(connection))
        {
            throw new InvalidOperationException(
                $"The table '{table.Name}' is rendered for {table.Dialect.DisplayName}, which runs on a "
                    + $"{table.Dialect.ConnectionType.Name}, but the caller passed a '{connection.GetType().FullName}'."
            );
        }

        if (
            transaction is not null
            && (
                !table.Dialect.TransactionType.IsInstanceOfType(transaction)
                || !ReferenceEquals(transaction.Connection, connection)
            )
        )
        {
            throw new InvalidOperationException(
                "The transaction is not an open transaction on the connection passed with it, so the operation would "
                    + "run outside it."
            );
        }
    }

    private static void _RequireTable<TKey>(FencedRowTable<TKey> table, FencedRow<TKey> row, string paramName = "row")
        where TKey : notnull
    {
        Argument.IsNotNull(row, paramName: paramName);

        // A generation counts only on the row that issued it; on another table's row it could match by accident.
        if (!string.Equals(row.Table, table.Name, StringComparison.Ordinal))
        {
            throw new ArgumentException($"The token was issued by table '{row.Table}', not '{table.Name}'.", paramName);
        }
    }

    private static DbCommand _Command(DbConnection connection, DbTransaction? transaction, string sql)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = transaction;

        return command;
    }

    private static DbCommand _Command<TKey>(
        FencedRowTable<TKey> table,
        DbConnection connection,
        DbTransaction? transaction,
        string sql,
        TKey key
    )
        where TKey : notnull
    {
        var command = _Command(connection, transaction, sql);
        table.BindKey(command, key);

        return command;
    }

    private static void _AddGeneration<TKey>(FencedRowTable<TKey> table, DbCommand command, long generation)
        where TKey : notnull
    {
        table.Dialect.AddParameter(command, FencedRowTable<TKey>.GenerationParameter, SqlColumnType.Int64, generation);
    }

    private static void _AddHolder<TKey>(FencedRowTable<TKey> table, DbCommand command, string? holder)
        where TKey : notnull
    {
        table.Dialect.AddParameter(
            command,
            FencedRowTable<TKey>.HolderParameter,
            SqlColumnType.Text(table.HolderMaxLength),
            holder
        );
    }

    /// <summary>
    /// Runs the consumer's binder after the helper's own parameters, and refuses any parameter it adds under the
    /// reserved prefix or named as a dialect's own variable: a consumer <c>@LeaseGeneration</c> would shadow the fence's
    /// own, and on SQL Server a consumer <c>@now</c> collides with the batch's <c>DECLARE @now</c>.
    /// </summary>
    private static int[] _KeyOrder<TKey>(int count, Func<int, TKey> key)
        where TKey : notnull
    {
        var order = Enumerable.Range(0, count).ToArray();

        if (
            typeof(IComparable<TKey>).IsAssignableFrom(typeof(TKey))
            || typeof(IComparable).IsAssignableFrom(typeof(TKey))
        )
        {
            // A stable sort, so two entries of one key end in the order given.
            order = [.. order.OrderBy(key, Comparer<TKey>.Default)];
        }

        return order;
    }

    private static void _BindConsumer(DbCommand command, Action<DbCommand>? bind, string paramName)
    {
        if (bind is null)
        {
            return;
        }

        var ours = command.Parameters.Count;
        bind(command);

        for (var i = ours; i < command.Parameters.Count; i++)
        {
            var name = command.Parameters[i].ParameterName.TrimStart('@', ':', '$');

            if (
                name.StartsWith(FencedRowTable<int>.ReservedPrefix, StringComparison.OrdinalIgnoreCase)
                || FencedRowTable<int>.DialectVariables.Contains(name)
            )
            {
                throw new ArgumentException(
                    $"The parameter '{command.Parameters[i].ParameterName}' is reserved: names starting with "
                        + $"'{FencedRowTable<int>.ReservedPrefix}', and 'now' and 'applied', belong to the helper's "
                        + "statement. Name the consumer's parameters differently.",
                    paramName
                );
            }
        }
    }

    private static string[] _Returning<TKey>(FencedRowTable<TKey> table, IReadOnlyList<string>? returning)
        where TKey : notnull
    {
        if (returning is null || returning.Count == 0)
        {
            return [table.Generation];
        }

        var quoted = new string[returning.Count];

        for (var i = 0; i < returning.Count; i++)
        {
            Argument.IsNotNullOrWhiteSpace(returning[i], paramName: "write");
            quoted[i] = table.Dialect.Quote(returning[i]);
        }

        return quoted;
    }

    private static async Task<bool> _IsLiveAsync<TKey>(
        FencedRowTable<TKey> table,
        DbConnection connection,
        DbTransaction transaction,
        TKey key,
        CancellationToken cancellationToken
    )
        where TKey : notnull
    {
        await using var command = _Command(table, connection, transaction, table.LiveSql, key);
        await using var reader = await _ReaderAsync(command, cancellationToken).ConfigureAwait(false);

        // The refused row is locked by this transaction, so it is still there to read.
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            && Convert.ToInt32(reader.GetValue(0), CultureInfo.InvariantCulture) == 1;
    }

    private static async Task<SqlFenced<LockedRowLease, TAccepted>> _ExecuteAsync<TAccepted>(
        DbCommand command,
        Func<DbDataReader, CancellationToken, ValueTask<TAccepted>> readAccepted,
        CancellationToken cancellationToken
    )
    {
        try
        {
            return await SqlFencedCommand
                .ExecuteAsync(command, lockedRead: true, _ReadLeaseAsync, readAccepted, cancellationToken)
                .ConfigureAwait(false);
        }
        // SqlClient reports a command it cancelled mid-flight as a provider error; surface the cancellation asked for.
        catch (DbException ex) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(ex.Message, ex, cancellationToken);
        }
    }

    private static async Task<DbDataReader> _ReaderAsync(DbCommand command, CancellationToken cancellationToken)
    {
        try
        {
            return await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbException ex) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(ex.Message, ex, cancellationToken);
        }
    }

    private static async ValueTask<LockedRowLease> _ReadLeaseAsync(
        DbDataReader reader,
        CancellationToken cancellationToken
    )
    {
        // The locked read's columns: generation, state, expiry, takeover count, holder.
        var expiresAt = await reader.IsDBNullAsync(2, cancellationToken).ConfigureAwait(false)
            ? (DateTimeOffset?)null
            : await reader.GetFieldValueAsync<DateTimeOffset>(2, cancellationToken).ConfigureAwait(false);
        var holder = await reader.IsDBNullAsync(4, cancellationToken).ConfigureAwait(false)
            ? null
            : reader.GetString(4);

        return new LockedRowLease(
            new RowLeaseSnapshot(
                reader.GetInt64(0),
                (RowLeaseState)reader.GetInt16(1),
                expiresAt,
                holder,
                reader.GetInt32(3)
            )
        );
    }

    private static async ValueTask<GrantedRow> _ReadGrantedAsync(
        DbDataReader reader,
        CancellationToken cancellationToken
    )
    {
        return new GrantedRow(
            reader.GetInt64(1),
            await reader.GetFieldValueAsync<DateTimeOffset>(2, cancellationToken).ConfigureAwait(false),
            reader.GetInt32(3)
        );
    }

    private static ValueTask<DateTimeOffset> _ReadExpiryAsync(DbDataReader reader, CancellationToken cancellationToken)
    {
        return new(reader.GetFieldValueAsync<DateTimeOffset>(1, cancellationToken));
    }

    private static ValueTask<long> _ReadGenerationAsync(DbDataReader reader, CancellationToken cancellationToken)
    {
        return ValueTask.FromResult(reader.GetInt64(1));
    }

    /// <summary>
    /// Classifies a refused transition from the lease its locking read found. The fence is this generation, active,
    /// and unexpired, so a refused row at this generation that is still active is expired.
    /// </summary>
    private static LeaseFenceStatus _Rejection(RowLeaseSnapshot? lease, long generation)
    {
        if (lease is not { } before || before.Generation != generation)
        {
            return LeaseFenceStatus.Stale;
        }

        return before.State switch
        {
            RowLeaseState.Active => LeaseFenceStatus.Expired,
            RowLeaseState.Settled => LeaseFenceStatus.Settled,
            RowLeaseState.Released => LeaseFenceStatus.Released,
            RowLeaseState.Abandoned => LeaseFenceStatus.Abandoned,
            RowLeaseState.Revoked => LeaseFenceStatus.Revoked,
            // A never-granted row carries no attempt, so any generation a caller holds for it is not its own.
            RowLeaseState.Idle => LeaseFenceStatus.Stale,
            _ => throw new InvalidOperationException(
                $"A fenced row carries an unknown lease state {((short)before.State).ToString(CultureInfo.InvariantCulture)}; "
                    + "the row was changed outside the helper."
            ),
        };
    }

    #endregion

    private sealed record GrantedRow(long Generation, DateTimeOffset ExpiresAt, int TakeoverCount);
}
#pragma warning restore CA2100
