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
/// into the variable every decision in the batch reads, so a retention or lease decision is never made on a clock read
/// from before a lock wait.
/// </summary>
/// <remarks>
/// <para>
/// The lock is <c>UPDLOCK, HOLDLOCK, ROWLOCK</c>. <c>UPDLOCK</c> rather than a shared lock: a fence holds it until the
/// caller's transaction ends, and an admission, renewal, or completion waiting on it then sees the committed outcome,
/// where a waiting admission queued for the update lock behind a shared one would deadlock against the fencing
/// transaction's own completion. <c>HOLDLOCK</c> keeps it to the end of the transaction and, when the row is absent,
/// locks the key range, so two first admissions of one key serialize instead of racing to insert; the insert that
/// follows can therefore never collide. Update locks are taken the same way under read committed snapshot isolation,
/// so no RCSI hint is needed.
/// </para>
/// <para>
/// No batch uses <c>TRY/CATCH</c> or <c>SET</c> options. An enlisted batch runs on the caller's session: a caught
/// duplicate-key error would still doom a caller transaction running with <c>XACT_ABORT ON</c>, and a <c>SET</c> would
/// outlive the batch for the rest of that session. Row counts are selected with <c>@@ROWCOUNT</c> rather than read from
/// the command, because the caller's session may run with <c>NOCOUNT ON</c>.
/// </para>
/// <para>
/// A generation is drawn with <c>NEXT VALUE FOR</c> into a variable, after the row lock is held. <c>NEXT VALUE FOR</c>
/// is illegal inside <c>CASE</c>, <c>OUTPUT</c>, <c>WHERE</c>, subqueries, and <c>MERGE</c>, and a generation drawn
/// before the lock could be lower than one a still-open admission already holds.
/// </para>
/// </remarks>
#pragma warning disable CA2100 // SQL text is built from the validated schema name plus internal object and column constants.
internal sealed class SqlServerIdempotencyRecordStore : IIdempotencyRecordStore
{
    // A deadlock or snapshot update conflict is the one failure a fresh transaction can clear on its own; the first
    // attempt plus two retries. Only the autonomous renewal and purge retry: an enlisted verb's failure has already
    // rolled back the caller's transaction, so only the unit's owner can decide whether to run it again.
    private const int _MaxAttempts = 3;

    // datetimeoffset reaches back to year 1, so a purge cutoff further back than this would overflow DATEADD. No
    // record can have been retained that long ago, so clamping the age deletes exactly the same rows.
    private const int _MaxPurgeAgeDays = 700_000;

    // Span parameter prefixes: an admission adds both a lease and a retention to the same clock reading.
    private const string _RetentionSpan = "Span";
    private const string _LeaseSpan = "Lease";

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
    private readonly string _renewSql;
    private readonly string _peekSql;

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

        var schema = storageOptions.Value.Schema;
        _table = SqlServerIdempotencySchema.QualifiedTable(schema);
        var sequence = SqlServerIdempotencySchema.QualifiedSequence(schema);

        _lockOrInsertSql = _BuildLockSql(_table, insertWhenAbsent: true);
        _lockSql = _BuildLockSql(_table, insertWhenAbsent: false);
        _admitSql = _BuildAdmitSql(_table, sequence);
        _completeSql = _BuildCompleteSql(_table);
        _releaseSql = _BuildReleaseSql(_table);
        _renewSql = _BuildRenewSql(_table);
        _peekSql = _BuildPeekSql(_table);
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
        _AddFingerprintParameters(command, fingerprint);
        _AddSpanParameters(command, _RetentionSpan, retention);

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
        DateTimeOffset? leaseExpiresAt = await reader.IsDBNullAsync(5, cancellationToken).ConfigureAwait(false)
            ? null
            : await reader.GetFieldValueAsync<DateTimeOffset>(5, cancellationToken).ConfigureAwait(false);
        IdempotentResult? result = null;

        if (status == IdempotencyRecordStatus.Completed)
        {
            result = new IdempotentResult(
                await reader.GetFieldValueAsync<byte[]>(6, cancellationToken).ConfigureAwait(false),
                reader.GetString(7)
            );
        }

        var retentionUntil = await reader
            .GetFieldValueAsync<DateTimeOffset>(8, cancellationToken)
            .ConfigureAwait(false);

        return new IdempotencyRecordState(
            inserted,
            status,
            fingerprint,
            generation,
            leaseExpiresAt,
            result,
            retentionUntil,
            IsRetentionElapsed: reader.GetBoolean(9),
            IsLeaseLive: reader.GetBoolean(10)
        );
    }

    #endregion

    #region Admit, complete, release

    public async ValueTask<IdempotencyRecordGrant> AdmitAsync(
        IRelationalUnitOfWorkResource resource,
        IdempotencyRecordKey key,
        IdempotencyFingerprint fingerprint,
        TimeSpan leaseDuration,
        TimeSpan retention,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(resource);
        Argument.IsNotNull(fingerprint);
        var (connection, transaction) = _RequireLive(resource);

        await using var command = _CreateCommand(_admitSql, connection, transaction, key);
        _AddFingerprintParameters(command, fingerprint);
        _AddSpanParameters(command, _LeaseSpan, leaseDuration);
        _AddSpanParameters(command, _RetentionSpan, retention);

        await using var reader = await _ExecuteReaderAsync(command, cancellationToken).ConfigureAwait(false);
        await reader.ReadAsync(cancellationToken).ConfigureAwait(false);

        if (reader.GetInt32(0) != 1)
        {
            throw _NotWritten(key, "admit");
        }

        return new IdempotencyRecordGrant(
            reader.GetInt64(1),
            await reader.GetFieldValueAsync<DateTimeOffset>(2, cancellationToken).ConfigureAwait(false)
        );
    }

    public async ValueTask CompleteAsync(
        IRelationalUnitOfWorkResource resource,
        IdempotencyRecordKey key,
        long generation,
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
        command.Parameters.Add(_GenerationParameter(generation));
        command.Parameters.Add(new SqlParameter("Result", SqlDbType.VarBinary, -1) { Value = result.ToArray() });
        command.Parameters.Add(
            new SqlParameter("ResultContract", SqlDbType.NVarChar, IdempotencyFieldLimits.ContractMaxLength)
            {
                Value = contract,
            }
        );
        _AddSpanParameters(command, _RetentionSpan, retention);

        await _WriteAsync(command, key, "complete", cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask ReleaseAsync(
        IRelationalUnitOfWorkResource resource,
        IdempotencyRecordKey key,
        long generation,
        TimeSpan retention,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(resource);
        var (connection, transaction) = _RequireLive(resource);

        await using var command = _CreateCommand(_releaseSql, connection, transaction, key);
        command.Parameters.Add(_GenerationParameter(generation));
        _AddSpanParameters(command, _RetentionSpan, retention);

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

        if (reader.GetInt32(0) != 1)
        {
            throw _NotWritten(key, verb);
        }
    }

    private static InvalidOperationException _NotWritten(IdempotencyRecordKey key, string verb)
    {
        // The caller locked the record and checked its generation in this transaction, so the row cannot have changed
        // since; reaching here means the table was changed outside this provider or the caller skipped the lock.
        return new InvalidOperationException(
            $"Could not {verb} the idempotency record '{key.Key}': it was not found at the expected generation "
                + "inside the transaction that locked it."
        );
    }

    #endregion

    #region Renew

    public ValueTask<IdempotentLeaseRenewal> RenewAsync(
        IdempotencyRecordKey key,
        long generation,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default
    )
    {
        return _RunAutonomousAsync(
            async (connection, transaction, ct) =>
            {
                await using var command = _CreateCommand(_renewSql, connection, transaction, key);
                command.Parameters.Add(_GenerationParameter(generation));
                _AddSpanParameters(command, _LeaseSpan, leaseDuration);

                await using var reader = await _ExecuteReaderAsync(command, ct).ConfigureAwait(false);
                await reader.ReadAsync(ct).ConfigureAwait(false);

                // Column 0 is null when the row is absent.
                if (await reader.IsDBNullAsync(0, ct).ConfigureAwait(false))
                {
                    return new IdempotentLeaseRenewal(IdempotentLeaseStatus.Stale, ExpiresAt: null);
                }

                if (!await reader.IsDBNullAsync(4, ct).ConfigureAwait(false))
                {
                    var renewedUntil = await reader.GetFieldValueAsync<DateTimeOffset>(4, ct).ConfigureAwait(false);

                    return new IdempotentLeaseRenewal(IdempotentLeaseStatus.Current, renewedUntil);
                }

                var status = (IdempotencyRecordStatus)reader.GetInt16(0);
                long? recordGeneration = await reader.IsDBNullAsync(1, ct).ConfigureAwait(false)
                    ? null
                    : reader.GetInt64(1);
                DateTimeOffset? expiresAt = await reader.IsDBNullAsync(2, ct).ConfigureAwait(false)
                    ? null
                    : await reader.GetFieldValueAsync<DateTimeOffset>(2, ct).ConfigureAwait(false);
                var isLive = reader.GetBoolean(3);

                return IdempotencyLeaseClassifier.Classify(status, recordGeneration, isLive, generation) switch
                {
                    IdempotentLeaseStatus.Expired => new IdempotentLeaseRenewal(
                        IdempotentLeaseStatus.Expired,
                        expiresAt
                    ),
                    IdempotentLeaseStatus.Current => throw new InvalidOperationException(
                        $"The idempotency record '{key.Key}' held a live lease at the renewing generation, yet the "
                            + "renewal changed nothing."
                    ),
                    var refused => new IdempotentLeaseRenewal(refused, ExpiresAt: null),
                };
            },
            cancellationToken
        );
    }

    #endregion

    #region Peek

    public async ValueTask<IdempotencyPeekStatus> PeekAsync(
        IdempotencyRecordKey key,
        CancellationToken cancellationToken = default
    )
    {
        await using var connection = _options.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        // Explicit so the read runs at READ COMMITTED whatever isolation level a pooled session last used. No
        // locking hint is added: under RCSI the row is read from its version store and never waits; without RCSI a
        // plain read takes only a shared lock for the statement's own duration, which the winner's update-intent
        // lock does not block, and only waits out the winner's exclusive lock while its write is still uncommitted.
        await using var transaction = (SqlTransaction)
            await connection
                .BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
                .ConfigureAwait(false);

        await using var command = _CreateCommand(_peekSql, connection, transaction, key);

        bool found;
        IdempotencyRecordStatus status = default;
        DateTimeOffset retentionUntil = default;
        DateTimeOffset now = default;

        await using (var reader = await _ExecuteReaderAsync(command, cancellationToken).ConfigureAwait(false))
        {
            found = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);

            if (found)
            {
                status = (IdempotencyRecordStatus)reader.GetInt16(0);
                retentionUntil = await reader
                    .GetFieldValueAsync<DateTimeOffset>(1, cancellationToken)
                    .ConfigureAwait(false);
                now = await reader.GetFieldValueAsync<DateTimeOffset>(2, cancellationToken).ConfigureAwait(false);
            }
        }

        // The read already happened and nothing was written; a late cancel must not roll back a read.
        await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);

        if (!found || retentionUntil <= now)
        {
            return IdempotencyPeekStatus.Absent;
        }

        return status == IdempotencyRecordStatus.Completed
            ? IdempotencyPeekStatus.Completed
            : IdempotencyPeekStatus.Pending;
    }

    #endregion

    #region Purge

    public ValueTask<int> PurgeAsync(TimeSpan olderThan, int limit, CancellationToken cancellationToken = default)
    {
        Argument.IsPositiveOrZero(olderThan);
        Argument.IsPositive(limit);

        var age = olderThan > TimeSpan.FromDays(_MaxPurgeAgeDays) ? TimeSpan.FromDays(_MaxPurgeAgeDays) : olderThan;

        return _RunAutonomousAsync(
            async (connection, transaction, ct) =>
            {
                var sql = await _GetPurgeSqlAsync(connection, transaction, ct).ConfigureAwait(false);

                await using var command = new SqlCommand(sql, connection, transaction)
                {
                    CommandTimeout = _options.CommandTimeoutSeconds,
                };
                command.Parameters.Add(new SqlParameter("Limit", SqlDbType.Int) { Value = limit });
                _AddSpanParameters(command, _RetentionSpan, age);

                await using var reader = await _ExecuteReaderAsync(command, ct).ConfigureAwait(false);
                await reader.ReadAsync(ct).ConfigureAwait(false);

                return reader.GetInt32(0);
            },
            cancellationToken
        );
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

    /// <summary>
    /// Runs <paramref name="work" /> in a fresh READ COMMITTED transaction on the provider's own connection and commits
    /// it, retrying a deadlock or snapshot update conflict in a new transaction.
    /// </summary>
    private async ValueTask<T> _RunAutonomousAsync<T>(
        Func<SqlConnection, SqlTransaction, CancellationToken, Task<T>> work,
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
                // Explicit so the call runs at READ COMMITTED whatever isolation level a pooled session last used:
                // READPAST is refused above it.
                await using var transaction = (SqlTransaction)
                    await connection
                        .BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
                        .ConfigureAwait(false);

                var result = await work(connection, transaction, cancellationToken).ConfigureAwait(false);

                // The write already happened and the result describes it; a late cancel must not roll it back.
                await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);

                return result;
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

    private static void _AddFingerprintParameters(SqlCommand command, IdempotencyFingerprint fingerprint)
    {
        command.Parameters.Add(
            new SqlParameter(
                "FingerprintAlgorithm",
                SqlDbType.NVarChar,
                IdempotencyFieldLimits.FingerprintAlgorithmMaxLength
            )
            {
                Value = fingerprint.Algorithm,
            }
        );
        command.Parameters.Add(
            new SqlParameter("Fingerprint", SqlDbType.VarBinary, IdempotencyFieldLimits.FingerprintMaxLength)
            {
                Value = fingerprint.Hash.ToArray(),
            }
        );
    }

    private static SqlParameter _GenerationParameter(long generation)
    {
        return new SqlParameter("Generation", SqlDbType.BigInt) { Value = generation };
    }

    /// <summary>
    /// Adds a span as whole days, whole seconds within the day, and the nanosecond remainder, the parts
    /// <see cref="_DeadlineSql" /> adds back. A single <c>DATEADD</c> takes an <see langword="int" />, which overflows in
    /// nanoseconds after about two seconds and in seconds after about 68 years.
    /// </summary>
    private static void _AddSpanParameters(SqlCommand command, string prefix, TimeSpan span)
    {
        var days = checked((int)(span.Ticks / TimeSpan.TicksPerDay));
        var ticksWithinDay = span.Ticks % TimeSpan.TicksPerDay;
        var wholeSeconds = checked((int)(ticksWithinDay / TimeSpan.TicksPerSecond));
        var nanoseconds = checked((int)(ticksWithinDay % TimeSpan.TicksPerSecond * 100));

        command.Parameters.Add(new SqlParameter($"{prefix}Days", SqlDbType.Int) { Value = days });
        command.Parameters.Add(new SqlParameter($"{prefix}Seconds", SqlDbType.Int) { Value = wholeSeconds });
        command.Parameters.Add(new SqlParameter($"{prefix}Nanoseconds", SqlDbType.Int) { Value = nanoseconds });
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

    /// <summary>A deadline <paramref name="start" /> plus the span parameters with <paramref name="prefix" />, one DATEADD per unit.</summary>
    private static string _DeadlineSql(string start, string prefix)
    {
        return $"DATEADD(nanosecond, @{prefix}Nanoseconds, "
            + $"DATEADD(second, @{prefix}Seconds, "
            + $"DATEADD(day, @{prefix}Days, {start})))";
    }

    private static string _BuildLockSql(string table, bool insertWhenAbsent)
    {
        // The locking read waits out any other holder, and only then is the clock read, so the retention and lease
        // decisions see a clock from after the wait. An absent row leaves every variable NULL but @rowFound, and
        // HOLDLOCK keeps its key range locked, so the insert below cannot collide with a concurrent one.
        var insert = insertWhenAbsent
            ? $"""
                IF @rowFound = 0
                BEGIN
                    SET @rowInserted = 1;
                    SET @rowStatus = {SqlServerIdempotencySchema.Pending};
                    SET @rowAlgorithm = @FingerprintAlgorithm;
                    SET @rowFingerprint = @Fingerprint;
                    SET @rowRetentionUntil = {_DeadlineSql("@now", _RetentionSpan)};

                    INSERT INTO {table} (
                        {SqlServerIdempotencySchema.TenantId},
                        {SqlServerIdempotencySchema.Key},
                        {SqlServerIdempotencySchema.Status},
                        {SqlServerIdempotencySchema.FingerprintAlgorithm},
                        {SqlServerIdempotencySchema.Fingerprint},
                        {SqlServerIdempotencySchema.Generation},
                        {SqlServerIdempotencySchema.LeaseExpiresAt},
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
                @rowLeaseExpiresAt datetimeoffset(7), @rowResult varbinary(max),
                @rowContract nvarchar({IdempotencyFieldLimits.ContractMaxLength}),
                @rowRetentionUntil datetimeoffset(7), @now datetimeoffset(7);

            SELECT @rowFound = 1,
                @rowStatus = {SqlServerIdempotencySchema.Status},
                @rowAlgorithm = {SqlServerIdempotencySchema.FingerprintAlgorithm},
                @rowFingerprint = {SqlServerIdempotencySchema.Fingerprint},
                @rowGeneration = {SqlServerIdempotencySchema.Generation},
                @rowLeaseExpiresAt = {SqlServerIdempotencySchema.LeaseExpiresAt},
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
                @rowLeaseExpiresAt,
                @rowResult,
                @rowContract,
                @rowRetentionUntil,
                CAST(CASE WHEN @rowRetentionUntil <= @now THEN 1 ELSE 0 END AS bit),
                CAST(CASE WHEN @rowLeaseExpiresAt > @now THEN 1 ELSE 0 END AS bit);
            """;
    }

    private static string _ExtendRetentionSql()
    {
        // Extends, never shortens: a retention already further out than this call's is kept. The record is already
        // locked by this transaction, so reading the clock before the update waits on nothing.
        return $"""
            DECLARE @now datetimeoffset(7), @extendTo datetimeoffset(7);
            SET @now = TODATETIMEOFFSET(SYSUTCDATETIME(), 0);
            SET @extendTo = {_DeadlineSql("@now", _RetentionSpan)};
            """;
    }

    private const string _ExtendedRetention = $"""
        CASE WHEN {SqlServerIdempotencySchema.RetentionUntil} > @extendTo THEN {SqlServerIdempotencySchema.RetentionUntil} ELSE @extendTo END
        """;

    private static string _BuildAdmitSql(string table, string sequence)
    {
        // Runs on a row this transaction already locked, so its clock and its generation both come after any wait.
        // Also the in-place reset of a record past its retention: every outcome column is overwritten.
        return $"""
            {_ExtendRetentionSql()}

            DECLARE @granted bigint, @grantedUntil datetimeoffset(7);
            SET @granted = NEXT VALUE FOR {sequence};
            SET @grantedUntil = {_DeadlineSql("@now", _LeaseSpan)};

            UPDATE {table}
            SET {SqlServerIdempotencySchema.Status} = {SqlServerIdempotencySchema.Pending},
                {SqlServerIdempotencySchema.FingerprintAlgorithm} = @FingerprintAlgorithm,
                {SqlServerIdempotencySchema.Fingerprint} = @Fingerprint,
                {SqlServerIdempotencySchema.Generation} = @granted,
                {SqlServerIdempotencySchema.LeaseExpiresAt} = @grantedUntil,
                {SqlServerIdempotencySchema.Result} = NULL,
                {SqlServerIdempotencySchema.ResultContract} = NULL,
                {SqlServerIdempotencySchema.RetentionUntil} = {_ExtendedRetention}
            WHERE {_KeyPredicate};

            SELECT @@ROWCOUNT, @granted, @grantedUntil;
            """;
    }

    private static string _BuildCompleteSql(string table)
    {
        // The completing generation is kept, so a second completion by the same attempt finds its own completed
        // record and is refused instead of overwriting the stored result.
        return $"""
            {_ExtendRetentionSql()}

            UPDATE {table}
            SET {SqlServerIdempotencySchema.Status} = {SqlServerIdempotencySchema.Completed},
                {SqlServerIdempotencySchema.LeaseExpiresAt} = NULL,
                {SqlServerIdempotencySchema.Result} = @Result,
                {SqlServerIdempotencySchema.ResultContract} = @ResultContract,
                {SqlServerIdempotencySchema.RetentionUntil} = {_ExtendedRetention}
            WHERE {_KeyPredicate}
                AND {SqlServerIdempotencySchema.Generation} = @Generation;

            SELECT @@ROWCOUNT;
            """;
    }

    private static string _BuildReleaseSql(string table)
    {
        return $"""
            {_ExtendRetentionSql()}

            UPDATE {table}
            SET {SqlServerIdempotencySchema.Status} = {SqlServerIdempotencySchema.Pending},
                {SqlServerIdempotencySchema.Generation} = NULL,
                {SqlServerIdempotencySchema.LeaseExpiresAt} = NULL,
                {SqlServerIdempotencySchema.Result} = NULL,
                {SqlServerIdempotencySchema.ResultContract} = NULL,
                {SqlServerIdempotencySchema.RetentionUntil} = {_ExtendedRetention}
            WHERE {_KeyPredicate}
                AND {SqlServerIdempotencySchema.Generation} = @Generation;

            SELECT @@ROWCOUNT;
            """;
    }

    private static string _BuildRenewSql(string table)
    {
        // The locking read waits out any holder, then the clock is read, so the guard below decides on a clock from
        // after the wait. The final SELECT reports the row as the locking read saw it, which classifies a refusal, and
        // @renewedUntil, which is set only when the renewal applied.
        return $"""
            DECLARE @rowFound bit = 0, @rowStatus smallint, @rowGeneration bigint,
                @rowLeaseExpiresAt datetimeoffset(7), @now datetimeoffset(7), @renewedUntil datetimeoffset(7);

            SELECT @rowFound = 1,
                @rowStatus = {SqlServerIdempotencySchema.Status},
                @rowGeneration = {SqlServerIdempotencySchema.Generation},
                @rowLeaseExpiresAt = {SqlServerIdempotencySchema.LeaseExpiresAt}
            FROM {table} WITH (UPDLOCK, HOLDLOCK, ROWLOCK)
            WHERE {_KeyPredicate};

            SET @now = TODATETIMEOFFSET(SYSUTCDATETIME(), 0);

            IF @rowGeneration = @Generation
                AND @rowStatus = {SqlServerIdempotencySchema.Pending}
                AND @rowLeaseExpiresAt > @now
            BEGIN
                SET @renewedUntil = {_DeadlineSql("@now", _LeaseSpan)};

                UPDATE {table}
                SET {SqlServerIdempotencySchema.LeaseExpiresAt} = @renewedUntil
                WHERE {_KeyPredicate};
            END;

            SELECT CASE WHEN @rowFound = 1 THEN @rowStatus END,
                @rowGeneration,
                @rowLeaseExpiresAt,
                CAST(CASE WHEN @rowLeaseExpiresAt > @now THEN 1 ELSE 0 END AS bit),
                @renewedUntil;
            """;
    }

    private static string _BuildPeekSql(string table)
    {
        // No UPDLOCK/HOLDLOCK: a plain read at whatever isolation the caller's transaction set.
        return $"""
            SELECT {SqlServerIdempotencySchema.Status},
                {SqlServerIdempotencySchema.RetentionUntil},
                TODATETIMEOFFSET(SYSUTCDATETIME(), 0)
            FROM {table}
            WHERE {_KeyPredicate};
            """;
    }

    private static string _BuildPurgeSql(string table, string hints)
    {
        // READPAST leaves a record an admission, fence, or completion holds for a later purge, so the purge never
        // waits on application work and never deletes a row under a transaction that is deciding on it. Since nothing
        // waits, the clock can be read first. A live lease keeps its record even past retention, because its attempt
        // may still complete.
        return $"""
            DECLARE @now datetimeoffset(7) = TODATETIMEOFFSET(SYSUTCDATETIME(), 0);
            DECLARE @cutoff datetimeoffset(7) = DATEADD(nanosecond, -@SpanNanoseconds,
                DATEADD(second, -@SpanSeconds, DATEADD(day, -@SpanDays, @now)));

            WITH doomed AS (
                SELECT TOP (@Limit) {SqlServerIdempotencySchema.TenantId}
                FROM {table} WITH ({hints})
                WHERE {SqlServerIdempotencySchema.RetentionUntil} <= @cutoff
                    AND (
                        {SqlServerIdempotencySchema.LeaseExpiresAt} IS NULL
                        OR {SqlServerIdempotencySchema.LeaseExpiresAt} <= @now
                    )
            )
            DELETE FROM doomed;

            SELECT @@ROWCOUNT;
            """;
    }

    #endregion
}
#pragma warning restore CA2100
