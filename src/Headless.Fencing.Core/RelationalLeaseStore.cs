// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Checks;
using Headless.Sql;
using Headless.UnitOfWork;

namespace Headless.Fencing;

/// <summary>
/// The relational lease store, written once against <see cref="ISqlDialect" />. Every verb that can wait on a lease
/// row first takes the row's update-intent lock and only then reads the database clock, once, in the statement that
/// decides, so a decision is never made on a clock read from before a lock wait.
/// </summary>
/// <remarks>
/// <para>
/// Renew, settle, release, and fence are one fenced transition each: a single conditional <c>UPDATE</c> whose
/// <c>WHERE</c> is the fence (this generation, active, unexpired by the database clock), so the check and the write
/// cannot disagree. A refusal is classified from the row the locking read found, never re-read.
/// </para>
/// <para>
/// A grant is the same transition with the opposite fence (no live holder) and a new generation drawn in its
/// assignment, so a generation is drawn only for a grant that applies and only after the row's lock is held. An
/// absent row is inserted by a statement that cannot raise a duplicate-key error inside a caller's transaction.
/// </para>
/// <para>
/// Autonomous verbs open their own connection at READ COMMITTED, commit before returning, and retry a transient fault
/// raised before the commit (<see cref="SqlAutonomousTransaction" />). Enlisted verbs run on the unit's connection and transaction, never commit, and never retry:
/// the failure has already rolled back the caller's transaction, so only the unit's owner can run it again.
/// </para>
/// </remarks>
#pragma warning disable CA2100 // SQL text is rendered once from validated identifiers and dialect statements; values are parameters.
internal sealed class RelationalLeaseStore : ILeaseStore, ILeaseEnlistedGrantGuard
{
    // Each purge batch is its own short transaction so a large purge never holds many row locks at once.
    private const int _PurgeBatchSize = 1000;

    // A lost insert race means another transaction committed the row between the locking read and the insert, so the
    // next locking read finds it. Only a row purged again in that window could send the loop round once more.
    private const int _MaxGrantRounds = 5;

    // timestamptz and datetimeoffset both reach back past year 1, so a purge cutoff further back than this would
    // overflow. No lease can have ended that long ago, so clamping the age deletes exactly the same rows.
    private static readonly TimeSpan _MaxPurgeAge = TimeSpan.FromDays(700_000);

    private readonly RelationalFencingStorage _storage;
    private readonly ISqlDialect _dialect;
    private readonly FencingTable _t;
    private readonly IUnitOfWorkFactory _unitOfWorkFactory;
    private readonly TimeProvider _timeProvider;
    private readonly string _grantSql;
    private readonly string _insertSql;
    private readonly string _renewSql;
    private readonly string _renewWithProgressSql;
    private readonly string _settleSql;
    private readonly string _releaseSql;
    private readonly string _fenceSql;
    private readonly string _claimFirstSql;
    private readonly string _claimAfterSql;
    private readonly string _purgeSql;

    public RelationalLeaseStore(
        RelationalFencingStorage storage,
        IUnitOfWorkFactory unitOfWorkFactory,
        TimeProvider timeProvider
    )
    {
        _storage = storage;
        _dialect = storage.Dialect;
        _t = storage.Table;
        _unitOfWorkFactory = unitOfWorkFactory;
        _timeProvider = timeProvider;

        const string now = SqlDialectTokens.Now;
        var t = _t;
        var active = FencingTable.StateLiteral(FencingTable.Active);
        var deadline = _dialect.ShiftByDuration(now, "Duration");
        var live = $"{t.State} = {active} AND {t.ExpiresAt} > {now}";
        var current = $"{t.Generation} = @Generation AND {live}";

        // Reads the row's lock and nothing else a decision needs besides its classification; the progress is not
        // read, so a renewal never ships the stored payload back.
        var locked = _dialect.Render(
            new SqlLockedRead(t.Table, t.Key, [t.Generation, t.State, t.ExpiresAt, t.TakeoverCount])
        );

        // Only a takeover of an expired active attempt counts one more, because a sweep already counted an abandoned
        // one and a settlement or release reset the count. Progress is left as it is, so the new attempt can resume.
        _grantSql =
            locked
            + _dialect.Render(
                new SqlFencedTransition(
                    t.Table,
                    t.Key,
                    Fence: $"NOT ({live})",
                    Set: $"""
                    {t.Generation} = {_dialect.NextSequenceValue(t.Sequence)},
                        {t.State} = {active},
                        {t.GrantedAt} = {now},
                        {t.ExpiresAt} = {deadline},
                        {t.EndedAt} = NULL,
                        {t.TakeoverCount} = CASE WHEN {t.State} = {active} THEN {t.TakeoverCount} + 1 ELSE {t.TakeoverCount} END
                    """,
                    Returning: [t.Generation, t.ExpiresAt, t.TakeoverCount, t.Progress, t.ProgressContract]
                )
            );
        _insertSql = _dialect.Render(
            new SqlInsertIfAbsent(
                t.Table,
                t.Key,
                [t.Generation, t.State, t.GrantedAt, t.ExpiresAt, t.EndedAt],
                [_dialect.NextSequenceValue(t.Sequence), active, now, deadline, "NULL"],
                [t.Generation, t.ExpiresAt]
            )
        );
        _renewSql = locked + _Transition(current, $"{t.ExpiresAt} = {deadline}");
        // Progress rides in the renewal's own fenced write, so it lands only when the renewal does.
        _renewWithProgressSql =
            locked
            + _Transition(
                current,
                $"{t.ExpiresAt} = {deadline}, {t.Progress} = @Progress, {t.ProgressContract} = @ProgressContract"
            );
        _settleSql = locked + _Transition(current, _EndSet(FencingTable.Settled));
        _releaseSql = locked + _Transition(current, _EndSet(FencingTable.Released));
        _fenceSql = locked + _Transition(current, set: null);

        _claimFirstSql = _dialect.Render(_Claim(withCursor: false));
        _claimAfterSql = _dialect.Render(_Claim(withCursor: true));
        _purgeSql = _dialect.Render(
            new SqlDeleteBatch(
                t.Table,
                t.KeyColumns,
                $"{t.Kind} = @Kind AND {t.State} <> {active} AND {t.EndedAt} <= {_dialect.ShiftByDuration(now, "OlderThan", subtract: true)}",
                "BatchSize"
            )
        );
    }

    #region Owned units and enlistment

    public async ValueTask<IUnitOfWork> BeginOwnedUnitAsync(CancellationToken cancellationToken = default)
    {
#pragma warning disable CA2000 // The owned unit opens this connection and closes it when the unit ends; disposing it here would end the unit's transaction.
        var connection = _storage.CreateConnection();
#pragma warning restore CA2000

        try
        {
            return await _storage
                .BeginOwnedUnit(_unitOfWorkFactory, connection, cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);

            throw;
        }
    }

    public void ValidateEnlistment(IUnitOfWork unitOfWork)
    {
        var (connection, _) = _RequireLive(unitOfWork);

        using var configured = _storage.CreateConnection();
        RelationalEnlistment.RequireSameDatabase(
            configured,
            connection,
            _storage.PackageName,
            UnitOfWorkLeasesFeature.Operation
        );
    }

    public void ValidateEnlistedGrant(IUnitOfWork unitOfWork)
    {
        // No unit is exempt, not even one this store began: the expired-lease sweep hands its own unit to caller code
        // that may grant and then roll back, which would let the store draw the same generation again.
        if (_storage.EnlistedGrantRefusal is { } reason)
        {
            throw new NotSupportedException(reason);
        }
    }

    #endregion

    #region Grant

    public ValueTask<LeaseGrantResult> GrantAsync(
        LeaseKey key,
        TimeSpan duration,
        CancellationToken cancellationToken = default
    )
    {
        return _RunAutonomousAsync(
            (connection, transaction, ct) => _GrantAsync(connection, transaction, key, duration, ct),
            cancellationToken
        );
    }

    public async ValueTask<LeaseGrantResult> GrantEnlistedAsync(
        IUnitOfWork unitOfWork,
        LeaseKey key,
        TimeSpan duration,
        CancellationToken cancellationToken = default
    )
    {
        // Re-checked here rather than trusted from validation: the caller may have ended the transaction or closed the
        // connection in between, and a statement on either would fail with a less useful message or run outside it.
        var (connection, transaction) = _RequireLive(Argument.IsNotNull(unitOfWork));

        return await _GrantAsync(connection, transaction, key, duration, cancellationToken).ConfigureAwait(false);
    }

    private async Task<LeaseGrantResult> _GrantAsync(
        DbConnection connection,
        DbTransaction transaction,
        LeaseKey key,
        TimeSpan duration,
        CancellationToken cancellationToken
    )
    {
        for (var round = 1; round <= _MaxGrantRounds; round++)
        {
            SqlFenced<LeaseRow, GrantedRow> granted;

            await using (var command = _Command(_grantSql, connection, transaction, key))
            {
                _dialect.AddDuration(command, "Duration", duration);
                granted = await _ExecuteAsync(command, lockedRead: true, _ReadGrantedAsync, cancellationToken)
                    .ConfigureAwait(false);
            }

            var decided = granted.Match(
                accepted: (row, before) =>
                    before!.State == FencingTable.Active
                        ? LeaseGrantResult.Takeover(
                            key.ToLease(row.Generation),
                            row.ExpiresAt,
                            before.Generation,
                            row.TakeoverCount,
                            row.Progress
                        )
                        : LeaseGrantResult.Granted(
                            key.ToLease(row.Generation),
                            row.ExpiresAt,
                            row.TakeoverCount,
                            row.Progress
                        ),
                // The fence refuses only a live holder; with no row at all there is nothing to refuse, so insert.
                rejected: before =>
                    before is null
                        ? null
                        : LeaseGrantResult.Held(before.Generation, before.ExpiresAt, before.TakeoverCount)
            );

            if (decided is not null)
            {
                return decided;
            }

            await using var insert = _Command(_insertSql, connection, transaction, key);
            _dialect.AddDuration(insert, "Duration", duration);
            var inserted = await _ExecuteAsync(insert, lockedRead: false, _ReadInsertedAsync, cancellationToken)
                .ConfigureAwait(false);

            // Not inserted: another transaction committed the row after the locking read; the next round finds it.
            var result = inserted.Match<LeaseGrantResult?>(
                accepted: (row, _) => LeaseGrantResult.Granted(key.ToLease(row.Generation), row.ExpiresAt),
                rejected: static _ => null
            );

            if (result is not null)
            {
                return result;
            }
        }

        throw new InvalidOperationException(
            $"The lease '{key.Kind}/{key.Resource}' changed between every locking read and insert for "
                + $"{_MaxGrantRounds} rounds; the grant gave up instead of looping."
        );
    }

    #endregion

    #region Renew, settle, release, fence

    public ValueTask<LeaseRenewalResult> RenewAsync(
        LeaseKey key,
        long generation,
        TimeSpan duration,
        LeaseProgress? progress,
        CancellationToken cancellationToken = default
    )
    {
        return _RunAutonomousAsync(
            (connection, transaction, ct) =>
                _RenewAsync(connection, transaction, key, generation, duration, progress, ct),
            cancellationToken
        );
    }

    public async ValueTask<LeaseRenewalResult> RenewEnlistedAsync(
        IUnitOfWork unitOfWork,
        LeaseKey key,
        long generation,
        TimeSpan duration,
        LeaseProgress? progress,
        CancellationToken cancellationToken = default
    )
    {
        var (connection, transaction) = _RequireLive(Argument.IsNotNull(unitOfWork));

        return await _RenewAsync(connection, transaction, key, generation, duration, progress, cancellationToken)
            .ConfigureAwait(false);
    }

    public ValueTask<LeaseSettlementStatus> SettleAsync(
        LeaseKey key,
        long generation,
        CancellationToken cancellationToken = default
    )
    {
        return _RunAutonomousAsync(
            (connection, transaction, ct) =>
                _EndAsync(connection, transaction, _settleSql, LeaseSettlementStatus.Settled, key, generation, ct),
            cancellationToken
        );
    }

    public async ValueTask<LeaseSettlementStatus> SettleEnlistedAsync(
        IUnitOfWork unitOfWork,
        LeaseKey key,
        long generation,
        CancellationToken cancellationToken = default
    )
    {
        var (connection, transaction) = _RequireLive(Argument.IsNotNull(unitOfWork));

        return await _EndAsync(
                connection,
                transaction,
                _settleSql,
                LeaseSettlementStatus.Settled,
                key,
                generation,
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    public ValueTask<LeaseSettlementStatus> ReleaseAsync(
        LeaseKey key,
        long generation,
        CancellationToken cancellationToken = default
    )
    {
        return _RunAutonomousAsync(
            (connection, transaction, ct) =>
                _EndAsync(connection, transaction, _releaseSql, LeaseSettlementStatus.Released, key, generation, ct),
            cancellationToken
        );
    }

    public async ValueTask<LeaseSettlementStatus> ReleaseEnlistedAsync(
        IUnitOfWork unitOfWork,
        LeaseKey key,
        long generation,
        CancellationToken cancellationToken = default
    )
    {
        var (connection, transaction) = _RequireLive(Argument.IsNotNull(unitOfWork));

        return await _EndAsync(
                connection,
                transaction,
                _releaseSql,
                LeaseSettlementStatus.Released,
                key,
                generation,
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    public async ValueTask<LeaseFenceStatus> FenceEnlistedAsync(
        IUnitOfWork unitOfWork,
        LeaseKey key,
        long generation,
        CancellationToken cancellationToken = default
    )
    {
        var (connection, transaction) = _RequireLive(Argument.IsNotNull(unitOfWork));

        await using var command = _Command(_fenceSql, connection, transaction, key);
        _dialect.AddParameter(command, "Generation", SqlColumnType.Int64, generation);
        var fenced = await _ExecuteAsync(
                command,
                lockedRead: true,
                static (_, _) => ValueTask.FromResult(true),
                cancellationToken
            )
            .ConfigureAwait(false);

        return fenced.Match(
            accepted: static (_, _) => LeaseFenceStatus.Current,
            rejected: before => _Rejection(key, before, generation)
        );
    }

    private async Task<LeaseRenewalResult> _RenewAsync(
        DbConnection connection,
        DbTransaction transaction,
        LeaseKey key,
        long generation,
        TimeSpan duration,
        LeaseProgress? progress,
        CancellationToken cancellationToken
    )
    {
        await using var command = _Command(
            progress is null ? _renewSql : _renewWithProgressSql,
            connection,
            transaction,
            key
        );
        _dialect.AddParameter(command, "Generation", SqlColumnType.Int64, generation);
        _dialect.AddDuration(command, "Duration", duration);

        if (progress is not null)
        {
            _dialect.AddParameter(command, "Progress", SqlColumnType.Binary, progress.Payload);
            _dialect.AddParameter(
                command,
                "ProgressContract",
                SqlColumnType.Text(FencingFieldLimits.ProgressContractMaxLength),
                progress.Contract
            );
        }

        var renewed = await _ExecuteAsync(command, lockedRead: true, _ReadExpiryAsync, cancellationToken)
            .ConfigureAwait(false);

        return renewed.Match(
            accepted: static (expiresAt, _) => new LeaseRenewalResult(LeaseRenewalStatus.Renewed, expiresAt),
            rejected: before =>
                _Rejection(key, before, generation) switch
                {
                    LeaseFenceStatus.Expired => new LeaseRenewalResult(LeaseRenewalStatus.Expired, before!.ExpiresAt),
                    LeaseFenceStatus.Settled => new LeaseRenewalResult(LeaseRenewalStatus.Settled, ExpiresAt: null),
                    LeaseFenceStatus.Released => new LeaseRenewalResult(LeaseRenewalStatus.Released, ExpiresAt: null),
                    LeaseFenceStatus.Abandoned => new LeaseRenewalResult(LeaseRenewalStatus.Abandoned, ExpiresAt: null),
                    _ => new LeaseRenewalResult(LeaseRenewalStatus.Stale, ExpiresAt: null),
                }
        );
    }

    private async Task<LeaseSettlementStatus> _EndAsync(
        DbConnection connection,
        DbTransaction transaction,
        string sql,
        LeaseSettlementStatus success,
        LeaseKey key,
        long generation,
        CancellationToken cancellationToken
    )
    {
        await using var command = _Command(sql, connection, transaction, key);
        _dialect.AddParameter(command, "Generation", SqlColumnType.Int64, generation);
        var ended = await _ExecuteAsync(command, lockedRead: true, _ReadExpiryAsync, cancellationToken)
            .ConfigureAwait(false);

        // A settled or released row at this generation reports its state whichever verb asked, so a retried settlement
        // reports success and a release after a settlement reports that the attempt settled.
        return ended.Match(
            accepted: (_, _) => success,
            rejected: before =>
                _Rejection(key, before, generation) switch
                {
                    LeaseFenceStatus.Settled => LeaseSettlementStatus.Settled,
                    LeaseFenceStatus.Released => LeaseSettlementStatus.Released,
                    LeaseFenceStatus.Abandoned => LeaseSettlementStatus.Abandoned,
                    LeaseFenceStatus.Expired => LeaseSettlementStatus.Expired,
                    _ => LeaseSettlementStatus.Stale,
                }
        );
    }

    #endregion

    #region Sweep and purge

    public async ValueTask<ExpiredLease?> ClaimExpiredEnlistedAsync(
        IUnitOfWork unitOfWork,
        string kind,
        ExpiredLease? after,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(kind);
        var (connection, transaction) = _RequireLive(Argument.IsNotNull(unitOfWork));

        await using var command = _Command(after is null ? _claimFirstSql : _claimAfterSql, connection, transaction);
        _dialect.AddParameter(command, "Kind", SqlColumnType.KeyText(FencingFieldLimits.KindMaxLength), kind);

        if (after is not null)
        {
            _dialect.AddParameter(command, "AfterExpiresAt", SqlColumnType.Timestamp, after.ExpiresAt);
            _dialect.AddParameter(
                command,
                "AfterTenantId",
                SqlColumnType.KeyText(FencingFieldLimits.TenantIdMaxLength),
                after.TenantId ?? string.Empty
            );
            _dialect.AddParameter(
                command,
                "AfterResource",
                SqlColumnType.KeyText(FencingFieldLimits.ResourceMaxLength),
                after.Resource
            );
        }

        await using var reader = await _ReaderAsync(command, cancellationToken).ConfigureAwait(false);

        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var key = new LeaseKey(reader.GetString(0), kind, reader.GetString(1));
        var expiresAt = await reader.GetFieldValueAsync<DateTimeOffset>(3, cancellationToken).ConfigureAwait(false);
        var progress = await _ReadProgressAsync(reader, 5, cancellationToken).ConfigureAwait(false);

        return key.ToExpiredLease(reader.GetInt64(2), expiresAt, reader.GetInt32(4), progress);
    }

    public async ValueTask<int> PurgeAsync(
        string kind,
        TimeSpan olderThan,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(kind);
        Argument.IsPositiveOrZero(olderThan);

        var age = olderThan > _MaxPurgeAge ? _MaxPurgeAge : olderThan;
        var total = 0;

        while (true)
        {
            var deleted = await _RunAutonomousAsync(
                    async (connection, transaction, ct) =>
                    {
                        await using var command = _Command(_purgeSql, connection, transaction);
                        _dialect.AddParameter(
                            command,
                            "Kind",
                            SqlColumnType.KeyText(FencingFieldLimits.KindMaxLength),
                            kind
                        );
                        _dialect.AddParameter(command, "BatchSize", SqlColumnType.Int32, _PurgeBatchSize);
                        _dialect.AddDuration(command, "OlderThan", age);

                        try
                        {
                            return await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                        }
                        catch (DbException ex) when (ct.IsCancellationRequested)
                        {
                            throw new OperationCanceledException(ex.Message, ex, ct);
                        }
                    },
                    cancellationToken
                )
                .ConfigureAwait(false);

            total += deleted;

            if (deleted < _PurgeBatchSize)
            {
                return total;
            }
        }
    }

    #endregion

    #region Helpers

    private string _Transition(string fence, string? set)
    {
        return _dialect.Render(new SqlFencedTransition(_t.Table, _t.Key, fence, set, [_t.ExpiresAt]));
    }

    private string _EndSet(short state)
    {
        // An ended attempt's progress and takeover count belong to work that is finished, so the next grant starts
        // clean.
        return $"""
            {_t.State} = {FencingTable.StateLiteral(state)},
                {_t.EndedAt} = {SqlDialectTokens.Now},
                {_t.TakeoverCount} = 0,
                {_t.Progress} = NULL,
                {_t.ProgressContract} = NULL
            """;
    }

    private SqlClaimNext _Claim(bool withCursor)
    {
        // Skipping locked rows passes over a lease another sweeper is claiming or a fence is holding, so concurrent
        // sweepers never hand one lease to two handlers and never wait on each other. The keyset cursor walks the
        // active expiry index; a lease the caller already visited is behind it, so a lease whose handler threw is left
        // for a later call. Abandoning counts one takeover and keeps the progress, so the next grant resumes it.
        var t = _t;
        var order = new[] { t.ExpiresAt, t.TenantId, t.Resource };
        var cursor = withCursor
            ? " AND " + _dialect.KeysetAfter(order, ["AfterExpiresAt", "AfterTenantId", "AfterResource"])
            : string.Empty;

        return new SqlClaimNext(
            t.Table,
            t.KeyColumns,
            $"{t.Kind} = @Kind AND {t.State} = {FencingTable.StateLiteral(FencingTable.Active)} AND {t.ExpiresAt} <= {SqlDialectTokens.Now}{cursor}",
            order,
            $"""
            {t.State} = {FencingTable.StateLiteral(FencingTable.Abandoned)},
                {t.EndedAt} = {SqlDialectTokens.Now},
                {t.TakeoverCount} = {t.TakeoverCount} + 1
            """,
            [t.TenantId, t.Resource, t.Generation, t.ExpiresAt, t.TakeoverCount, t.Progress, t.ProgressContract]
        );
    }

    private ValueTask<T> _RunAutonomousAsync<T>(
        Func<DbConnection, DbTransaction, CancellationToken, Task<T>> body,
        CancellationToken cancellationToken
    )
    {
        return SqlAutonomousTransaction.RunAsync(_storage.CreateConnection, body, _timeProvider, cancellationToken);
    }

    private DbCommand _Command(string sql, DbConnection connection, DbTransaction transaction, LeaseKey? key = null)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = transaction;
        command.CommandTimeout = _storage.CommandTimeoutSeconds;

        if (key is { } value)
        {
            _dialect.AddParameter(
                command,
                "TenantId",
                SqlColumnType.KeyText(FencingFieldLimits.TenantIdMaxLength),
                value.TenantId
            );
            _dialect.AddParameter(command, "Kind", SqlColumnType.KeyText(FencingFieldLimits.KindMaxLength), value.Kind);
            _dialect.AddParameter(
                command,
                "Resource",
                SqlColumnType.KeyText(FencingFieldLimits.ResourceMaxLength),
                value.Resource
            );
        }

        return command;
    }

    private static async Task<SqlFenced<LeaseRow, TAccepted>> _ExecuteAsync<TAccepted>(
        DbCommand command,
        bool lockedRead,
        Func<DbDataReader, CancellationToken, ValueTask<TAccepted>> readAccepted,
        CancellationToken cancellationToken
    )
    {
        try
        {
            return await SqlFencedCommand
                .ExecuteAsync(command, lockedRead, _ReadRowAsync, readAccepted, cancellationToken)
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

    private static async ValueTask<LeaseRow> _ReadRowAsync(DbDataReader reader, CancellationToken cancellationToken)
    {
        return new LeaseRow(
            reader.GetInt64(0),
            reader.GetInt16(1),
            await reader.GetFieldValueAsync<DateTimeOffset>(2, cancellationToken).ConfigureAwait(false),
            reader.GetInt32(3)
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
            reader.GetInt32(3),
            await _ReadProgressAsync(reader, 4, cancellationToken).ConfigureAwait(false)
        );
    }

    private static async ValueTask<GrantedRow> _ReadInsertedAsync(
        DbDataReader reader,
        CancellationToken cancellationToken
    )
    {
        return new GrantedRow(
            reader.GetInt64(1),
            await reader.GetFieldValueAsync<DateTimeOffset>(2, cancellationToken).ConfigureAwait(false),
            TakeoverCount: 0,
            Progress: null
        );
    }

    private static ValueTask<DateTimeOffset> _ReadExpiryAsync(DbDataReader reader, CancellationToken cancellationToken)
    {
        return new(reader.GetFieldValueAsync<DateTimeOffset>(1, cancellationToken));
    }

    /// <summary>Reads a progress payload and its contract from two adjacent columns; both are null when none is stored.</summary>
    private static async Task<LeaseProgress?> _ReadProgressAsync(
        DbDataReader reader,
        int payloadOrdinal,
        CancellationToken cancellationToken
    )
    {
        if (await reader.IsDBNullAsync(payloadOrdinal, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var payload = await reader.GetFieldValueAsync<byte[]>(payloadOrdinal, cancellationToken).ConfigureAwait(false);

        return new LeaseProgress(payload, reader.GetString(payloadOrdinal + 1));
    }

    /// <summary>
    /// Classifies a refused transition from the row its locking read found. The fence is this generation, active, and
    /// unexpired, so a refused row at this generation that is still active is expired.
    /// </summary>
    private static LeaseFenceStatus _Rejection(LeaseKey key, LeaseRow? before, long generation)
    {
        if (before is null || before.Generation != generation)
        {
            return LeaseFenceStatus.Stale;
        }

        return before.State switch
        {
            FencingTable.Settled => LeaseFenceStatus.Settled,
            FencingTable.Released => LeaseFenceStatus.Released,
            FencingTable.Abandoned => LeaseFenceStatus.Abandoned,
            FencingTable.Active => LeaseFenceStatus.Expired,
            _ => throw new InvalidOperationException(
                $"The lease '{key.Kind}/{key.Resource}' carries an unknown state {before.State.ToString(CultureInfo.InvariantCulture)}; the table was changed outside this provider."
            ),
        };
    }

    private (DbConnection Connection, DbTransaction Transaction) _RequireLive(IUnitOfWork unitOfWork)
    {
        return RelationalEnlistment.RequireLive(
            unitOfWork,
            _dialect.ConnectionType,
            _dialect.TransactionType,
            _storage.PackageName,
            UnitOfWorkLeasesFeature.Operation
        );
    }

    #endregion

    private sealed record LeaseRow(long Generation, short State, DateTimeOffset ExpiresAt, int TakeoverCount);

    private sealed record GrantedRow(
        long Generation,
        DateTimeOffset ExpiresAt,
        int TakeoverCount,
        LeaseProgress? Progress
    );
}
#pragma warning restore CA2100
