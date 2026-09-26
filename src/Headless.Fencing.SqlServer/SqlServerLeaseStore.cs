// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Data.Common;
using System.Runtime.InteropServices;
using Headless.Checks;
using Headless.Constants;
using Headless.UnitOfWork;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Headless.Fencing.SqlServer;

/// <summary>
/// The SQL Server lease store. Every verb is one batch that first reads the lease row with an update-intent lock held
/// to the end of the transaction, and only then captures <c>SYSUTCDATETIME()</c>, once, into the variable every
/// decision in the batch reads, so a decision is never made on a clock read from before a lock wait.
/// </summary>
/// <remarks>
/// <para>
/// The lock is <c>UPDLOCK, HOLDLOCK, ROWLOCK</c>. <c>UPDLOCK</c> rather than a shared lock: a fence holds it until
/// the caller's transaction ends, and a grant, renewal, settlement, or sweep waiting on it then sees the committed
/// outcome, where a waiting grant queued for the update lock behind a shared one would deadlock against the fencing
/// transaction's own settlement. <c>HOLDLOCK</c> keeps it to the end of the transaction and, when the row is absent,
/// locks the key range, so two first grants of one key serialize instead of racing to insert. Update locks are taken
/// the same way under read committed snapshot isolation, so no RCSI hint is needed.
/// </para>
/// <para>
/// No batch uses <c>TRY/CATCH</c> or <c>SET</c> options. An enlisted batch runs on the caller's session: a caught
/// error would still doom a caller transaction running with <c>XACT_ABORT ON</c>, and a <c>SET</c> would outlive the
/// batch for the rest of that session. The grant therefore never attempts an insert that could collide; it decides
/// from its locked read.
/// </para>
/// </remarks>
#pragma warning disable CA2100 // SQL text is built from the validated schema name plus internal object and column constants.
internal sealed class SqlServerLeaseStore : ILeaseStore
{
    // A deadlock or snapshot update conflict is the one failure a fresh transaction can clear on its own; the first
    // attempt plus two retries.
    private const int _MaxAttempts = 3;

    // Each purge batch is its own short transaction so a large purge never holds many row locks at once.
    private const int _PurgeBatchSize = 1000;

    // datetimeoffset reaches back to year 1, so a purge cutoff further back than this would overflow DATEADD. No
    // lease can have ended that long ago, so clamping the age deletes exactly the same rows.
    private const int _MaxPurgeAgeDays = 700_000;

    private const string _ReadCommittedSnapshotProbeSql =
        "SELECT is_read_committed_snapshot_on FROM sys.databases WHERE database_id = DB_ID();";

    private readonly SqlServerFencingOptions _options;
    private readonly IUnitOfWorkFactory _unitOfWorkFactory;
    private readonly string _table;
    private readonly string _grantSql;
    private readonly string _renewSql;
    private readonly string _settleSql;
    private readonly string _releaseSql;
    private readonly string _fenceSql;

    // Built once per store, after probing the lease database's snapshot setting; null until the first sweep or purge.
    private SweepStatements? _sweepStatements;

    public SqlServerLeaseStore(
        IOptions<SqlServerFencingOptions> options,
        IOptions<FencingStorageOptions> storageOptions,
        IUnitOfWorkFactory unitOfWorkFactory
    )
    {
        _options = options.Value;
        _unitOfWorkFactory = unitOfWorkFactory;

        var schema = storageOptions.Value.Schema;
        _table = SqlServerFencingSchema.QualifiedTable(schema);

        _grantSql = _BuildGrantSql(_table, SqlServerFencingSchema.QualifiedSequence(schema));
        _renewSql = _BuildTransitionSql(
            _table,
            $"""
            SET @changed = {_DeadlineSql("@now")};

                UPDATE {_table}
                SET {SqlServerFencingSchema.ExpiresAt} = @changed
                WHERE {_KeyPredicate()};
            """
        );
        _settleSql = _BuildTransitionSql(_table, _EndStatement(_table, SqlServerFencingSchema.Settled));
        _releaseSql = _BuildTransitionSql(_table, _EndStatement(_table, SqlServerFencingSchema.Released));
        _fenceSql = _BuildFenceSql(_table);
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

    public void ValidateEnlistment(IUnitOfWork unitOfWork)
    {
        Argument.IsNotNull(unitOfWork);

        // The gate every enlisted lease call passed before this store judged the unit: an active unit carrying a
        // live relational transaction. Which provider and database that transaction belongs to is checked below.
        UnitOfWorkTransactions.RequireTransaction<DbTransaction>(unitOfWork, UnitOfWorkLeasesFeature.Operation);
        var (connection, _) = _RequireLive(_Relational(unitOfWork));

        using var configured = _options.CreateConnection();

        if (!RelationalDatabaseIdentity.IsSameDatabase(configured, connection))
        {
            throw new InvalidOperationException(
                $"The unit of work's connection targets database '{connection.Database}' on '{connection.DataSource}', "
                    + $"but Headless.Fencing.SqlServer is configured for database '{configured.Database}' on "
                    + $"'{configured.DataSource}'. An enlisted lease call runs in the unit's own transaction, so the "
                    + "unit must run on the database that holds the leases."
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
        IUnitOfWork unitOfWork,
        LeaseKey key,
        TimeSpan duration,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(unitOfWork);

        // Re-checked here rather than trusted from validation: the caller may have ended the transaction or closed
        // the connection in between, and a statement on either would fail with a less useful message or, worse, run
        // outside the unit. No retry: a deadlock has already rolled back the caller's transaction, so only the unit's
        // owner can decide whether to run the whole unit again.
        var (connection, transaction) = _RequireLive(_Relational(unitOfWork));

        return await _GrantAsync(connection, transaction, key, duration, cancellationToken).ConfigureAwait(false);
    }

    private async Task<LeaseGrantResult> _GrantAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        LeaseKey key,
        TimeSpan duration,
        CancellationToken cancellationToken
    )
    {
        await using var command = _CreateCommand(_grantSql, connection, transaction, key);
        _AddSpanParameters(command, duration);

        await using var reader = await _ExecuteReaderAsync(command, cancellationToken).ConfigureAwait(false);
        await reader.ReadAsync(cancellationToken).ConfigureAwait(false);

        var outcome = reader.GetByte(0);
        var generation = reader.GetInt64(1);
        var expiresAt = await reader.GetFieldValueAsync<DateTimeOffset>(2, cancellationToken).ConfigureAwait(false);

        return outcome switch
        {
            _GrantOutcomeHeld => LeaseGrantResult.Held(generation, expiresAt),
            _GrantOutcomeTakeover => LeaseGrantResult.Takeover(key.ToLease(generation), expiresAt, reader.GetInt64(3)),
            _ => LeaseGrantResult.Granted(key.ToLease(generation), expiresAt),
        };
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
        IUnitOfWork unitOfWork,
        LeaseKey key,
        long generation,
        TimeSpan duration,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(unitOfWork);
        var (connection, transaction) = _RequireLive(_Relational(unitOfWork));

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
        IUnitOfWork unitOfWork,
        LeaseKey key,
        long generation,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(unitOfWork);
        var (connection, transaction) = _RequireLive(_Relational(unitOfWork));

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
        Argument.IsNotNull(unitOfWork);
        var (connection, transaction) = _RequireLive(_Relational(unitOfWork));

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
        SqlConnection connection,
        SqlTransaction transaction,
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
        SqlConnection connection,
        SqlTransaction transaction,
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
        SqlConnection connection,
        SqlTransaction transaction,
        string sql,
        LeaseKey key,
        long generation,
        TimeSpan? duration,
        CancellationToken cancellationToken
    )
    {
        await using var command = _CreateCommand(sql, connection, transaction, key);
        command.Parameters.Add(new SqlParameter("Generation", SqlDbType.BigInt) { Value = generation });

        if (duration is { } value)
        {
            _AddSpanParameters(command, value);
        }

        await using var reader = await _ExecuteReaderAsync(command, cancellationToken).ConfigureAwait(false);
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
        IUnitOfWork unitOfWork,
        LeaseKey key,
        long generation,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(unitOfWork);
        var (connection, transaction) = _RequireLive(_Relational(unitOfWork));

        await using var command = _CreateCommand(_fenceSql, connection, transaction, key);
        await using var reader = await _ExecuteReaderAsync(command, cancellationToken).ConfigureAwait(false);
        await reader.ReadAsync(cancellationToken).ConfigureAwait(false);

        return _Classify(await _ReadRowAsync(reader, cancellationToken).ConfigureAwait(false), generation);
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
        Argument.IsNotNull(unitOfWork);
        Argument.IsNotNull(kind);
        var (connection, transaction) = _RequireLive(_Relational(unitOfWork));

        var statements = await _GetSweepStatementsAsync(connection, transaction, cancellationToken)
            .ConfigureAwait(false);

        await using var command = new SqlCommand(
            after is null ? statements.ClaimFirst : statements.ClaimAfter,
            connection,
            transaction
        )
        {
            CommandTimeout = _options.CommandTimeoutSeconds,
        };
        command.Parameters.Add(_KindParameter(kind));

        if (after is not null)
        {
            command.Parameters.Add(
                new SqlParameter("AfterExpiresAt", SqlDbType.DateTimeOffset) { Value = after.ExpiresAt }
            );
            command.Parameters.Add(
                new SqlParameter("AfterTenantId", SqlDbType.NVarChar, FencingFieldLimits.TenantIdMaxLength)
                {
                    Value = after.TenantId ?? string.Empty,
                }
            );
            command.Parameters.Add(
                new SqlParameter("AfterResource", SqlDbType.NVarChar, FencingFieldLimits.ResourceMaxLength)
                {
                    Value = after.Resource,
                }
            );
        }

        await using var reader = await _ExecuteReaderAsync(command, cancellationToken).ConfigureAwait(false);

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
                        var statements = await _GetSweepStatementsAsync(connection, transaction, ct)
                            .ConfigureAwait(false);

                        await using var command = new SqlCommand(statements.Purge, connection, transaction)
                        {
                            CommandTimeout = _options.CommandTimeoutSeconds,
                        };
                        command.Parameters.Add(_KindParameter(kind));
                        command.Parameters.Add(
                            new SqlParameter("BatchSize", SqlDbType.Int) { Value = _PurgeBatchSize }
                        );
                        _AddSpanParameters(command, age);

                        try
                        {
                            return await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                        }
                        catch (SqlException ex) when (ct.IsCancellationRequested)
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

    private async ValueTask<T> _RunAutonomousAsync<T>(
        Func<SqlConnection, SqlTransaction, CancellationToken, Task<T>> body,
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
                // Explicit so the verb runs at READ COMMITTED whatever isolation level a pooled session last used:
                // READPAST is refused above it, and a stricter level turns a lock wait into a conflict.
                await using var transaction = (SqlTransaction)
                    await connection
                        .BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
                        .ConfigureAwait(false);

                var result = await body(connection, transaction, cancellationToken).ConfigureAwait(false);

                // The batch already decided and the result describes it; a late cancel must not roll back a write the
                // caller is about to be told happened.
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

    /// <summary>
    /// Returns the sweep and purge statements for the lease database. <c>READPAST</c> is refused at READ COMMITTED
    /// when the database runs read committed snapshot isolation unless the read also takes locks, so the statements
    /// add <c>READCOMMITTEDLOCK</c> there.
    /// </summary>
    private async Task<SweepStatements> _GetSweepStatementsAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        CancellationToken cancellationToken
    )
    {
        if (_sweepStatements is { } cached)
        {
            return cached;
        }

        await using var command = new SqlCommand(_ReadCommittedSnapshotProbeSql, connection, transaction)
        {
            CommandTimeout = _options.CommandTimeoutSeconds,
        };
        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        var hints = GetReadPastHints(result is true);

        // Two first sweeps may both probe; they build the same statements.
        return _sweepStatements = new SweepStatements(
            _BuildClaimSql(_table, hints, withCursor: false),
            _BuildClaimSql(_table, hints, withCursor: true),
            _BuildPurgeSql(_table, hints)
        );
    }

    internal static string GetReadPastHints(bool readCommittedSnapshotEnabled)
    {
        return readCommittedSnapshotEnabled
            ? "UPDLOCK, READPAST, ROWLOCK, READCOMMITTEDLOCK"
            : "UPDLOCK, READPAST, ROWLOCK";
    }

    private SqlCommand _CreateCommand(string sql, SqlConnection connection, SqlTransaction transaction, LeaseKey key)
    {
        var command = new SqlCommand(sql, connection, transaction) { CommandTimeout = _options.CommandTimeoutSeconds };
        command.Parameters.Add(
            new SqlParameter("TenantId", SqlDbType.NVarChar, FencingFieldLimits.TenantIdMaxLength)
            {
                Value = key.TenantId,
            }
        );
        command.Parameters.Add(_KindParameter(key.Kind));
        command.Parameters.Add(
            new SqlParameter("Resource", SqlDbType.NVarChar, FencingFieldLimits.ResourceMaxLength)
            {
                Value = key.Resource,
            }
        );

        return command;
    }

    private static SqlParameter _KindParameter(string kind)
    {
        return new SqlParameter("Kind", SqlDbType.NVarChar, FencingFieldLimits.KindMaxLength) { Value = kind };
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
            SqlServerFencingSchema.Settled => LeaseFenceStatus.Settled,
            SqlServerFencingSchema.Released => LeaseFenceStatus.Released,
            SqlServerFencingSchema.Abandoned => LeaseFenceStatus.Abandoned,
            SqlServerFencingSchema.Active when value.IsLive => LeaseFenceStatus.Current,
            SqlServerFencingSchema.Active => LeaseFenceStatus.Expired,
            _ => throw new InvalidOperationException(
                $"The lease row carries an unknown state {value.State.ToString(CultureInfo.InvariantCulture)}; the table was changed outside this provider."
            ),
        };
    }

    private static InvalidOperationException _UnchangedLiveLease(LeaseKey key)
    {
        // The update and the classifying read share one clock value and the row lock, so a live row at this
        // generation is always changed; reaching here means the table was changed outside this provider.
        return new InvalidOperationException(
            $"The lease '{key.Kind}/{key.Resource}' was live at the caller's generation but not updated."
        );
    }

    private static IRelationalUnitOfWorkResource _Relational(IUnitOfWork unitOfWork)
    {
        // Enlisted verbs run only on a unit ValidateEnlistment accepted, whose resource is relational.
        return (IRelationalUnitOfWorkResource)unitOfWork.Resource!;
    }

    private static (SqlConnection Connection, SqlTransaction Transaction) _RequireLive(
        IRelationalUnitOfWorkResource resource
    )
    {
        if (resource.Transaction is not SqlTransaction transaction)
        {
            throw new InvalidOperationException(
                $"The unit of work carries a '{resource.Transaction.GetType().FullName}' transaction, but "
                    + "Headless.Fencing.SqlServer runs enlisted lease calls only through a SqlTransaction. Begin the "
                    + "unit on the SQL Server database that holds the leases."
            );
        }

        if (resource.Connection is not SqlConnection { State: ConnectionState.Open } connection)
        {
            throw new InvalidOperationException(
                "The unit of work's connection is not an open SqlConnection, so the lease call cannot run inside "
                    + "its transaction."
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

    private const byte _GrantOutcomeGranted = 0;
    private const byte _GrantOutcomeTakeover = 1;
    private const byte _GrantOutcomeHeld = 2;

    private static string _KeyPredicate()
    {
        return $"""
            {SqlServerFencingSchema.TenantId} = @TenantId
                    AND {SqlServerFencingSchema.Kind} = @Kind
                    AND {SqlServerFencingSchema.Resource} = @Resource
            """;
    }

    /// <summary>A deadline <paramref name="start" /> plus the span parameters, one DATEADD per unit.</summary>
    private static string _DeadlineSql(string start)
    {
        return "DATEADD(nanosecond, @SpanNanoseconds, "
            + "DATEADD(second, @SpanSeconds, "
            + $"DATEADD(day, @SpanDays, {start})))";
    }

    private static string _LockThenClockSql(string table)
    {
        // Takes the row's update-intent lock into variables, then reads the clock. The clock statement runs only
        // after the locking read has waited out any other holder, so every decision below sees a clock from after
        // the wait. When the row is absent every variable stays NULL.
        return $"""
            DECLARE @rowGeneration bigint, @rowState smallint, @rowExpiresAt datetimeoffset(7), @now datetimeoffset(7);

            SELECT @rowGeneration = {SqlServerFencingSchema.Generation},
                @rowState = {SqlServerFencingSchema.State},
                @rowExpiresAt = {SqlServerFencingSchema.ExpiresAt}
            FROM {table} WITH (UPDLOCK, HOLDLOCK, ROWLOCK)
            WHERE {_KeyPredicate()};

            SET @now = TODATETIMEOFFSET(SYSUTCDATETIME(), 0);
            """;
    }

    private static string _BuildGrantSql(string table, string sequence)
    {
        // NEXT VALUE FOR is illegal inside CASE, OUTPUT, WHERE, subqueries, and MERGE, so the generation is drawn
        // into a variable, and only on the branch that grants, after the locking read: a generation drawn before the
        // lock could be lower than one a still-open grant already holds. A row that exists is never inserted over,
        // so the batch cannot raise a duplicate-key error inside a caller's transaction.
        return $"""
            {_LockThenClockSql(table)}

            IF @rowState = {SqlServerFencingSchema.Active} AND @rowExpiresAt > @now
                SELECT CAST({_GrantOutcomeHeld} AS tinyint), @rowGeneration, @rowExpiresAt, CAST(NULL AS bigint);
            ELSE
            BEGIN
                DECLARE @granted bigint, @grantedUntil datetimeoffset(7);
                SET @granted = NEXT VALUE FOR {sequence};
                SET @grantedUntil = {_DeadlineSql("@now")};

                IF @rowGeneration IS NULL
                    INSERT INTO {table} (
                        {SqlServerFencingSchema.TenantId},
                        {SqlServerFencingSchema.Kind},
                        {SqlServerFencingSchema.Resource},
                        {SqlServerFencingSchema.Generation},
                        {SqlServerFencingSchema.State},
                        {SqlServerFencingSchema.GrantedAt},
                        {SqlServerFencingSchema.ExpiresAt},
                        {SqlServerFencingSchema.EndedAt}
                    )
                    VALUES (@TenantId, @Kind, @Resource, @granted, {SqlServerFencingSchema.Active}, @now, @grantedUntil, NULL);
                ELSE
                    UPDATE {table}
                    SET {SqlServerFencingSchema.Generation} = @granted,
                        {SqlServerFencingSchema.State} = {SqlServerFencingSchema.Active},
                        {SqlServerFencingSchema.GrantedAt} = @now,
                        {SqlServerFencingSchema.ExpiresAt} = @grantedUntil,
                        {SqlServerFencingSchema.EndedAt} = NULL
                    WHERE {_KeyPredicate()};

                -- An expired active row is a takeover and reports its generation; an ended row is simply free.
                SELECT CAST(CASE WHEN @rowState = {SqlServerFencingSchema.Active} THEN {_GrantOutcomeTakeover} ELSE {_GrantOutcomeGranted} END AS tinyint),
                    @granted,
                    @grantedUntil,
                    @rowGeneration;
            END;
            """;
    }

    private static string _EndStatement(string table, short state)
    {
        return $"""
            UPDATE {table}
                SET {SqlServerFencingSchema.State} = {state.ToString(
                CultureInfo.InvariantCulture
            )}, {SqlServerFencingSchema.EndedAt} = @now
                WHERE {_KeyPredicate()};

                SET @changed = @rowExpiresAt;
            """;
    }

    private static string _BuildTransitionSql(string table, string change)
    {
        // The final SELECT reports the row as the locking read saw it, which classifies a refusal, and @changed,
        // which is set only when the change applied.
        return $"""
            {_LockThenClockSql(table)}

            DECLARE @changed datetimeoffset(7);

            IF @rowGeneration = @Generation AND @rowState = {SqlServerFencingSchema.Active} AND @rowExpiresAt > @now
            BEGIN
                {change}
            END;

            SELECT @rowGeneration,
                @rowState,
                @rowExpiresAt,
                CAST(CASE WHEN @rowExpiresAt > @now THEN 1 ELSE 0 END AS bit),
                @changed;
            """;
    }

    private static string _BuildFenceSql(string table)
    {
        return $"""
            {_LockThenClockSql(table)}

            SELECT @rowGeneration, @rowState, @rowExpiresAt, CAST(CASE WHEN @rowExpiresAt > @now THEN 1 ELSE 0 END AS bit);
            """;
    }

    private static string _BuildClaimSql(string table, string hints, bool withCursor)
    {
        // READPAST passes over a lease another sweeper is claiming or a fence is holding, so concurrent sweepers never
        // hand one lease to two handlers and never wait on each other; since nothing waits, the clock can be read
        // first. The keyset cursor walks the active expiry index; a lease the caller already visited is behind it, so
        // a lease whose handler threw is left for a later call.
        var cursor = withCursor
            ? $"""
                AND (
                            {SqlServerFencingSchema.ExpiresAt} > @AfterExpiresAt
                            OR ({SqlServerFencingSchema.ExpiresAt} = @AfterExpiresAt AND {SqlServerFencingSchema.TenantId} > @AfterTenantId)
                            OR (
                                {SqlServerFencingSchema.ExpiresAt} = @AfterExpiresAt
                                AND {SqlServerFencingSchema.TenantId} = @AfterTenantId
                                AND {SqlServerFencingSchema.Resource} > @AfterResource
                            )
                        )
                """
            : string.Empty;

        return $"""
            DECLARE @now datetimeoffset(7) = TODATETIMEOFFSET(SYSUTCDATETIME(), 0);

            WITH candidate AS (
                SELECT TOP (1)
                    {SqlServerFencingSchema.TenantId},
                    {SqlServerFencingSchema.Resource},
                    {SqlServerFencingSchema.Generation},
                    {SqlServerFencingSchema.State},
                    {SqlServerFencingSchema.ExpiresAt},
                    {SqlServerFencingSchema.EndedAt}
                FROM {table} WITH ({hints})
                WHERE {SqlServerFencingSchema.Kind} = @Kind
                    AND {SqlServerFencingSchema.State} = {SqlServerFencingSchema.Active}
                    AND {SqlServerFencingSchema.ExpiresAt} <= @now
                    {cursor}
                ORDER BY {SqlServerFencingSchema.ExpiresAt}, {SqlServerFencingSchema.TenantId}, {SqlServerFencingSchema.Resource}
            )
            UPDATE candidate
            SET {SqlServerFencingSchema.State} = {SqlServerFencingSchema.Abandoned},
                {SqlServerFencingSchema.EndedAt} = @now
            OUTPUT inserted.{SqlServerFencingSchema.TenantId},
                inserted.{SqlServerFencingSchema.Resource},
                inserted.{SqlServerFencingSchema.Generation},
                inserted.{SqlServerFencingSchema.ExpiresAt};
            """;
    }

    private static string _BuildPurgeSql(string table, string hints)
    {
        // Only ended rows are deleted; an active row, expired or not, is kept. READPAST leaves a row a grant is
        // re-granting for a later purge. Deleting a row never lets a generation repeat, because generations come from
        // the store-wide sequence.
        return $"""
            DECLARE @cutoff datetimeoffset(7) = DATEADD(nanosecond, -@SpanNanoseconds,
                DATEADD(second, -@SpanSeconds, DATEADD(day, -@SpanDays, TODATETIMEOFFSET(SYSUTCDATETIME(), 0))));

            WITH doomed AS (
                SELECT TOP (@BatchSize) {SqlServerFencingSchema.TenantId}
                FROM {table} WITH ({hints})
                WHERE {SqlServerFencingSchema.Kind} = @Kind
                    AND {SqlServerFencingSchema.State} <> {SqlServerFencingSchema.Active}
                    AND {SqlServerFencingSchema.EndedAt} <= @cutoff
            )
            DELETE FROM doomed;
            """;
    }

    #endregion

    private sealed record SweepStatements(string ClaimFirst, string ClaimAfter, string Purge);

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct LeaseRow(long Generation, short State, DateTimeOffset ExpiresAt, bool IsLive);
}
#pragma warning restore CA2100
