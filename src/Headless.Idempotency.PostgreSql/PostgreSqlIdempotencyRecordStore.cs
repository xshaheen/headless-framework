// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using Headless.Checks;
using Headless.Constants;
using Headless.UnitOfWork;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace Headless.Idempotency.PostgreSql;

/// <summary>
/// The PostgreSQL idempotency record store. Every verb that can wait on a record row first takes the row's
/// <c>FOR UPDATE</c> lock and only then reads <c>clock_timestamp()</c>, so a retention decision is never made on a
/// clock read from before a lock wait.
/// </summary>
/// <remarks>
/// <para>
/// Lock-or-insert never raises a unique-key violation: the insert is <c>ON CONFLICT DO NOTHING</c>, which waits for a
/// concurrent inserter of the same key and then inserts nothing when that one committed, and the locking read that
/// follows finds whichever row won. A caught <c>23505</c> would not be enough, because any error inside a caller's
/// transaction aborts it (<c>25P02</c>) for every statement after.
/// </para>
/// <para>
/// <c>clock_timestamp()</c> is used, never <c>now()</c>: <c>now()</c> is frozen at transaction start, so inside a long
/// enlisted unit it would keep a record past its retention looking retained.
/// </para>
/// </remarks>
#pragma warning disable CA2100 // SQL text is built from the validated schema name plus internal object and column constants.
internal sealed class PostgreSqlIdempotencyRecordStore : IIdempotencyRecordStore
{
    // A deadlock or serialization failure is the one failure a fresh transaction can clear on its own; the first
    // attempt plus two retries. Only the autonomous purge retries: an enlisted verb's failure has already rolled back
    // the caller's transaction, so only the unit's owner can decide whether to run it again.
    private const int _MaxAttempts = 3;

    // A lost insert race means another transaction committed the row between the insert and the locking read, so the
    // next round's insert does nothing and its locking read finds it. Only a row purged again in that window could
    // send the loop round once more.
    private const int _MaxLockRounds = 5;

    // timestamptz reaches back to 4713 BC, so a purge cutoff further back than this would overflow. No record can
    // have been retained that long ago, so clamping the age deletes exactly the same rows.
    private const int _MaxPurgeAgeDays = 700_000;

    private readonly PostgreSqlIdempotencyOptions _options;
    private readonly IUnitOfWorkFactory _unitOfWorkFactory;
    private readonly string _lockOrInsertSql;
    private readonly string _lockSql;
    private readonly string _admitSql;
    private readonly string _completeSql;
    private readonly string _releaseSql;
    private readonly string _purgeSql;

    public PostgreSqlIdempotencyRecordStore(
        IOptions<PostgreSqlIdempotencyOptions> options,
        IOptions<IdempotencyStorageOptions> storageOptions,
        IUnitOfWorkFactory unitOfWorkFactory
    )
    {
        _options = options.Value;
        _unitOfWorkFactory = unitOfWorkFactory;

        var table = PostgreSqlIdempotencySchema.QualifiedTable(storageOptions.Value.Schema);

        _lockOrInsertSql = _BuildLockOrInsertSql(table);
        _lockSql = _BuildLockSql(table);
        _admitSql = _BuildAdmitSql(table);
        _completeSql = _BuildCompleteSql(table);
        _releaseSql = _BuildReleaseSql(table);
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
                    + $"Headless.Idempotency.PostgreSql is configured for database '{configured.Database}'. An "
                    + "enlisted idempotency call runs in the unit's own transaction, so the unit must run on the "
                    + "database that holds the idempotency records."
            );
        }
    }

    #endregion

    #region Lock

    public async ValueTask<IdempotencyRecordState> LockOrInsertAsync(
        IRelationalUnitOfWorkResource resource,
        IdempotencyRecordKey key,
        IdempotencyFingerprint fingerprint,
        TimeSpan retention,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(resource);
        Argument.IsNotNull(fingerprint);

        // Re-checked here rather than trusted from validation: the caller may have ended the transaction or closed the
        // connection in between, and a statement on either would fail with a less useful message or, worse, run
        // outside the unit.
        var (connection, transaction) = _RequireLive(resource);

        for (var round = 1; round <= _MaxLockRounds; round++)
        {
            await using var command = _CreateCommand(_lockOrInsertSql, connection, transaction, key);
            command.Parameters.Add(_TextParameter("FingerprintAlgorithm", fingerprint.Algorithm));
            command.Parameters.Add(_BytesParameter("Fingerprint", fingerprint.Hash));
            command.Parameters.Add(_IntervalParameter("Retention", retention));

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

            // The insert's RETURNING says whether this call created the row.
            var inserted = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);

            await reader.NextResultAsync(cancellationToken).ConfigureAwait(false);
            var state = await _ReadLockedAsync(reader, inserted, cancellationToken).ConfigureAwait(false);

            if (state is not null)
            {
                return state;
            }
        }

        throw new InvalidOperationException(
            $"The idempotency record '{key.Key}' was deleted between every insert and locking read for "
                + $"{_MaxLockRounds} rounds; the admission gave up instead of looping."
        );
    }

    public async ValueTask<IdempotencyRecordState?> LockAsync(
        IRelationalUnitOfWorkResource resource,
        IdempotencyRecordKey key,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(resource);
        var (connection, transaction) = _RequireLive(resource);

        await using var command = _CreateCommand(_lockSql, connection, transaction, key);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        return await _ReadLockedAsync(reader, inserted: false, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads the locking read's result set and then the clock result set that follows it. The clock is its own
    /// statement so it is read only after the lock is held: a column computed by the locking statement itself can be
    /// evaluated before the lock wait.
    /// </summary>
    private static async Task<IdempotencyRecordState?> _ReadLockedAsync(
        NpgsqlDataReader reader,
        bool inserted,
        CancellationToken cancellationToken
    )
    {
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var status = (IdempotencyRecordStatus)reader.GetInt16(0);
        var fingerprint = new IdempotencyFingerprint(
            reader.GetString(1),
            await reader.GetFieldValueAsync<byte[]>(2, cancellationToken).ConfigureAwait(false)
        );
        long? generation = await reader.IsDBNullAsync(3, cancellationToken).ConfigureAwait(false)
            ? null
            : reader.GetInt64(3);
        IdempotentResult? result = null;

        if (status == IdempotencyRecordStatus.Completed)
        {
            result = new IdempotentResult(
                await reader.GetFieldValueAsync<byte[]>(4, cancellationToken).ConfigureAwait(false),
                reader.GetString(5)
            );
        }

        var retentionUntil = await reader
            .GetFieldValueAsync<DateTimeOffset>(6, cancellationToken)
            .ConfigureAwait(false);

        await reader.NextResultAsync(cancellationToken).ConfigureAwait(false);
        await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        var now = await reader.GetFieldValueAsync<DateTimeOffset>(0, cancellationToken).ConfigureAwait(false);

        return new IdempotencyRecordState(
            inserted,
            status,
            fingerprint,
            generation,
            result,
            retentionUntil,
            IsRetentionElapsed: retentionUntil <= now
        );
    }

    #endregion

    #region Admit, complete, release

    public async ValueTask AdmitAsync(
        IRelationalUnitOfWorkResource resource,
        IdempotencyRecordKey key,
        IdempotencyFingerprint fingerprint,
        long leaseGeneration,
        TimeSpan retention,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(resource);
        Argument.IsNotNull(fingerprint);
        var (connection, transaction) = _RequireLive(resource);

        await using var command = _CreateCommand(_admitSql, connection, transaction, key);
        command.Parameters.Add(_TextParameter("FingerprintAlgorithm", fingerprint.Algorithm));
        command.Parameters.Add(_BytesParameter("Fingerprint", fingerprint.Hash));
        command.Parameters.Add(_GenerationParameter(leaseGeneration));
        command.Parameters.Add(_IntervalParameter("Retention", retention));

        _EnsureWritten(await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false), key, "admit");
    }

    public async ValueTask CompleteAsync(
        IRelationalUnitOfWorkResource resource,
        IdempotencyRecordKey key,
        long leaseGeneration,
        ReadOnlyMemory<byte> result,
        string contract,
        TimeSpan retention,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(resource);
        Argument.IsNotNull(contract);
        var (connection, transaction) = _RequireLive(resource);

        await using var command = _CreateCommand(_completeSql, connection, transaction, key);
        command.Parameters.Add(_GenerationParameter(leaseGeneration));
        command.Parameters.Add(_BytesParameter("Result", result));
        command.Parameters.Add(_TextParameter("ResultContract", contract));
        command.Parameters.Add(_IntervalParameter("Retention", retention));

        _EnsureWritten(await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false), key, "complete");
    }

    public async ValueTask ReleaseAsync(
        IRelationalUnitOfWorkResource resource,
        IdempotencyRecordKey key,
        long leaseGeneration,
        TimeSpan retention,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(resource);
        var (connection, transaction) = _RequireLive(resource);

        await using var command = _CreateCommand(_releaseSql, connection, transaction, key);
        command.Parameters.Add(_GenerationParameter(leaseGeneration));
        command.Parameters.Add(_IntervalParameter("Retention", retention));

        _EnsureWritten(await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false), key, "release");
    }

    private static void _EnsureWritten(int affected, IdempotencyRecordKey key, string verb)
    {
        // The caller locked the record and checked its generation in this transaction, so the row cannot have changed
        // since; reaching here means the table was changed outside this provider or the caller skipped the lock.
        if (affected != 1)
        {
            throw new InvalidOperationException(
                $"Could not {verb} the idempotency record '{key.Key}': it was not found at the expected lease "
                    + "generation inside the transaction that locked it."
            );
        }
    }

    #endregion

    #region Purge

    public async ValueTask<int> PurgeAsync(TimeSpan olderThan, int limit, CancellationToken cancellationToken = default)
    {
        Argument.IsPositiveOrZero(olderThan);
        Argument.IsPositive(limit);

        var age = olderThan > TimeSpan.FromDays(_MaxPurgeAgeDays) ? TimeSpan.FromDays(_MaxPurgeAgeDays) : olderThan;

        for (var attempt = 1; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await using var connection = _options.CreateConnection();
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                // Explicit so the purge runs at READ COMMITTED even when the server's default isolation level is
                // stricter, where a skipped or concurrently changed row would surface as a serialization failure.
                await using var transaction = await connection
                    .BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
                    .ConfigureAwait(false);

                await using var command = new NpgsqlCommand(_purgeSql, connection, transaction)
                {
                    CommandTimeout = _options.CommandTimeoutSeconds,
                };
                command.Parameters.Add(_IntervalParameter("OlderThan", age));
                command.Parameters.Add(new NpgsqlParameter<int>("Limit", NpgsqlDbType.Integer) { TypedValue = limit });

                var deleted = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

                // The delete already happened and the count describes it; a late cancel must not roll it back.
                await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);

                return deleted;
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

    #endregion

    #region Helpers

    private NpgsqlCommand _CreateCommand(
        string sql,
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        IdempotencyRecordKey key
    )
    {
        var command = new NpgsqlCommand(sql, connection, transaction)
        {
            CommandTimeout = _options.CommandTimeoutSeconds,
        };
        command.Parameters.Add(_TextParameter("TenantId", key.TenantId));
        command.Parameters.Add(_TextParameter("Key", key.Key));

        return command;
    }

    private static NpgsqlParameter<string> _TextParameter(string name, string value)
    {
        return new NpgsqlParameter<string>(name, NpgsqlDbType.Varchar) { TypedValue = value };
    }

    private static NpgsqlParameter<ReadOnlyMemory<byte>> _BytesParameter(string name, ReadOnlyMemory<byte> value)
    {
        return new NpgsqlParameter<ReadOnlyMemory<byte>>(name, NpgsqlDbType.Bytea) { TypedValue = value };
    }

    private static NpgsqlParameter<long> _GenerationParameter(long generation)
    {
        return new NpgsqlParameter<long>("LeaseGeneration", NpgsqlDbType.Bigint) { TypedValue = generation };
    }

    private static NpgsqlParameter<TimeSpan> _IntervalParameter(string name, TimeSpan value)
    {
        return new NpgsqlParameter<TimeSpan>(name, NpgsqlDbType.Interval) { TypedValue = value };
    }

    private static (NpgsqlConnection Connection, NpgsqlTransaction Transaction) _RequireLive(
        IRelationalUnitOfWorkResource resource
    )
    {
        if (resource.Transaction is not NpgsqlTransaction transaction)
        {
            throw new InvalidOperationException(
                $"The unit of work carries a '{resource.Transaction.GetType().FullName}' transaction, but "
                    + "Headless.Idempotency.PostgreSql runs enlisted idempotency calls only through an "
                    + "NpgsqlTransaction. Begin the unit on the PostgreSQL database that holds the idempotency records."
            );
        }

        if (resource.Connection is not NpgsqlConnection { State: ConnectionState.Open } connection)
        {
            throw new InvalidOperationException(
                "The unit of work's connection is not an open NpgsqlConnection, so the idempotency call cannot run "
                    + "inside its transaction."
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

    private const string _KeyPredicate = $"""
        {PostgreSqlIdempotencySchema.TenantId} = @TenantId
            AND {PostgreSqlIdempotencySchema.Key} = @Key
        """;

    // Extends, never shortens: a retention already further out than this call's is kept.
    private const string _ExtendedRetention = $"""
        GREATEST({PostgreSqlIdempotencySchema.RetentionUntil}, clock_timestamp() + @Retention)
        """;

    private static string _LockAndClockSql(string table)
    {
        // Two statements: the locking read waits out any other holder, and only then is the clock read.
        return $"""
            SELECT {PostgreSqlIdempotencySchema.Status},
                {PostgreSqlIdempotencySchema.FingerprintAlgorithm},
                {PostgreSqlIdempotencySchema.Fingerprint},
                {PostgreSqlIdempotencySchema.LeaseGeneration},
                {PostgreSqlIdempotencySchema.Result},
                {PostgreSqlIdempotencySchema.ResultContract},
                {PostgreSqlIdempotencySchema.RetentionUntil}
            FROM {table}
            WHERE {_KeyPredicate}
            FOR UPDATE;

            SELECT clock_timestamp();
            """;
    }

    private static string _BuildLockOrInsertSql(string table)
    {
        // The inserted row's retention starts from a clock read before any wait on a concurrent inserter, which can
        // only shorten it by that wait; an admission extends it again from a clock read under the lock.
        return $"""
            INSERT INTO {table} (
                {PostgreSqlIdempotencySchema.TenantId},
                {PostgreSqlIdempotencySchema.Key},
                {PostgreSqlIdempotencySchema.Status},
                {PostgreSqlIdempotencySchema.FingerprintAlgorithm},
                {PostgreSqlIdempotencySchema.Fingerprint},
                {PostgreSqlIdempotencySchema.LeaseGeneration},
                {PostgreSqlIdempotencySchema.Result},
                {PostgreSqlIdempotencySchema.ResultContract},
                {PostgreSqlIdempotencySchema.RetentionUntil}
            )
            VALUES (
                @TenantId,
                @Key,
                {PostgreSqlIdempotencySchema.Pending},
                @FingerprintAlgorithm,
                @Fingerprint,
                NULL,
                NULL,
                NULL,
                clock_timestamp() + @Retention
            )
            ON CONFLICT ({PostgreSqlIdempotencySchema.TenantId}, {PostgreSqlIdempotencySchema.Key}) DO NOTHING
            RETURNING 1;

            {_LockAndClockSql(table)}
            """;
    }

    private static string _BuildLockSql(string table)
    {
        return _LockAndClockSql(table);
    }

    private static string _BuildAdmitSql(string table)
    {
        // Also the in-place reset of a record past its retention: every outcome column is overwritten.
        return $"""
            UPDATE {table}
            SET {PostgreSqlIdempotencySchema.Status} = {PostgreSqlIdempotencySchema.Pending},
                {PostgreSqlIdempotencySchema.FingerprintAlgorithm} = @FingerprintAlgorithm,
                {PostgreSqlIdempotencySchema.Fingerprint} = @Fingerprint,
                {PostgreSqlIdempotencySchema.LeaseGeneration} = @LeaseGeneration,
                {PostgreSqlIdempotencySchema.Result} = NULL,
                {PostgreSqlIdempotencySchema.ResultContract} = NULL,
                {PostgreSqlIdempotencySchema.RetentionUntil} = {_ExtendedRetention}
            WHERE {_KeyPredicate};
            """;
    }

    private static string _BuildCompleteSql(string table)
    {
        return $"""
            UPDATE {table}
            SET {PostgreSqlIdempotencySchema.Status} = {PostgreSqlIdempotencySchema.Completed},
                {PostgreSqlIdempotencySchema.Result} = @Result,
                {PostgreSqlIdempotencySchema.ResultContract} = @ResultContract,
                {PostgreSqlIdempotencySchema.RetentionUntil} = {_ExtendedRetention}
            WHERE {_KeyPredicate}
                AND {PostgreSqlIdempotencySchema.LeaseGeneration} = @LeaseGeneration;
            """;
    }

    private static string _BuildReleaseSql(string table)
    {
        return $"""
            UPDATE {table}
            SET {PostgreSqlIdempotencySchema.Status} = {PostgreSqlIdempotencySchema.Pending},
                {PostgreSqlIdempotencySchema.LeaseGeneration} = NULL,
                {PostgreSqlIdempotencySchema.Result} = NULL,
                {PostgreSqlIdempotencySchema.ResultContract} = NULL,
                {PostgreSqlIdempotencySchema.RetentionUntil} = {_ExtendedRetention}
            WHERE {_KeyPredicate}
                AND {PostgreSqlIdempotencySchema.LeaseGeneration} = @LeaseGeneration;
            """;
    }

    private static string _BuildPurgeSql(string table)
    {
        // SKIP LOCKED leaves a record an admission, fence, or completion holds for a later purge, so the purge never
        // waits on application work and never deletes a row under a transaction that is deciding on it. Since
        // nothing waits, the clock can be read first.
        return $"""
            WITH clock AS MATERIALIZED (SELECT clock_timestamp() AS now),
            doomed AS (
                SELECT r.{PostgreSqlIdempotencySchema.TenantId}, r.{PostgreSqlIdempotencySchema.Key}
                FROM {table} AS r, clock
                WHERE r.{PostgreSqlIdempotencySchema.RetentionUntil} <= clock.now - @OlderThan
                LIMIT @Limit
                FOR UPDATE OF r SKIP LOCKED
            )
            DELETE FROM {table} AS r
            USING doomed
            WHERE r.{PostgreSqlIdempotencySchema.TenantId} = doomed.{PostgreSqlIdempotencySchema.TenantId}
                AND r.{PostgreSqlIdempotencySchema.Key} = doomed.{PostgreSqlIdempotencySchema.Key};
            """;
    }

    #endregion
}
#pragma warning restore CA2100
