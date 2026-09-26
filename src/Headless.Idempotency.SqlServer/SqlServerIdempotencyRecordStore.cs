// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using Headless.Checks;
using Headless.Constants;
using Headless.UnitOfWork;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Headless.Idempotency.SqlServer;

/// <summary>
/// The SQL Server idempotency record store. Every verb that can wait on a record row is one batch that first reads the
/// row with an update-intent lock held to the end of the transaction, and only then captures <c>SYSUTCDATETIME()</c>
/// into the variable every decision in the batch reads, so a retention decision is never made on a clock read from
/// before a lock wait.
/// </summary>
/// <remarks>
/// <para>
/// The lock is <c>UPDLOCK, HOLDLOCK, ROWLOCK</c>. <c>HOLDLOCK</c> keeps it to the end of the transaction and, when the
/// row is absent, locks the key range, so two first admissions of one key serialize instead of racing to insert; the
/// insert that follows can therefore never collide. Update locks are taken the same way under read committed snapshot
/// isolation, so no RCSI hint is needed.
/// </para>
/// <para>
/// No batch uses <c>TRY/CATCH</c> or <c>SET</c> options. An enlisted batch runs on the caller's session: a caught
/// duplicate-key error would still doom a caller transaction running with <c>XACT_ABORT ON</c>, and a <c>SET</c> would
/// outlive the batch for the rest of that session. Row counts are selected with <c>@@ROWCOUNT</c> rather than read from
/// the command, because the caller's session may run with <c>NOCOUNT ON</c>.
/// </para>
/// </remarks>
#pragma warning disable CA2100 // SQL text is built from the validated schema name plus internal object and column constants.
internal sealed class SqlServerIdempotencyRecordStore : IIdempotencyRecordStore
{
    // A deadlock or snapshot update conflict is the one failure a fresh transaction can clear on its own; the first
    // attempt plus two retries. Only the autonomous purge retries: an enlisted verb's failure has already rolled back
    // the caller's transaction, so only the unit's owner can decide whether to run it again.
    private const int _MaxAttempts = 3;

    // datetimeoffset reaches back to year 1, so a purge cutoff further back than this would overflow DATEADD. No
    // record can have been retained that long ago, so clamping the age deletes exactly the same rows.
    private const int _MaxPurgeAgeDays = 700_000;

    private const string _ReadCommittedSnapshotProbeSql =
        "SELECT is_read_committed_snapshot_on FROM sys.databases WHERE database_id = DB_ID();";

    private readonly SqlServerIdempotencyOptions _options;
    private readonly IUnitOfWorkFactory _unitOfWorkFactory;
    private readonly string _table;
    private readonly string _lockOrInsertSql;
    private readonly string _lockSql;
    private readonly string _admitSql;
    private readonly string _completeSql;
    private readonly string _releaseSql;

    // Built once per store, after probing the record database's snapshot setting; null until the first purge.
    private string? _purgeSql;

    public SqlServerIdempotencyRecordStore(
        IOptions<SqlServerIdempotencyOptions> options,
        IOptions<IdempotencyStorageOptions> storageOptions,
        IUnitOfWorkFactory unitOfWorkFactory
    )
    {
        _options = options.Value;
        _unitOfWorkFactory = unitOfWorkFactory;
        _table = SqlServerIdempotencySchema.QualifiedTable(storageOptions.Value.Schema);

        _lockOrInsertSql = _BuildLockSql(_table, insertWhenAbsent: true);
        _lockSql = _BuildLockSql(_table, insertWhenAbsent: false);
        _admitSql = _BuildAdmitSql(_table);
        _completeSql = _BuildCompleteSql(_table);
        _releaseSql = _BuildReleaseSql(_table);
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
                $"The unit of work's connection targets database '{connection.Database}' on '{connection.DataSource}', "
                    + $"but Headless.Idempotency.SqlServer is configured for database '{configured.Database}' on "
                    + $"'{configured.DataSource}'. An enlisted idempotency call runs in the unit's own transaction, so "
                    + "the unit must run on the database that holds the idempotency records."
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

        await using var command = _CreateCommand(_lockOrInsertSql, connection, transaction, key);
        command.Parameters.Add(_AlgorithmParameter(fingerprint.Algorithm));
        command.Parameters.Add(
            new SqlParameter("Fingerprint", SqlDbType.VarBinary, IdempotencyFieldLimits.FingerprintMaxLength)
            {
                Value = fingerprint.Hash.ToArray(),
            }
        );
        _AddSpanParameters(command, retention);

        return await _ReadLockedAsync(command, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"The idempotency record '{key.Key}' was neither found nor inserted under its key-range lock."
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

        return await _ReadLockedAsync(command, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<IdempotencyRecordState?> _ReadLockedAsync(
        SqlCommand command,
        CancellationToken cancellationToken
    )
    {
        await using var reader = await _ExecuteReaderAsync(command, cancellationToken).ConfigureAwait(false);
        await reader.ReadAsync(cancellationToken).ConfigureAwait(false);

        // Column 0 is null when the row is absent and was not inserted.
        if (await reader.IsDBNullAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var inserted = reader.GetBoolean(0);
        var status = (IdempotencyRecordStatus)reader.GetInt16(1);
        var fingerprint = new IdempotencyFingerprint(
            reader.GetString(2),
            await reader.GetFieldValueAsync<byte[]>(3, cancellationToken).ConfigureAwait(false)
        );
        long? generation = await reader.IsDBNullAsync(4, cancellationToken).ConfigureAwait(false)
            ? null
            : reader.GetInt64(4);
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

        return new IdempotencyRecordState(
            inserted,
            status,
            fingerprint,
            generation,
            result,
            retentionUntil,
            IsRetentionElapsed: reader.GetBoolean(8)
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
        command.Parameters.Add(_AlgorithmParameter(fingerprint.Algorithm));
        command.Parameters.Add(
            new SqlParameter("Fingerprint", SqlDbType.VarBinary, IdempotencyFieldLimits.FingerprintMaxLength)
            {
                Value = fingerprint.Hash.ToArray(),
            }
        );
        command.Parameters.Add(_GenerationParameter(leaseGeneration));
        _AddSpanParameters(command, retention);

        await _WriteAsync(command, key, "admit", cancellationToken).ConfigureAwait(false);
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
        command.Parameters.Add(new SqlParameter("Result", SqlDbType.VarBinary, -1) { Value = result.ToArray() });
        command.Parameters.Add(
            new SqlParameter("ResultContract", SqlDbType.NVarChar, IdempotencyFieldLimits.ContractMaxLength)
            {
                Value = contract,
            }
        );
        _AddSpanParameters(command, retention);

        await _WriteAsync(command, key, "complete", cancellationToken).ConfigureAwait(false);
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
        _AddSpanParameters(command, retention);

        await _WriteAsync(command, key, "release", cancellationToken).ConfigureAwait(false);
    }

    private static async Task _WriteAsync(
        SqlCommand command,
        IdempotencyRecordKey key,
        string verb,
        CancellationToken cancellationToken
    )
    {
        await using var reader = await _ExecuteReaderAsync(command, cancellationToken).ConfigureAwait(false);
        await reader.ReadAsync(cancellationToken).ConfigureAwait(false);

        // The caller locked the record and checked its generation in this transaction, so the row cannot have changed
        // since; reaching here means the table was changed outside this provider or the caller skipped the lock.
        if (reader.GetInt32(0) != 1)
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
                // Explicit so the purge runs at READ COMMITTED whatever isolation level a pooled session last used:
                // READPAST is refused above it.
                await using var transaction = (SqlTransaction)
                    await connection
                        .BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
                        .ConfigureAwait(false);

                var sql = await _GetPurgeSqlAsync(connection, transaction, cancellationToken).ConfigureAwait(false);

                await using var command = new SqlCommand(sql, connection, transaction)
                {
                    CommandTimeout = _options.CommandTimeoutSeconds,
                };
                command.Parameters.Add(new SqlParameter("Limit", SqlDbType.Int) { Value = limit });
                _AddSpanParameters(command, age);

                int deleted;

                await using (var reader = await _ExecuteReaderAsync(command, cancellationToken).ConfigureAwait(false))
                {
                    await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                    deleted = reader.GetInt32(0);
                }

                // The delete already happened and the count describes it; a late cancel must not roll it back.
                await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);

                return deleted;
            }
            catch (SqlException ex) when (_IsRetryable(ex) && attempt < _MaxAttempts)
            {
                // The victim's transaction is already rolled back and disposed above; the next attempt starts clean.
            }
        }
    }

    private static bool _IsRetryable(SqlException ex)
    {
        return ex.Number is SqlErrorCodes.SqlServer.DeadlockVictim or SqlErrorCodes.SqlServer.SnapshotUpdateConflict;
    }

    /// <summary>
    /// Returns the purge statement for the record database. <c>READPAST</c> is refused at READ COMMITTED when the
    /// database runs read committed snapshot isolation unless the read also takes locks, so the statement adds
    /// <c>READCOMMITTEDLOCK</c> there.
    /// </summary>
    private async Task<string> _GetPurgeSqlAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        CancellationToken cancellationToken
    )
    {
        if (_purgeSql is { } cached)
        {
            return cached;
        }

        await using var command = new SqlCommand(_ReadCommittedSnapshotProbeSql, connection, transaction)
        {
            CommandTimeout = _options.CommandTimeoutSeconds,
        };
        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);

        // Two first purges may both probe; they build the same statement.
        return _purgeSql = _BuildPurgeSql(_table, GetReadPastHints(result is true));
    }

    internal static string GetReadPastHints(bool readCommittedSnapshotEnabled)
    {
        return readCommittedSnapshotEnabled
            ? "UPDLOCK, READPAST, ROWLOCK, READCOMMITTEDLOCK"
            : "UPDLOCK, READPAST, ROWLOCK";
    }

    #endregion

    #region Helpers

    private static async Task<SqlDataReader> _ExecuteReaderAsync(
        SqlCommand command,
        CancellationToken cancellationToken
    )
    {
        try
        {
            return await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        }
        // SqlClient reports a command it cancelled mid-flight as a SqlException ("Operation cancelled by user");
        // surface it as the cancellation the caller asked for.
        catch (SqlException ex) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(ex.Message, ex, cancellationToken);
        }
    }

    private SqlCommand _CreateCommand(
        string sql,
        SqlConnection connection,
        SqlTransaction transaction,
        IdempotencyRecordKey key
    )
    {
        var command = new SqlCommand(sql, connection, transaction) { CommandTimeout = _options.CommandTimeoutSeconds };
        command.Parameters.Add(
            new SqlParameter("TenantId", SqlDbType.NVarChar, IdempotencyFieldLimits.TenantIdMaxLength)
            {
                Value = key.TenantId,
            }
        );
        command.Parameters.Add(
            new SqlParameter("IdempotencyKey", SqlDbType.NVarChar, IdempotencyFieldLimits.KeyMaxLength)
            {
                Value = key.Key,
            }
        );

        return command;
    }

    private static SqlParameter _AlgorithmParameter(string algorithm)
    {
        return new SqlParameter(
            "FingerprintAlgorithm",
            SqlDbType.NVarChar,
            IdempotencyFieldLimits.FingerprintAlgorithmMaxLength
        )
        {
            Value = algorithm,
        };
    }

    private static SqlParameter _GenerationParameter(long generation)
    {
        return new SqlParameter("LeaseGeneration", SqlDbType.BigInt) { Value = generation };
    }

    /// <summary>
    /// Adds a span as whole days, whole seconds within the day, and the nanosecond remainder, the parts
    /// <see cref="_DeadlineSql" /> adds back. A single <c>DATEADD</c> takes an <see langword="int" />, which overflows in
    /// nanoseconds after about two seconds and in seconds after about 68 years.
    /// </summary>
    private static void _AddSpanParameters(SqlCommand command, TimeSpan span)
    {
        var days = checked((int)(span.Ticks / TimeSpan.TicksPerDay));
        var ticksWithinDay = span.Ticks % TimeSpan.TicksPerDay;
        var wholeSeconds = checked((int)(ticksWithinDay / TimeSpan.TicksPerSecond));
        var nanoseconds = checked((int)(ticksWithinDay % TimeSpan.TicksPerSecond * 100));

        command.Parameters.Add(new SqlParameter("SpanDays", SqlDbType.Int) { Value = days });
        command.Parameters.Add(new SqlParameter("SpanSeconds", SqlDbType.Int) { Value = wholeSeconds });
        command.Parameters.Add(new SqlParameter("SpanNanoseconds", SqlDbType.Int) { Value = nanoseconds });
    }

    private static (SqlConnection Connection, SqlTransaction Transaction) _RequireLive(
        IRelationalUnitOfWorkResource resource
    )
    {
        if (resource.Transaction is not SqlTransaction transaction)
        {
            throw new InvalidOperationException(
                $"The unit of work carries a '{resource.Transaction.GetType().FullName}' transaction, but "
                    + "Headless.Idempotency.SqlServer runs enlisted idempotency calls only through a SqlTransaction. "
                    + "Begin the unit on the SQL Server database that holds the idempotency records."
            );
        }

        if (resource.Connection is not SqlConnection { State: ConnectionState.Open } connection)
        {
            throw new InvalidOperationException(
                "The unit of work's connection is not an open SqlConnection, so the idempotency call cannot run "
                    + "inside its transaction."
            );
        }

        // A committed or rolled-back SqlTransaction drops its connection, so this also refuses a finished transaction.
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

    // Batch variables never share a name with a parameter, compared case-insensitively: T-SQL would reject the
    // redeclaration of a parameter name.
    private const string _KeyPredicate = $"""
        {SqlServerIdempotencySchema.TenantId} = @TenantId
                AND {SqlServerIdempotencySchema.Key} = @IdempotencyKey
        """;

    /// <summary>A deadline <paramref name="start" /> plus the span parameters, one DATEADD per unit.</summary>
    private static string _DeadlineSql(string start)
    {
        return "DATEADD(nanosecond, @SpanNanoseconds, "
            + "DATEADD(second, @SpanSeconds, "
            + $"DATEADD(day, @SpanDays, {start})))";
    }

    private static string _BuildLockSql(string table, bool insertWhenAbsent)
    {
        // The locking read waits out any other holder, and only then is the clock read, so the retention decision
        // sees a clock from after the wait. An absent row leaves every variable NULL but @rowFound, and HOLDLOCK keeps
        // its key range locked, so the insert below cannot collide with a concurrent one.
        var insert = insertWhenAbsent
            ? $"""
                IF @rowFound = 0
                BEGIN
                    SET @rowInserted = 1;
                    SET @rowStatus = {SqlServerIdempotencySchema.Pending};
                    SET @rowAlgorithm = @FingerprintAlgorithm;
                    SET @rowFingerprint = @Fingerprint;
                    SET @rowRetentionUntil = {_DeadlineSql("@now")};

                    INSERT INTO {table} (
                        {SqlServerIdempotencySchema.TenantId},
                        {SqlServerIdempotencySchema.Key},
                        {SqlServerIdempotencySchema.Status},
                        {SqlServerIdempotencySchema.FingerprintAlgorithm},
                        {SqlServerIdempotencySchema.Fingerprint},
                        {SqlServerIdempotencySchema.LeaseGeneration},
                        {SqlServerIdempotencySchema.Result},
                        {SqlServerIdempotencySchema.ResultContract},
                        {SqlServerIdempotencySchema.RetentionUntil}
                    )
                    VALUES (
                        @TenantId,
                        @IdempotencyKey,
                        @rowStatus,
                        @rowAlgorithm,
                        @rowFingerprint,
                        NULL,
                        NULL,
                        NULL,
                        @rowRetentionUntil
                    );
                END;
                """
            : string.Empty;

        return $"""
            DECLARE @rowFound bit = 0, @rowInserted bit = 0, @rowStatus smallint,
                @rowAlgorithm nvarchar({IdempotencyFieldLimits.FingerprintAlgorithmMaxLength}),
                @rowFingerprint varbinary({IdempotencyFieldLimits.FingerprintMaxLength}), @rowGeneration bigint,
                @rowResult varbinary(max), @rowContract nvarchar({IdempotencyFieldLimits.ContractMaxLength}),
                @rowRetentionUntil datetimeoffset(7), @now datetimeoffset(7);

            SELECT @rowFound = 1,
                @rowStatus = {SqlServerIdempotencySchema.Status},
                @rowAlgorithm = {SqlServerIdempotencySchema.FingerprintAlgorithm},
                @rowFingerprint = {SqlServerIdempotencySchema.Fingerprint},
                @rowGeneration = {SqlServerIdempotencySchema.LeaseGeneration},
                @rowResult = {SqlServerIdempotencySchema.Result},
                @rowContract = {SqlServerIdempotencySchema.ResultContract},
                @rowRetentionUntil = {SqlServerIdempotencySchema.RetentionUntil}
            FROM {table} WITH (UPDLOCK, HOLDLOCK, ROWLOCK)
            WHERE {_KeyPredicate};

            SET @now = TODATETIMEOFFSET(SYSUTCDATETIME(), 0);

            {insert}

            SELECT CASE WHEN @rowFound = 1 OR @rowInserted = 1 THEN @rowInserted END,
                @rowStatus,
                @rowAlgorithm,
                @rowFingerprint,
                @rowGeneration,
                @rowResult,
                @rowContract,
                @rowRetentionUntil,
                CAST(CASE WHEN @rowRetentionUntil <= @now THEN 1 ELSE 0 END AS bit);
            """;
    }

    private static string _ExtendRetentionSql()
    {
        // Extends, never shortens: a retention already further out than this call's is kept. The record is already
        // locked by this transaction, so reading the clock before the update waits on nothing.
        return $"""
            DECLARE @now datetimeoffset(7), @extendTo datetimeoffset(7);
            SET @now = TODATETIMEOFFSET(SYSUTCDATETIME(), 0);
            SET @extendTo = {_DeadlineSql("@now")};
            """;
    }

    private const string _ExtendedRetention = $"""
        CASE WHEN {SqlServerIdempotencySchema.RetentionUntil} > @extendTo THEN {SqlServerIdempotencySchema.RetentionUntil} ELSE @extendTo END
        """;

    private static string _BuildAdmitSql(string table)
    {
        // Also the in-place reset of a record past its retention: every outcome column is overwritten.
        return $"""
            {_ExtendRetentionSql()}

            UPDATE {table}
            SET {SqlServerIdempotencySchema.Status} = {SqlServerIdempotencySchema.Pending},
                {SqlServerIdempotencySchema.FingerprintAlgorithm} = @FingerprintAlgorithm,
                {SqlServerIdempotencySchema.Fingerprint} = @Fingerprint,
                {SqlServerIdempotencySchema.LeaseGeneration} = @LeaseGeneration,
                {SqlServerIdempotencySchema.Result} = NULL,
                {SqlServerIdempotencySchema.ResultContract} = NULL,
                {SqlServerIdempotencySchema.RetentionUntil} = {_ExtendedRetention}
            WHERE {_KeyPredicate};

            SELECT @@ROWCOUNT;
            """;
    }

    private static string _BuildCompleteSql(string table)
    {
        return $"""
            {_ExtendRetentionSql()}

            UPDATE {table}
            SET {SqlServerIdempotencySchema.Status} = {SqlServerIdempotencySchema.Completed},
                {SqlServerIdempotencySchema.Result} = @Result,
                {SqlServerIdempotencySchema.ResultContract} = @ResultContract,
                {SqlServerIdempotencySchema.RetentionUntil} = {_ExtendedRetention}
            WHERE {_KeyPredicate}
                AND {SqlServerIdempotencySchema.LeaseGeneration} = @LeaseGeneration;

            SELECT @@ROWCOUNT;
            """;
    }

    private static string _BuildReleaseSql(string table)
    {
        return $"""
            {_ExtendRetentionSql()}

            UPDATE {table}
            SET {SqlServerIdempotencySchema.Status} = {SqlServerIdempotencySchema.Pending},
                {SqlServerIdempotencySchema.LeaseGeneration} = NULL,
                {SqlServerIdempotencySchema.Result} = NULL,
                {SqlServerIdempotencySchema.ResultContract} = NULL,
                {SqlServerIdempotencySchema.RetentionUntil} = {_ExtendedRetention}
            WHERE {_KeyPredicate}
                AND {SqlServerIdempotencySchema.LeaseGeneration} = @LeaseGeneration;

            SELECT @@ROWCOUNT;
            """;
    }

    private static string _BuildPurgeSql(string table, string hints)
    {
        // READPAST leaves a record an admission, fence, or completion holds for a later purge, so the purge never
        // waits on application work and never deletes a row under a transaction that is deciding on it. Since nothing
        // waits, the clock can be read first.
        return $"""
            DECLARE @cutoff datetimeoffset(7) = DATEADD(nanosecond, -@SpanNanoseconds,
                DATEADD(second, -@SpanSeconds, DATEADD(day, -@SpanDays, TODATETIMEOFFSET(SYSUTCDATETIME(), 0))));

            WITH doomed AS (
                SELECT TOP (@Limit) {SqlServerIdempotencySchema.TenantId}
                FROM {table} WITH ({hints})
                WHERE {SqlServerIdempotencySchema.RetentionUntil} <= @cutoff
            )
            DELETE FROM doomed;

            SELECT @@ROWCOUNT;
            """;
    }

    #endregion
}
#pragma warning restore CA2100
