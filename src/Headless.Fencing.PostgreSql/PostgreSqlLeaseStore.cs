// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Data.Common;
using System.Runtime.InteropServices;
using Headless.Checks;
using Headless.Constants;
using Headless.UnitOfWork;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace Headless.Fencing.PostgreSql;

/// <summary>
/// The PostgreSQL lease store. Every verb that can wait on a lease row first takes the row's update-intent lock and
/// only then reads <c>clock_timestamp()</c>, once, in the statement that decides, so a decision is never made on a
/// clock read from before a lock wait.
/// </summary>
/// <remarks>
/// <para>
/// <c>clock_timestamp()</c> is captured in a <c>MATERIALIZED</c> CTE, never <c>now()</c>: <c>now()</c> is frozen at
/// transaction start, so inside a long enlisted unit it would keep an expired lease looking live.
/// </para>
/// <para>
/// The lock is <c>FOR NO KEY UPDATE</c> rather than a shared lock. A fence holds it until the caller's transaction
/// ends, and a grant, renewal, settlement, or sweep waiting on it then sees the committed outcome. With a shared lock
/// a waiting grant would queue for the update lock first, and the fencing transaction's own settlement would then
/// deadlock against it.
/// </para>
/// </remarks>
#pragma warning disable CA2100 // SQL text is built from the validated schema name plus internal object and column constants.
internal sealed class PostgreSqlLeaseStore : ILeaseStore
{
    // A deadlock or serialization failure is the one failure a fresh transaction can clear on its own; the first
    // attempt plus two retries.
    private const int _MaxAttempts = 3;

    // Each purge batch is its own short transaction so a large purge never holds many row locks at once.
    private const int _PurgeBatchSize = 1000;

    // A lost insert race means another transaction committed the row between the locking read and the insert, so
    // the next locking read finds it. Only a row purged again in that window could send the loop round once more.
    private const int _MaxGrantRounds = 5;

    // timestamptz reaches back to 4713 BC, so a purge cutoff further back than this would overflow. No lease can
    // have ended that long ago, so clamping the age deletes exactly the same rows.
    private const int _MaxPurgeAgeDays = 700_000;

    private readonly PostgreSqlFencingOptions _options;
    private readonly IUnitOfWorkFactory _unitOfWorkFactory;
    private readonly string _grantExistingSql;
    private readonly string _grantInsertSql;
    private readonly string _renewSql;
    private readonly string _settleSql;
    private readonly string _releaseSql;
    private readonly string _fenceSql;
    private readonly string _claimFirstSql;
    private readonly string _claimAfterSql;
    private readonly string _purgeSql;

    public PostgreSqlLeaseStore(
        IOptions<PostgreSqlFencingOptions> options,
        IOptions<FencingStorageOptions> storageOptions,
        IUnitOfWorkFactory unitOfWorkFactory
    )
    {
        _options = options.Value;
        _unitOfWorkFactory = unitOfWorkFactory;

        var schema = storageOptions.Value.Schema;
        var table = PostgreSqlFencingSchema.QualifiedTable(schema);
        var sequence = PostgreSqlFencingSchema.QualifiedSequence(schema);

        _grantExistingSql = _BuildGrantExistingSql(table, sequence);
        _grantInsertSql = _BuildGrantInsertSql(table, sequence);
        _renewSql = _BuildTransitionSql(table, $"{PostgreSqlFencingSchema.ExpiresAt} = clock.now + @Duration");
        _settleSql = _BuildTransitionSql(
            table,
            $"{PostgreSqlFencingSchema.State} = {PostgreSqlFencingSchema.Settled}, {PostgreSqlFencingSchema.EndedAt} = clock.now"
        );
        _releaseSql = _BuildTransitionSql(
            table,
            $"{PostgreSqlFencingSchema.State} = {PostgreSqlFencingSchema.Released}, {PostgreSqlFencingSchema.EndedAt} = clock.now"
        );
        _fenceSql = _BuildFenceSql(table);
        _claimFirstSql = _BuildClaimSql(table, withCursor: false);
        _claimAfterSql = _BuildClaimSql(table, withCursor: true);
        _purgeSql = _BuildPurgeSql(table);
    }

    #region Owned units and enlistment

    public async ValueTask<IUnitOfWork> BeginOwnedUnitAsync(CancellationToken cancellationToken = default)
    {
#pragma warning disable CA2000 // The owned unit opens this connection and closes it when the unit ends; disposing it here would end the unit's transaction.
        var connection = _options.CreateConnection();
#pragma warning restore CA2000

        try
        {
            return await _unitOfWorkFactory
                .BeginAsync(connection, IsolationLevel.ReadCommitted, cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);

            throw;
        }
    }

    public void ValidateEnlistment(IRelationalUnitOfWorkResource resource)
    {
        Argument.IsNotNull(resource);

        var (connection, _) = _RequireLive(resource);

        using var configured = _options.CreateConnection();

        if (!RelationalDatabaseIdentity.IsSameDatabase(configured, connection))
        {
            throw new InvalidOperationException(
                $"The unit of work's connection targets database '{connection.Database}', but "
                    + $"Headless.Fencing.PostgreSql is configured for database '{configured.Database}'. An enlisted "
                    + "lease call runs in the unit's own transaction, so the unit must run on the database that holds "
                    + "the leases."
            );
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
        IRelationalUnitOfWorkResource resource,
        LeaseKey key,
        TimeSpan duration,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(resource);

        // Re-checked here rather than trusted from validation: the caller may have ended the transaction or closed
        // the connection in between, and a statement on either would fail with a less useful message or, worse, run
        // outside the unit. No retry: a deadlock has already rolled back the caller's transaction, so only the unit's
        // owner can decide whether to run the whole unit again.
        var (connection, transaction) = _RequireLive(resource);

        return await _GrantAsync(connection, transaction, key, duration, cancellationToken).ConfigureAwait(false);
    }

    private async Task<LeaseGrantResult> _GrantAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        LeaseKey key,
        TimeSpan duration,
        CancellationToken cancellationToken
    )
    {
        for (var round = 1; round <= _MaxGrantRounds; round++)
        {
            // The locking read and the takeover run as one round trip. The takeover statement starts only after the
            // read holds the row lock, so its clock and its nextval() both come after any wait: a generation drawn
            // before the lock could be lower than one a still-open grant already holds.
            await using (var command = _CreateCommand(_grantExistingSql, connection, transaction, key))
            {
                command.Parameters.Add(_DurationParameter(duration));

                await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

                LeaseRow? existing = null;

                if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    existing = new LeaseRow(
                        reader.GetInt64(0),
                        reader.GetInt16(1),
                        await reader.GetFieldValueAsync<DateTimeOffset>(2, cancellationToken).ConfigureAwait(false),
                        IsLive: false
                    );
                }

                await reader.NextResultAsync(cancellationToken).ConfigureAwait(false);

                if (existing is { } row)
                {
                    if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        // The takeover statement matched nothing, so the row is a live active lease.
                        return LeaseGrantResult.Held(row.Generation, row.ExpiresAt);
                    }

                    var lease = key.ToLease(reader.GetInt64(0));
                    var expiresAt = await reader
                        .GetFieldValueAsync<DateTimeOffset>(1, cancellationToken)
                        .ConfigureAwait(false);

                    return row.State == PostgreSqlFencingSchema.Active
                        ? LeaseGrantResult.Takeover(lease, expiresAt, row.Generation)
                        : LeaseGrantResult.Granted(lease, expiresAt);
                }
            }

            // No row: insert one. ON CONFLICT DO NOTHING rather than DO UPDATE, because a refused update returns no
            // row and RETURNING cannot expose the holder's values. A conflicting insert waits for the other
            // transaction; when it committed, nothing is inserted and the next round's locking read finds its row.
            await using (var insert = _CreateCommand(_grantInsertSql, connection, transaction, key))
            {
                insert.Parameters.Add(_DurationParameter(duration));

                await using var reader = await insert.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

                if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    var generation = reader.GetInt64(0);
                    var expiresAt = await reader
                        .GetFieldValueAsync<DateTimeOffset>(1, cancellationToken)
                        .ConfigureAwait(false);

                    return LeaseGrantResult.Granted(key.ToLease(generation), expiresAt);
                }
            }
        }

        throw new InvalidOperationException(
            $"The lease '{key.Kind}/{key.Resource}' changed between every locking read and insert for "
                + $"{_MaxGrantRounds} rounds; the grant gave up instead of looping."
        );
    }

    #endregion

    #region Renew, settle, release

    public ValueTask<LeaseRenewalResult> RenewAsync(
        LeaseKey key,
        long generation,
        TimeSpan duration,
        CancellationToken cancellationToken = default
    )
    {
        return _RunAutonomousAsync(
            (connection, transaction, ct) => _RenewAsync(connection, transaction, key, generation, duration, ct),
            cancellationToken
        );
    }

    public async ValueTask<LeaseRenewalResult> RenewEnlistedAsync(
        IRelationalUnitOfWorkResource resource,
        LeaseKey key,
        long generation,
        TimeSpan duration,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(resource);
        var (connection, transaction) = _RequireLive(resource);

        return await _RenewAsync(connection, transaction, key, generation, duration, cancellationToken)
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
        IRelationalUnitOfWorkResource resource,
        LeaseKey key,
        long generation,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(resource);
        var (connection, transaction) = _RequireLive(resource);

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
        IRelationalUnitOfWorkResource resource,
        LeaseKey key,
        long generation,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(resource);
        var (connection, transaction) = _RequireLive(resource);

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

    private async Task<LeaseRenewalResult> _RenewAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        LeaseKey key,
        long generation,
        TimeSpan duration,
        CancellationToken cancellationToken
    )
    {
        var (row, changedExpiresAt) = await _TransitionAsync(
                connection,
                transaction,
                _renewSql,
                key,
                generation,
                duration,
                cancellationToken
            )
            .ConfigureAwait(false);

        if (changedExpiresAt is { } renewedUntil)
        {
            return new LeaseRenewalResult(LeaseRenewalStatus.Renewed, renewedUntil);
        }

        return _Classify(row, generation) switch
        {
            LeaseFenceStatus.Expired => new LeaseRenewalResult(LeaseRenewalStatus.Expired, row!.Value.ExpiresAt),
            LeaseFenceStatus.Stale => new LeaseRenewalResult(LeaseRenewalStatus.Stale, ExpiresAt: null),
            LeaseFenceStatus.Settled => new LeaseRenewalResult(LeaseRenewalStatus.Settled, ExpiresAt: null),
            LeaseFenceStatus.Released => new LeaseRenewalResult(LeaseRenewalStatus.Released, ExpiresAt: null),
            LeaseFenceStatus.Abandoned => new LeaseRenewalResult(LeaseRenewalStatus.Abandoned, ExpiresAt: null),
            _ => throw _UnchangedLiveLease(key),
        };
    }

    private async Task<LeaseSettlementStatus> _EndAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string sql,
        LeaseSettlementStatus success,
        LeaseKey key,
        long generation,
        CancellationToken cancellationToken
    )
    {
        var (row, changedExpiresAt) = await _TransitionAsync(
                connection,
                transaction,
                sql,
                key,
                generation,
                duration: null,
                cancellationToken
            )
            .ConfigureAwait(false);

        if (changedExpiresAt is not null)
        {
            return success;
        }

        // A settled or released row at this generation reports its state whichever verb asked, so a retried
        // settlement reports success and a release after a settlement reports that the attempt settled.
        return _Classify(row, generation) switch
        {
            LeaseFenceStatus.Settled => LeaseSettlementStatus.Settled,
            LeaseFenceStatus.Released => LeaseSettlementStatus.Released,
            LeaseFenceStatus.Abandoned => LeaseSettlementStatus.Abandoned,
            LeaseFenceStatus.Expired => LeaseSettlementStatus.Expired,
            LeaseFenceStatus.Stale => LeaseSettlementStatus.Stale,
            _ => throw _UnchangedLiveLease(key),
        };
    }

    private async Task<(LeaseRow? Row, DateTimeOffset? ChangedExpiresAt)> _TransitionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string sql,
        LeaseKey key,
        long generation,
        TimeSpan? duration,
        CancellationToken cancellationToken
    )
    {
        await using var command = _CreateCommand(sql, connection, transaction, key);
        command.Parameters.Add(_GenerationParameter(generation));

        if (duration is { } value)
        {
            command.Parameters.Add(_DurationParameter(value));
        }

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        // The first result set is the locking read; the decision is the second statement's single row.
        await reader.NextResultAsync(cancellationToken).ConfigureAwait(false);
        await reader.ReadAsync(cancellationToken).ConfigureAwait(false);

        var row = await _ReadRowAsync(reader, cancellationToken).ConfigureAwait(false);
        DateTimeOffset? changed = await reader.IsDBNullAsync(4, cancellationToken).ConfigureAwait(false)
            ? null
            : await reader.GetFieldValueAsync<DateTimeOffset>(4, cancellationToken).ConfigureAwait(false);

        return (row, changed);
    }

    #endregion

    #region Fence

    public async ValueTask<LeaseFenceStatus> FenceEnlistedAsync(
        IRelationalUnitOfWorkResource resource,
        LeaseKey key,
        long generation,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(resource);
        var (connection, transaction) = _RequireLive(resource);

        await using var command = _CreateCommand(_fenceSql, connection, transaction, key);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        await reader.NextResultAsync(cancellationToken).ConfigureAwait(false);
        await reader.ReadAsync(cancellationToken).ConfigureAwait(false);

        return _Classify(await _ReadRowAsync(reader, cancellationToken).ConfigureAwait(false), generation);
    }

    #endregion

    #region Sweep and purge

    public async ValueTask<ExpiredLease?> ClaimExpiredEnlistedAsync(
        IRelationalUnitOfWorkResource resource,
        string kind,
        ExpiredLease? after,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(resource);
        Argument.IsNotNull(kind);
        var (connection, transaction) = _RequireLive(resource);

        await using var command = new NpgsqlCommand(
            after is null ? _claimFirstSql : _claimAfterSql,
            connection,
            transaction
        );
        command.CommandTimeout = _options.CommandTimeoutSeconds;
        command.Parameters.Add(_TextParameter("Kind", kind));

        if (after is not null)
        {
            command.Parameters.Add(
                new NpgsqlParameter<DateTimeOffset>("AfterExpiresAt", NpgsqlDbType.TimestampTz)
                {
                    TypedValue = after.ExpiresAt.ToUniversalTime(),
                }
            );
            command.Parameters.Add(_TextParameter("AfterTenantId", after.TenantId ?? string.Empty));
            command.Parameters.Add(_TextParameter("AfterResource", after.Resource));
        }

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var key = new LeaseKey(reader.GetString(0), kind, reader.GetString(1));
        var generation = reader.GetInt64(2);
        var expiresAt = await reader.GetFieldValueAsync<DateTimeOffset>(3, cancellationToken).ConfigureAwait(false);

        return key.ToExpiredLease(generation, expiresAt);
    }

    public async ValueTask<int> PurgeAsync(
        string kind,
        TimeSpan olderThan,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(kind);
        Argument.IsPositiveOrZero(olderThan);

        var age = olderThan > TimeSpan.FromDays(_MaxPurgeAgeDays) ? TimeSpan.FromDays(_MaxPurgeAgeDays) : olderThan;
        var total = 0;

        while (true)
        {
            var deleted = await _RunAutonomousAsync(
                    async (connection, transaction, ct) =>
                    {
                        await using var command = new NpgsqlCommand(_purgeSql, connection, transaction);
                        command.CommandTimeout = _options.CommandTimeoutSeconds;
                        command.Parameters.Add(_TextParameter("Kind", kind));
                        command.Parameters.Add(
                            new NpgsqlParameter<TimeSpan>("OlderThan", NpgsqlDbType.Interval) { TypedValue = age }
                        );
                        command.Parameters.Add(
                            new NpgsqlParameter<int>("BatchSize", NpgsqlDbType.Integer) { TypedValue = _PurgeBatchSize }
                        );

                        return await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
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

    private async ValueTask<T> _RunAutonomousAsync<T>(
        Func<NpgsqlConnection, NpgsqlTransaction, CancellationToken, Task<T>> body,
        CancellationToken cancellationToken
    )
    {
        for (var attempt = 1; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await using var connection = _options.CreateConnection();
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                // Explicit so the verb runs at READ COMMITTED even when the server's default isolation level is
                // stricter: a stricter level turns a lost insert race or a lock wait into a serialization failure.
                await using var transaction = await connection
                    .BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
                    .ConfigureAwait(false);

                var result = await body(connection, transaction, cancellationToken).ConfigureAwait(false);

                // The statement already decided and the result describes it; a late cancel must not roll back a
                // write the caller is about to be told happened.
                await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);

                return result;
            }
            catch (PostgresException ex) when (_IsRetryable(ex) && attempt < _MaxAttempts)
            {
                // The victim's transaction is already rolled back and disposed above; the next attempt starts clean.
            }
        }
    }

    private static bool _IsRetryable(PostgresException ex)
    {
        return string.Equals(ex.SqlState, SqlErrorCodes.PostgreSql.DeadlockDetected, StringComparison.Ordinal)
            || string.Equals(ex.SqlState, SqlErrorCodes.PostgreSql.SerializationFailure, StringComparison.Ordinal);
    }

    private NpgsqlCommand _CreateCommand(
        string sql,
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        LeaseKey key
    )
    {
        var command = new NpgsqlCommand(sql, connection, transaction)
        {
            CommandTimeout = _options.CommandTimeoutSeconds,
        };
        command.Parameters.Add(_TextParameter("TenantId", key.TenantId));
        command.Parameters.Add(_TextParameter("Kind", key.Kind));
        command.Parameters.Add(_TextParameter("Resource", key.Resource));

        return command;
    }

    private static NpgsqlParameter<string> _TextParameter(string name, string value)
    {
        return new NpgsqlParameter<string>(name, NpgsqlDbType.Varchar) { TypedValue = value };
    }

    private static NpgsqlParameter<long> _GenerationParameter(long generation)
    {
        return new NpgsqlParameter<long>("Generation", NpgsqlDbType.Bigint) { TypedValue = generation };
    }

    private static NpgsqlParameter<TimeSpan> _DurationParameter(TimeSpan duration)
    {
        return new NpgsqlParameter<TimeSpan>("Duration", NpgsqlDbType.Interval) { TypedValue = duration };
    }

    /// <summary>Reads the decision row's first four columns: generation, state, expiry, and whether it is live.</summary>
    private static async Task<LeaseRow?> _ReadRowAsync(DbDataReader reader, CancellationToken cancellationToken)
    {
        if (await reader.IsDBNullAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var generation = reader.GetInt64(0);
        var state = reader.GetInt16(1);
        var expiresAt = await reader.GetFieldValueAsync<DateTimeOffset>(2, cancellationToken).ConfigureAwait(false);

        return new LeaseRow(generation, state, expiresAt, reader.GetBoolean(3));
    }

    private static LeaseFenceStatus _Classify(LeaseRow? row, long generation)
    {
        if (row is not { } value || value.Generation != generation)
        {
            return LeaseFenceStatus.Stale;
        }

        return value.State switch
        {
            PostgreSqlFencingSchema.Settled => LeaseFenceStatus.Settled,
            PostgreSqlFencingSchema.Released => LeaseFenceStatus.Released,
            PostgreSqlFencingSchema.Abandoned => LeaseFenceStatus.Abandoned,
            PostgreSqlFencingSchema.Active when value.IsLive => LeaseFenceStatus.Current,
            PostgreSqlFencingSchema.Active => LeaseFenceStatus.Expired,
            _ => throw new InvalidOperationException(
                $"The lease row carries an unknown state {value.State}; the table was changed outside this provider."
            ),
        };
    }

    private static InvalidOperationException _UnchangedLiveLease(LeaseKey key)
    {
        // The update and the classifying read share one clock snapshot and the row lock, so a live row at this
        // generation is always changed; reaching here means the table was changed outside this provider.
        return new InvalidOperationException(
            $"The lease '{key.Kind}/{key.Resource}' was live at the caller's generation but not updated."
        );
    }

    private static (NpgsqlConnection Connection, NpgsqlTransaction Transaction) _RequireLive(
        IRelationalUnitOfWorkResource resource
    )
    {
        if (resource.Transaction is not NpgsqlTransaction transaction)
        {
            throw new InvalidOperationException(
                $"The unit of work carries a '{resource.Transaction.GetType().FullName}' transaction, but "
                    + "Headless.Fencing.PostgreSql runs enlisted lease calls only through an NpgsqlTransaction. Begin "
                    + "the unit on the PostgreSQL database that holds the leases."
            );
        }

        if (resource.Connection is not NpgsqlConnection { State: ConnectionState.Open } connection)
        {
            throw new InvalidOperationException(
                "The unit of work's connection is not an open NpgsqlConnection, so the lease call cannot run inside "
                    + "its transaction."
            );
        }

        if (!ReferenceEquals(transaction.Connection, connection))
        {
            throw new InvalidOperationException(
                "The unit of work's transaction is not bound to its connection, so a command on that connection "
                    + "would run outside the unit's transaction."
            );
        }

        return (connection, transaction);
    }

    #endregion

    #region SQL

    private static string _KeyPredicate(string alias)
    {
        return $"""
            {alias}.{PostgreSqlFencingSchema.TenantId} = @TenantId
                AND {alias}.{PostgreSqlFencingSchema.Kind} = @Kind
                AND {alias}.{PostgreSqlFencingSchema.Resource} = @Resource
            """;
    }

    private static string _LockSql(string table)
    {
        // Takes the row's update-intent lock and nothing else. Every decision reads the clock in a later statement,
        // after this one has waited out any other holder.
        return $"""
            SELECT l.{PostgreSqlFencingSchema.Generation}, l.{PostgreSqlFencingSchema.State}, l.{PostgreSqlFencingSchema.ExpiresAt}
            FROM {table} AS l
            WHERE {_KeyPredicate("l")}
            FOR NO KEY UPDATE;
            """;
    }

    private static string _BuildGrantExistingSql(string table, string sequence)
    {
        // nextval() sits in the SET list, so it is drawn only for a row the WHERE accepts, and only after the
        // preceding read holds the lock.
        return $"""
            {_LockSql(table)}

            WITH clock AS MATERIALIZED (SELECT clock_timestamp() AS now)
            UPDATE {table} AS l
            SET {PostgreSqlFencingSchema.Generation} = nextval('{sequence}'),
                {PostgreSqlFencingSchema.State} = {PostgreSqlFencingSchema.Active},
                {PostgreSqlFencingSchema.GrantedAt} = clock.now,
                {PostgreSqlFencingSchema.ExpiresAt} = clock.now + @Duration,
                {PostgreSqlFencingSchema.EndedAt} = NULL
            FROM clock
            WHERE {_KeyPredicate("l")}
                AND NOT (
                    l.{PostgreSqlFencingSchema.State} = {PostgreSqlFencingSchema.Active}
                    AND l.{PostgreSqlFencingSchema.ExpiresAt} > clock.now
                )
            RETURNING l.{PostgreSqlFencingSchema.Generation}, l.{PostgreSqlFencingSchema.ExpiresAt};
            """;
    }

    private static string _BuildGrantInsertSql(string table, string sequence)
    {
        return $"""
            WITH clock AS MATERIALIZED (SELECT clock_timestamp() AS now)
            INSERT INTO {table} (
                {PostgreSqlFencingSchema.TenantId},
                {PostgreSqlFencingSchema.Kind},
                {PostgreSqlFencingSchema.Resource},
                {PostgreSqlFencingSchema.Generation},
                {PostgreSqlFencingSchema.State},
                {PostgreSqlFencingSchema.GrantedAt},
                {PostgreSqlFencingSchema.ExpiresAt},
                {PostgreSqlFencingSchema.EndedAt}
            )
            SELECT @TenantId, @Kind, @Resource, nextval('{sequence}'), {PostgreSqlFencingSchema.Active},
                clock.now, clock.now + @Duration, NULL
            FROM clock
            ON CONFLICT ({PostgreSqlFencingSchema.TenantId}, {PostgreSqlFencingSchema.Kind}, {PostgreSqlFencingSchema.Resource})
            DO NOTHING
            RETURNING {PostgreSqlFencingSchema.Generation}, {PostgreSqlFencingSchema.ExpiresAt};
            """;
    }

    private static string _BuildTransitionSql(string table, string setClause)
    {
        // The outer SELECT reads the row as it was before the update (the statement shares one snapshot), which is
        // what classifies a refusal; the update's RETURNING reports whether it applied.
        return $"""
            {_LockSql(table)}

            WITH clock AS MATERIALIZED (SELECT clock_timestamp() AS now),
            changed AS (
                UPDATE {table} AS l
                SET {setClause}
                FROM clock
                WHERE {_KeyPredicate("l")}
                    AND l.{PostgreSqlFencingSchema.Generation} = @Generation
                    AND l.{PostgreSqlFencingSchema.State} = {PostgreSqlFencingSchema.Active}
                    AND l.{PostgreSqlFencingSchema.ExpiresAt} > clock.now
                RETURNING l.{PostgreSqlFencingSchema.ExpiresAt}
            )
            SELECT l.{PostgreSqlFencingSchema.Generation},
                l.{PostgreSqlFencingSchema.State},
                l.{PostgreSqlFencingSchema.ExpiresAt},
                l.{PostgreSqlFencingSchema.ExpiresAt} > clock.now,
                (SELECT c.{PostgreSqlFencingSchema.ExpiresAt} FROM changed AS c)
            FROM clock
            LEFT JOIN {table} AS l ON {_KeyPredicate("l")};
            """;
    }

    private static string _BuildFenceSql(string table)
    {
        return $"""
            {_LockSql(table)}

            WITH clock AS MATERIALIZED (SELECT clock_timestamp() AS now)
            SELECT l.{PostgreSqlFencingSchema.Generation},
                l.{PostgreSqlFencingSchema.State},
                l.{PostgreSqlFencingSchema.ExpiresAt},
                l.{PostgreSqlFencingSchema.ExpiresAt} > clock.now
            FROM clock
            LEFT JOIN {table} AS l ON {_KeyPredicate("l")};
            """;
    }

    private static string _BuildClaimSql(string table, bool withCursor)
    {
        // SKIP LOCKED passes over a lease another sweeper is claiming or a fence is holding, so concurrent sweepers
        // never hand one lease to two handlers and never wait on each other. The keyset cursor walks the active
        // expiry index; a lease the caller already visited is behind it, so a lease whose handler threw is left
        // for a later call.
        var cursor = withCursor
            ? $"""
                AND (l.{PostgreSqlFencingSchema.ExpiresAt}, l.{PostgreSqlFencingSchema.TenantId}, l.{PostgreSqlFencingSchema.Resource})
                    > (@AfterExpiresAt, @AfterTenantId, @AfterResource)
                """
            : string.Empty;

        return $"""
            WITH clock AS MATERIALIZED (SELECT clock_timestamp() AS now),
            candidate AS (
                SELECT l.{PostgreSqlFencingSchema.TenantId}, l.{PostgreSqlFencingSchema.Resource}
                FROM {table} AS l, clock
                WHERE l.{PostgreSqlFencingSchema.Kind} = @Kind
                    AND l.{PostgreSqlFencingSchema.State} = {PostgreSqlFencingSchema.Active}
                    AND l.{PostgreSqlFencingSchema.ExpiresAt} <= clock.now
                    {cursor}
                ORDER BY l.{PostgreSqlFencingSchema.ExpiresAt}, l.{PostgreSqlFencingSchema.TenantId}, l.{PostgreSqlFencingSchema.Resource}
                LIMIT 1
                FOR UPDATE OF l SKIP LOCKED
            )
            UPDATE {table} AS l
            SET {PostgreSqlFencingSchema.State} = {PostgreSqlFencingSchema.Abandoned},
                {PostgreSqlFencingSchema.EndedAt} = clock.now
            FROM candidate, clock
            WHERE l.{PostgreSqlFencingSchema.TenantId} = candidate.{PostgreSqlFencingSchema.TenantId}
                AND l.{PostgreSqlFencingSchema.Kind} = @Kind
                AND l.{PostgreSqlFencingSchema.Resource} = candidate.{PostgreSqlFencingSchema.Resource}
            RETURNING l.{PostgreSqlFencingSchema.TenantId},
                l.{PostgreSqlFencingSchema.Resource},
                l.{PostgreSqlFencingSchema.Generation},
                l.{PostgreSqlFencingSchema.ExpiresAt};
            """;
    }

    private static string _BuildPurgeSql(string table)
    {
        // Only ended rows are deleted; an active row, expired or not, is kept. SKIP LOCKED leaves a row a grant is
        // re-granting for a later purge. Deleting a row never lets a generation repeat, because generations come
        // from the store-wide sequence.
        return $"""
            WITH clock AS MATERIALIZED (SELECT clock_timestamp() AS now),
            doomed AS (
                SELECT l.{PostgreSqlFencingSchema.TenantId}, l.{PostgreSqlFencingSchema.Resource}
                FROM {table} AS l, clock
                WHERE l.{PostgreSqlFencingSchema.Kind} = @Kind
                    AND l.{PostgreSqlFencingSchema.State} <> {PostgreSqlFencingSchema.Active}
                    AND l.{PostgreSqlFencingSchema.EndedAt} <= clock.now - @OlderThan
                LIMIT @BatchSize
                FOR UPDATE OF l SKIP LOCKED
            )
            DELETE FROM {table} AS l
            USING doomed
            WHERE l.{PostgreSqlFencingSchema.TenantId} = doomed.{PostgreSqlFencingSchema.TenantId}
                AND l.{PostgreSqlFencingSchema.Kind} = @Kind
                AND l.{PostgreSqlFencingSchema.Resource} = doomed.{PostgreSqlFencingSchema.Resource};
            """;
    }

    #endregion

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct LeaseRow(long Generation, short State, DateTimeOffset ExpiresAt, bool IsLive);
}
#pragma warning restore CA2100
