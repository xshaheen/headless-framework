// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using System.Runtime.CompilerServices;
using Headless.Checks;
using Headless.Sql;
using Headless.UnitOfWork;

namespace Headless.Idempotency;

/// <summary>
/// The relational idempotency record store, written once against <see cref="ISqlDialect" />. Every verb that can wait
/// on a record row first takes the row's update-intent lock and only then reads the database clock, so a retention or
/// lease decision is never made on a clock read from before a lock wait.
/// </summary>
/// <remarks>
/// <para>
/// A locking read is followed, in the same batch, by a statement of its own that reads the clock, so the clock is read
/// after the lock wait. An absent record is inserted by a statement that cannot raise a duplicate-key error inside a
/// caller's transaction: on SQL Server the locking read already holds the key range, and on PostgreSQL a concurrent
/// first insert of the same key waits for the other transaction and inserts nothing, after which the next locking read
/// finds the committed row.
/// </para>
/// <para>
/// Admit, complete, release, recovery point, and renewal are one fenced transition each: a single conditional
/// <c>UPDATE</c> whose <c>WHERE</c> is the fence, so the check and the write are one decision. Admit draws its
/// generation from the store-wide sequence in that <c>UPDATE</c>'s own assignment, so a generation is drawn only for an
/// admission that applies and only after the row's lock is held.
/// </para>
/// <para>
/// Autonomous verbs (renew, peek, purge) open their own connection at READ COMMITTED, commit before returning, and
/// retry a transient fault raised before the commit (<see cref="SqlAutonomousTransaction" />). Enlisted verbs run on the unit's connection and transaction, never
/// commit, and never retry: the failure has already rolled back the caller's transaction, so only the unit's owner can
/// run it again.
/// </para>
/// </remarks>
#pragma warning disable CA2100 // SQL text is rendered once from validated identifiers and dialect statements; values are parameters.
internal sealed class RelationalIdempotencyRecordStore : IIdempotencyRecordStore, IIdempotencyEnlistedAdmissionGuard
{
    // A lost insert race means another transaction committed the row between the locking read and the insert, so the
    // next locking read finds it. Only a row purged again in that window could send the loop round once more.
    private const int _MaxLockRounds = 5;

    // timestamptz and datetimeoffset both reach back past year 1, so a purge cutoff further back than this would
    // overflow. No record can have been retained that long ago, so clamping the age deletes exactly the same rows.
    private static readonly TimeSpan _MaxPurgeAge = TimeSpan.FromDays(700_000);

    private readonly RelationalIdempotencyStorage _storage;
    private readonly ISqlDialect _dialect;
    private readonly IdempotencyTable _t;
    private readonly IUnitOfWorkFactory _unitOfWorkFactory;
    private readonly TimeProvider _timeProvider;
    private readonly string _lockSql;
    private readonly string _insertSql;
    private readonly string _admitKeepingRecoveryPointSql;
    private readonly string _admitClearingRecoveryPointSql;
    private readonly string _completeSql;
    private readonly string _releaseSql;
    private readonly string _setRecoveryPointSql;
    private readonly string _renewSql;
    private readonly string _peekSql;
    private readonly string _purgeSql;

    // The units this store began for its autonomous calls, tracked only when enlisted admissions are refused.
    private static readonly object _OwnUnit = new();
    private readonly ConditionalWeakTable<IUnitOfWork, object> _ownUnits = [];

    public RelationalIdempotencyRecordStore(
        RelationalIdempotencyStorage storage,
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
        var pending = IdempotencyTable.StatusLiteral(IdempotencyRecordStatus.Pending);
        var completed = IdempotencyTable.StatusLiteral(IdempotencyRecordStatus.Completed);
        var ownedByGeneration = $"{t.Generation} = @Generation AND {t.Status} = {pending}";

        // The locking read waits out any other holder; the clock is a statement of its own after it, because a column
        // the locking statement computed itself could be evaluated before the wait.
        _lockSql =
            _dialect.Render(
                new SqlLockedRead(
                    t.Table,
                    t.KeyColumns,
                    [
                        t.Status,
                        t.FingerprintAlgorithm,
                        t.Fingerprint,
                        t.Generation,
                        t.LeaseExpiresAt,
                        t.Result,
                        t.ResultContract,
                        t.RetentionUntil,
                        t.RecoveryPoint,
                        t.RecoveryState,
                        t.RecoveryContract,
                    ]
                )
            ) + _dialect.Render(new SqlClockedStatement($"SELECT {now};"));

        // Runs only after a locking read found no row. On PostgreSQL its clock is read before any wait on a concurrent
        // inserter, which can only shorten the retention by that wait; an admission extends it again under the lock.
        _insertSql = _dialect.Render(
            new SqlInsertIfAbsent(
                t.Table,
                t.KeyColumns,
                [t.Status, t.FingerprintAlgorithm, t.Fingerprint, t.RetentionUntil],
                [pending, "@FingerprintAlgorithm", "@Fingerprint", _dialect.ShiftByDuration(now, "Retention")],
                [t.RetentionUntil]
            )
        );

        // Also the in-place reset of a record past its retention: every outcome column is overwritten. The fence
        // restates what the caller decided under the same lock, that no live attempt holds the record; the clock only
        // moved forward since, so a lease judged expired then is expired now.
        var admitSet = $"""
            {t.Status} = {pending},
                {t.FingerprintAlgorithm} = @FingerprintAlgorithm,
                {t.Fingerprint} = @Fingerprint,
                {t.Generation} = {_dialect.NextSequenceValue(t.Sequence)},
                {t.LeaseExpiresAt} = {_dialect.ShiftByDuration(now, "LeaseDuration")},
                {t.Result} = NULL,
                {t.ResultContract} = NULL,
                {t.RetentionUntil} = {_ExtendedRetention()}
            """;
        var notHeld = $"{t.Status} <> {pending} OR {t.LeaseExpiresAt} IS NULL OR {t.LeaseExpiresAt} <= {now}";
        _admitKeepingRecoveryPointSql = _Transition(notHeld, admitSet, [t.Generation, t.LeaseExpiresAt]);
        _admitClearingRecoveryPointSql = _Transition(
            notHeld,
            $"""
            {admitSet},
                {t.RecoveryPoint} = NULL,
                {t.RecoveryState} = NULL,
                {t.RecoveryContract} = NULL
            """,
            [t.Generation, t.LeaseExpiresAt]
        );

        // The completing generation is kept, so a second completion by the same attempt finds its own completed record
        // and is refused instead of overwriting the stored result.
        _completeSql = _Transition(
            ownedByGeneration,
            $"""
            {t.Status} = {completed},
                {t.LeaseExpiresAt} = NULL,
                {t.Result} = @Result,
                {t.ResultContract} = @ResultContract,
                {t.RecoveryPoint} = NULL,
                {t.RecoveryState} = NULL,
                {t.RecoveryContract} = NULL,
                {t.RetentionUntil} = {_ExtendedRetention()}
            """,
            [t.Generation]
        );

        // The recovery point is kept: a released attempt may have finished steps the next attempt should not redo.
        _releaseSql = _Transition(
            ownedByGeneration,
            $"""
            {t.Generation} = NULL,
                {t.LeaseExpiresAt} = NULL,
                {t.RetentionUntil} = {_ExtendedRetention()}
            """,
            [t.Status]
        );

        // The lease was judged live under the same lock, so the fence only restates the generation.
        _setRecoveryPointSql = _Transition(
            ownedByGeneration,
            $"""
            {t.RecoveryPoint} = @RecoveryPoint,
                {t.RecoveryState} = @RecoveryState,
                {t.RecoveryContract} = @RecoveryContract
            """,
            [t.Generation]
        );

        // A refusal is classified from the row the locking read found, so the renewal ships nothing else back.
        _renewSql =
            _dialect.Render(new SqlLockedRead(t.Table, t.KeyColumns, [t.Status, t.Generation, t.LeaseExpiresAt]))
            + _Transition(
                $"{ownedByGeneration} AND {t.LeaseExpiresAt} > {now}",
                $"{t.LeaseExpiresAt} = {_dialect.ShiftByDuration(now, "LeaseDuration")}",
                [t.LeaseExpiresAt]
            );

        // No row lock and no lock hint. PostgreSQL's MVCC read never waits. SQL Server reads at plain READ COMMITTED:
        // its shared lock is compatible with the update lock a locked read holds, so a peek never queues behind one,
        // but with READ_COMMITTED_SNAPSHOT off it waits behind a writer's uncommitted exclusive lock, up to the command
        // timeout, and an enlisted unit holds that lock until the unit ends.
        _peekSql = _dialect.Render(
            new SqlClockedStatement(
                $"SELECT {t.Status}, {t.RetentionUntil}, {now} FROM {t.Table} WHERE {t.TenantId} = @TenantId AND {t.Key} = @IdempotencyKey;"
            )
        );

        // Skipping locked rows leaves a record an admission, fence, or completion holds for a later purge, so the purge
        // never waits on application work and never deletes a row under a transaction that is deciding on it. A live
        // lease keeps its record even past retention, because its attempt may still complete.
        _purgeSql = _dialect.Render(
            new SqlDeleteBatch(
                t.Table,
                t.KeyColumnNames,
                $"{t.RetentionUntil} <= {_dialect.ShiftByDuration(now, "OlderThan", subtract: true)} AND ({t.LeaseExpiresAt} IS NULL OR {t.LeaseExpiresAt} <= {now})",
                "Limit"
            )
        );
    }

    #region Owned units and enlistment

    public async ValueTask<IUnitOfWork> BeginOwnedUnitAsync(CancellationToken cancellationToken = default)
    {
        var connection = _storage.CreateConnection();

        try
        {
            var unit = await _storage
                .BeginOwnedUnit(_unitOfWorkFactory, connection, cancellationToken)
                .ConfigureAwait(false);

            if (_storage.EnlistedAdmissionRefusal is not null)
            {
                _ownUnits.Add(unit, _OwnUnit);
            }

            return unit;
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
            UnitOfWorkIdempotencyFeature.Operation
        );
    }

    public void ValidateEnlistedAdmission(IUnitOfWork unitOfWork)
    {
        // An autonomous admission runs in a unit this store began and commits it before the caller sees the
        // generation, so a rollback can never leave a caller holding a generation the store will draw again.
        if (_storage.EnlistedAdmissionRefusal is { } reason && !_ownUnits.TryGetValue(unitOfWork, out _))
        {
            throw new NotSupportedException(reason);
        }
    }

    #endregion

    #region Lock

    public async ValueTask<IdempotencyRecordState> LockOrInsertAsync(
        IUnitOfWork unitOfWork,
        IdempotencyRecordKey key,
        IdempotencyFingerprint fingerprint,
        TimeSpan retention,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(fingerprint);

        // Re-checked here rather than trusted from validation: the caller may have ended the transaction or closed the
        // connection in between, and a statement on either would fail with a less useful message or run outside it.
        var (connection, transaction) = _RequireLive(Argument.IsNotNull(unitOfWork));

        for (var round = 1; round <= _MaxLockRounds; round++)
        {
            if (await _LockAsync(connection, transaction, key, cancellationToken).ConfigureAwait(false) is { } existing)
            {
                return existing;
            }

            SqlFenced<RenewalRow, DateTimeOffset> inserted;

            await using (var command = _Command(_insertSql, connection, transaction, key))
            {
                _AddFingerprint(command, fingerprint);
                _dialect.AddDuration(command, "Retention", retention);
                inserted = await _ExecuteAsync(command, lockedRead: false, _ReadTimestampAsync, cancellationToken)
                    .ConfigureAwait(false);
            }

            // Not inserted: another transaction committed the row after the locking read; the next round locks it.
            var state = inserted.Match<IdempotencyRecordState?>(
                accepted: (retentionUntil, _) =>
                    new IdempotencyRecordState(
                        Inserted: true,
                        IdempotencyRecordStatus.Pending,
                        fingerprint,
                        Generation: null,
                        LeaseExpiresAt: null,
                        Result: null,
                        retentionUntil,
                        // The retention is positive and runs from the insert's own clock, so it has not passed yet.
                        IsRetentionElapsed: false,
                        IsLeaseLive: false
                    ),
                rejected: static _ => null
            );

            if (state is not null)
            {
                return state;
            }
        }

        throw new InvalidOperationException(
            $"The idempotency record '{key.Key}' changed between every locking read and insert for "
                + $"{_MaxLockRounds} rounds; the admission gave up instead of looping."
        );
    }

    public async ValueTask<IdempotencyRecordState?> LockAsync(
        IUnitOfWork unitOfWork,
        IdempotencyRecordKey key,
        CancellationToken cancellationToken = default
    )
    {
        var (connection, transaction) = _RequireLive(Argument.IsNotNull(unitOfWork));

        return await _LockAsync(connection, transaction, key, cancellationToken).ConfigureAwait(false);
    }

    private async Task<IdempotencyRecordState?> _LockAsync(
        DbConnection connection,
        DbTransaction transaction,
        IdempotencyRecordKey key,
        CancellationToken cancellationToken
    )
    {
        await using var command = _Command(_lockSql, connection, transaction, key);
        await using var reader = await _ReaderAsync(command, cancellationToken).ConfigureAwait(false);

        var row = await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? await _ReadRecordAsync(reader, cancellationToken).ConfigureAwait(false)
            : null;

        // The clock's own result set follows the locking read's, whether or not it found a row.
        await reader.NextResultAsync(cancellationToken).ConfigureAwait(false);

        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException("A clocked read returned no clock row; the dialect rendered it wrong.");
        }

        if (row is null)
        {
            return null;
        }

        var now = await reader.GetFieldValueAsync<DateTimeOffset>(0, cancellationToken).ConfigureAwait(false);

        return new IdempotencyRecordState(
            Inserted: false,
            row.Status,
            row.Fingerprint,
            row.Generation,
            row.LeaseExpiresAt,
            row.Result,
            row.RetentionUntil,
            IsRetentionElapsed: row.RetentionUntil <= now,
            IsLeaseLive: row.LeaseExpiresAt > now,
            row.RecoveryPoint
        );
    }

    #endregion

    #region Admit, complete, release, recovery point

    public async ValueTask<IdempotencyRecordGrant> AdmitAsync(
        IUnitOfWork unitOfWork,
        IdempotencyRecordKey key,
        IdempotencyFingerprint fingerprint,
        TimeSpan leaseDuration,
        TimeSpan retention,
        bool keepRecoveryPoint,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(fingerprint);
        var (connection, transaction) = _RequireLive(Argument.IsNotNull(unitOfWork));

        await using var command = _Command(
            keepRecoveryPoint ? _admitKeepingRecoveryPointSql : _admitClearingRecoveryPointSql,
            connection,
            transaction,
            key
        );
        _AddFingerprint(command, fingerprint);
        _dialect.AddDuration(command, "LeaseDuration", leaseDuration);
        _dialect.AddDuration(command, "Retention", retention);

        var admitted = await _ExecuteAsync(command, lockedRead: false, _ReadGrantAsync, cancellationToken)
            .ConfigureAwait(false);

        return admitted.Match(
            accepted: static (grant, _) => grant,
            rejected: _ =>
                throw new InvalidOperationException(
                    $"Could not admit an attempt on the idempotency record '{key.Key}': it was missing or held by a "
                        + "live attempt inside the transaction that locked it and found it free."
                )
        );
    }

    public async ValueTask CompleteAsync(
        IUnitOfWork unitOfWork,
        IdempotencyRecordKey key,
        long generation,
        ReadOnlyMemory<byte> result,
        string contract,
        TimeSpan retention,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(contract);
        var (connection, transaction) = _RequireLive(Argument.IsNotNull(unitOfWork));

        await using var command = _Command(_completeSql, connection, transaction, key);
        _dialect.AddParameter(command, "Generation", SqlColumnType.Int64, generation);
        _dialect.AddParameter(command, "Result", SqlColumnType.Binary, result);
        _dialect.AddParameter(
            command,
            "ResultContract",
            SqlColumnType.Text(IdempotencyFieldLimits.ContractMaxLength),
            contract
        );
        _dialect.AddDuration(command, "Retention", retention);

        await _WriteOwnedAsync(command, key, "complete", cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask ReleaseAsync(
        IUnitOfWork unitOfWork,
        IdempotencyRecordKey key,
        long generation,
        TimeSpan retention,
        CancellationToken cancellationToken = default
    )
    {
        var (connection, transaction) = _RequireLive(Argument.IsNotNull(unitOfWork));

        await using var command = _Command(_releaseSql, connection, transaction, key);
        _dialect.AddParameter(command, "Generation", SqlColumnType.Int64, generation);
        _dialect.AddDuration(command, "Retention", retention);

        await _WriteOwnedAsync(command, key, "release", cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask SetRecoveryPointAsync(
        IUnitOfWork unitOfWork,
        IdempotencyRecordKey key,
        long generation,
        string point,
        ReadOnlyMemory<byte> state,
        string contract,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(point);
        Argument.IsNotNull(contract);
        var (connection, transaction) = _RequireLive(Argument.IsNotNull(unitOfWork));

        await using var command = _Command(_setRecoveryPointSql, connection, transaction, key);
        _dialect.AddParameter(command, "Generation", SqlColumnType.Int64, generation);
        _dialect.AddParameter(
            command,
            "RecoveryPoint",
            SqlColumnType.Text(IdempotencyFieldLimits.RecoveryPointMaxLength),
            point
        );
        _dialect.AddParameter(command, "RecoveryState", SqlColumnType.Binary, state);
        _dialect.AddParameter(
            command,
            "RecoveryContract",
            SqlColumnType.Text(IdempotencyFieldLimits.ContractMaxLength),
            contract
        );

        await _WriteOwnedAsync(command, key, "set the recovery point of", cancellationToken).ConfigureAwait(false);
    }

    private static async Task _WriteOwnedAsync(
        DbCommand command,
        IdempotencyRecordKey key,
        string verb,
        CancellationToken cancellationToken
    )
    {
        var written = await _ExecuteAsync(
                command,
                lockedRead: false,
                static (_, _) => ValueTask.FromResult(true),
                cancellationToken
            )
            .ConfigureAwait(false);

        // The caller locked the record and checked its generation in this transaction, so the row cannot have changed
        // since; a refusal means the table was changed outside this provider or the caller skipped the lock.
        _ = written.Match(
            accepted: static (_, _) => true,
            rejected: _ =>
                throw new InvalidOperationException(
                    $"Could not {verb} the idempotency record '{key.Key}': it was not found at the expected generation "
                        + "inside the transaction that locked it."
                )
        );
    }

    #endregion

    #region Renew and peek

    public ValueTask<IdempotentLeaseRenewal> RenewAsync(
        IdempotencyRecordKey key,
        long generation,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default
    )
    {
        return _RunAutonomousAsync(
            "idempotency.renew",
            async (connection, transaction, ct) =>
            {
                await using var command = _Command(_renewSql, connection, transaction, key);
                _dialect.AddParameter(command, "Generation", SqlColumnType.Int64, generation);
                _dialect.AddDuration(command, "LeaseDuration", leaseDuration);

                var renewed = await _ExecuteAsync(command, lockedRead: true, _ReadTimestampAsync, ct)
                    .ConfigureAwait(false);

                // The fence is this generation, pending, and live by the database clock, so a refused row still pending
                // at this generation has an expired lease.
                return renewed.Match(
                    accepted: static (expiresAt, _) =>
                        new IdempotentLeaseRenewal(IdempotentLeaseStatus.Current, expiresAt),
                    rejected: before =>
                        before is null
                            ? new IdempotentLeaseRenewal(IdempotentLeaseStatus.Stale, ExpiresAt: null)
                            : IdempotencyLeaseClassifier.Classify(
                                before.Status,
                                before.Generation,
                                isLeaseLive: false,
                                generation
                            ) switch
                            {
                                IdempotentLeaseStatus.Expired => new IdempotentLeaseRenewal(
                                    IdempotentLeaseStatus.Expired,
                                    before.LeaseExpiresAt
                                ),
                                var refused => new IdempotentLeaseRenewal(refused, ExpiresAt: null),
                            }
                );
            },
            cancellationToken
        );
    }

    public ValueTask<IdempotencyPeekStatus> PeekAsync(
        IdempotencyRecordKey key,
        CancellationToken cancellationToken = default
    )
    {
        return _RunReadOnlyAsync(
            "idempotency.peek",
            async (connection, transaction, ct) =>
            {
                await using var command = _Command(_peekSql, connection, transaction, key);
                await using var reader = await _ReaderAsync(command, ct).ConfigureAwait(false);

                if (!await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    return IdempotencyPeekStatus.Absent;
                }

                var status = (IdempotencyRecordStatus)reader.GetInt16(0);
                var retentionUntil = await reader.GetFieldValueAsync<DateTimeOffset>(1, ct).ConfigureAwait(false);
                var now = await reader.GetFieldValueAsync<DateTimeOffset>(2, ct).ConfigureAwait(false);

                if (retentionUntil <= now)
                {
                    return IdempotencyPeekStatus.Absent;
                }

                return status == IdempotencyRecordStatus.Completed
                    ? IdempotencyPeekStatus.Completed
                    : IdempotencyPeekStatus.Pending;
            },
            cancellationToken
        );
    }

    #endregion

    #region Purge

    public ValueTask<int> PurgeAsync(TimeSpan olderThan, int limit, CancellationToken cancellationToken = default)
    {
        Argument.IsPositiveOrZero(olderThan);
        Argument.IsPositive(limit);

        var age = olderThan > _MaxPurgeAge ? _MaxPurgeAge : olderThan;

        return _RunAutonomousAsync(
            "idempotency.purge",
            async (connection, transaction, ct) =>
            {
                await using var command = _Command(_purgeSql, connection, transaction);
                _dialect.AddParameter(command, "Limit", SqlColumnType.Int32, limit);
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
        );
    }

    #endregion

    #region Helpers

    private string _Transition(string fence, string set, IReadOnlyList<string> returning)
    {
        return _dialect.Render(new SqlFencedTransition(_t.Table, _t.KeyColumns, fence, set, returning));
    }

    /// <summary>The later of the stored retention and the database clock plus <c>@Retention</c>: retention only extends.</summary>
    private string _ExtendedRetention()
    {
        var extendTo = _dialect.ShiftByDuration(SqlDialectTokens.Now, "Retention");

        return $"CASE WHEN {_t.RetentionUntil} > {extendTo} THEN {_t.RetentionUntil} ELSE {extendTo} END";
    }

    private ValueTask<T> _RunAutonomousAsync<T>(
        string operation,
        Func<DbConnection, DbTransaction, CancellationToken, Task<T>> body,
        CancellationToken cancellationToken
    )
    {
        return SqlAutonomousTransaction.RunAsync(
            operation,
            _storage.CreateConnection,
            body,
            _timeProvider,
            cancellationToken
        );
    }

    /// <summary>
    /// Runs a read that takes no lock of its own beyond what its isolation level implies. An engine whose autonomous
    /// transaction already reads that way runs it as any autonomous call: PostgreSQL's MVCC read never waits, and SQL
    /// Server at READ COMMITTED without a lock hint never waits behind an update lock, though with
    /// READ_COMMITTED_SNAPSHOT off it still waits behind an uncommitted writer's exclusive lock. An engine whose
    /// autonomous transaction takes a write lock at begin (SQLite) supplies a transaction that does not.
    /// </summary>
    private ValueTask<T> _RunReadOnlyAsync<T>(
        string operation,
        Func<DbConnection, DbTransaction, CancellationToken, Task<T>> body,
        CancellationToken cancellationToken
    )
    {
        if (_storage.BeginReadOnlyTransaction is not { } begin)
        {
            return _RunAutonomousAsync(operation, body, cancellationToken);
        }

        return SqlAutonomousTransaction.RetryAsync(
            operation,
            async (attempt, ct) =>
            {
                await using var connection = _storage.CreateConnection();
                await connection.OpenAsync(ct).ConfigureAwait(false);
                await using var transaction = await begin(connection, ct).ConfigureAwait(false);
                var result = await body(connection, transaction, ct).ConfigureAwait(false);

                // The read wrote nothing, so its commit only ends the transaction.
                attempt.MarkCommitStarted();
                await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);

                return result;
            },
            _timeProvider,
            onRetry: null,
            cancellationToken
        );
    }

    private DbCommand _Command(
        string sql,
        DbConnection connection,
        DbTransaction transaction,
        IdempotencyRecordKey? key = null
    )
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
                SqlColumnType.KeyText(IdempotencyFieldLimits.TenantIdMaxLength),
                value.TenantId
            );
            _dialect.AddParameter(
                command,
                "IdempotencyKey",
                SqlColumnType.KeyText(IdempotencyFieldLimits.KeyMaxLength),
                value.Key
            );
        }

        return command;
    }

    private void _AddFingerprint(DbCommand command, IdempotencyFingerprint fingerprint)
    {
        _dialect.AddParameter(
            command,
            "FingerprintAlgorithm",
            SqlColumnType.KeyText(IdempotencyFieldLimits.FingerprintAlgorithmMaxLength),
            fingerprint.Algorithm
        );
        _dialect.AddParameter(command, "Fingerprint", SqlColumnType.Binary, fingerprint.Hash);
    }

    private static async Task<SqlFenced<RenewalRow, TAccepted>> _ExecuteAsync<TAccepted>(
        DbCommand command,
        bool lockedRead,
        Func<DbDataReader, CancellationToken, ValueTask<TAccepted>> readAccepted,
        CancellationToken cancellationToken
    )
    {
        try
        {
            return await SqlFencedCommand
                .ExecuteAsync(command, lockedRead, _ReadRenewalRowAsync, readAccepted, cancellationToken)
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

    /// <summary>Reads the whole record a locking read returned, from ordinal 0.</summary>
    private static async Task<RecordRow> _ReadRecordAsync(DbDataReader reader, CancellationToken cancellationToken)
    {
        var status = (IdempotencyRecordStatus)reader.GetInt16(0);
        var fingerprint = new IdempotencyFingerprint(
            reader.GetString(1),
            await reader.GetFieldValueAsync<byte[]>(2, cancellationToken).ConfigureAwait(false)
        );
        var generation = await _ReadInt64OrNullAsync(reader, 3, cancellationToken).ConfigureAwait(false);
        var leaseExpiresAt = await _ReadTimestampOrNullAsync(reader, 4, cancellationToken).ConfigureAwait(false);
        IdempotentResult? result = null;

        if (status == IdempotencyRecordStatus.Completed)
        {
            result = new IdempotentResult(
                await reader.GetFieldValueAsync<byte[]>(5, cancellationToken).ConfigureAwait(false),
                reader.GetString(6)
            );
        }

        var retentionUntil = await reader
            .GetFieldValueAsync<DateTimeOffset>(7, cancellationToken)
            .ConfigureAwait(false);
        IdempotentRecoveryPoint? recoveryPoint = null;

        if (!await reader.IsDBNullAsync(8, cancellationToken).ConfigureAwait(false))
        {
            recoveryPoint = new IdempotentRecoveryPoint(
                reader.GetString(8),
                await reader.GetFieldValueAsync<byte[]>(9, cancellationToken).ConfigureAwait(false),
                reader.GetString(10)
            );
        }

        return new RecordRow(status, fingerprint, generation, leaseExpiresAt, result, retentionUntil, recoveryPoint);
    }

    /// <summary>Reads the renewal's locking read: status, generation, and lease expiry, from ordinal 0.</summary>
    private static async ValueTask<RenewalRow> _ReadRenewalRowAsync(
        DbDataReader reader,
        CancellationToken cancellationToken
    )
    {
        return new RenewalRow(
            (IdempotencyRecordStatus)reader.GetInt16(0),
            await _ReadInt64OrNullAsync(reader, 1, cancellationToken).ConfigureAwait(false),
            await _ReadTimestampOrNullAsync(reader, 2, cancellationToken).ConfigureAwait(false)
        );
    }

    private static ValueTask<DateTimeOffset> _ReadTimestampAsync(
        DbDataReader reader,
        CancellationToken cancellationToken
    )
    {
        return new(reader.GetFieldValueAsync<DateTimeOffset>(1, cancellationToken));
    }

    private static async ValueTask<IdempotencyRecordGrant> _ReadGrantAsync(
        DbDataReader reader,
        CancellationToken cancellationToken
    )
    {
        return new IdempotencyRecordGrant(
            reader.GetInt64(1),
            await reader.GetFieldValueAsync<DateTimeOffset>(2, cancellationToken).ConfigureAwait(false)
        );
    }

    private static async Task<long?> _ReadInt64OrNullAsync(
        DbDataReader reader,
        int ordinal,
        CancellationToken cancellationToken
    )
    {
        return await reader.IsDBNullAsync(ordinal, cancellationToken).ConfigureAwait(false)
            ? null
            : reader.GetInt64(ordinal);
    }

    private static async Task<DateTimeOffset?> _ReadTimestampOrNullAsync(
        DbDataReader reader,
        int ordinal,
        CancellationToken cancellationToken
    )
    {
        return await reader.IsDBNullAsync(ordinal, cancellationToken).ConfigureAwait(false)
            ? null
            : await reader.GetFieldValueAsync<DateTimeOffset>(ordinal, cancellationToken).ConfigureAwait(false);
    }

    private (DbConnection Connection, DbTransaction Transaction) _RequireLive(IUnitOfWork unitOfWork)
    {
        return RelationalEnlistment.RequireLive(
            unitOfWork,
            _dialect.ConnectionType,
            _dialect.TransactionType,
            _storage.PackageName,
            UnitOfWorkIdempotencyFeature.Operation
        );
    }

    #endregion

    /// <summary>A record as a locking read found it.</summary>
    private sealed record RecordRow(
        IdempotencyRecordStatus Status,
        IdempotencyFingerprint Fingerprint,
        long? Generation,
        DateTimeOffset? LeaseExpiresAt,
        IdempotentResult? Result,
        DateTimeOffset RetentionUntil,
        IdempotentRecoveryPoint? RecoveryPoint
    );

    /// <summary>What a renewal's locking read finds: enough to classify a refused renewal.</summary>
    private sealed record RenewalRow(IdempotencyRecordStatus Status, long? Generation, DateTimeOffset? LeaseExpiresAt);
}
#pragma warning restore CA2100
