// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Data.Common;
using Headless.Fencing;
using Headless.Idempotency;
using Headless.Testing.Testcontainers;
using Headless.UnitOfWork;
using Microsoft.Data.SqlClient;

namespace Tests;

/// <summary>
/// SQL Server leaf fixture for the idempotency conformance suite: one container whose database holds both the fenced
/// leases and the idempotency records, with read committed snapshot isolation off;
/// <see cref="SqlServerRcsiIdempotencyFixture" /> runs the same storage with it on. Tests run serially because the
/// blocking scenarios measure how long a call waits.
/// </summary>
[UsedImplicitly]
[CollectionDefinition(DisableParallelization = true)]
public sealed class SqlServerIdempotencyFixture
    : SqlServerIdempotencyFixtureBase,
        ICollectionFixture<SqlServerIdempotencyFixture>
{
    protected override string Database => "idempotency_test";

    public override bool ReadCommittedSnapshot => false;
}

/// <summary>
/// The idempotency fixture over a database with read committed snapshot isolation on, where <c>READPAST</c> at READ
/// COMMITTED is refused unless the read also takes locks.
/// </summary>
[UsedImplicitly]
[CollectionDefinition(DisableParallelization = true)]
public sealed class SqlServerRcsiIdempotencyFixture
    : SqlServerIdempotencyFixtureBase,
        ICollectionFixture<SqlServerRcsiIdempotencyFixture>
{
    protected override string Database => "idempotency_rcsi_test";

    public override bool ReadCommittedSnapshot => true;
}

public abstract class SqlServerIdempotencyFixtureBase : HeadlessSqlServerFixture, IAsyncLifetime, IIdempotencyFixture
{
    private const string _Records = $"[{IdempotencyStorageOptions.DefaultSchema}].[records]";
    private const string _Leases = $"[{FencingStorageOptions.DefaultSchema}].[leases]";

    protected abstract string Database { get; }

    /// <summary>Whether the database runs read committed snapshot isolation.</summary>
    public abstract bool ReadCommittedSnapshot { get; }

    /// <summary>Connection string of the shared database (the base one points at master).</summary>
    public string DatabaseConnectionString =>
        new SqlConnectionStringBuilder(ConnectionString) { InitialCatalog = Database }.ToString();

    // Re-implemented rather than overridden: the base fixture's InitializeAsync is not virtual, and the database can
    // only be created once its container accepts logins.
    public new async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();

        await using (var master = new SqlConnection(ConnectionString))
        {
            await master.OpenAsync(CancellationToken.None);

            await using var create = new SqlCommand(
                $"IF DB_ID(N'{Database}') IS NULL CREATE DATABASE [{Database}];",
                master
            );
            await create.ExecuteNonQueryAsync(CancellationToken.None);

            var snapshot = ReadCommittedSnapshot ? "ON" : "OFF";
            await using var alter = new SqlCommand(
                $"ALTER DATABASE [{Database}] SET READ_COMMITTED_SNAPSHOT {snapshot} WITH ROLLBACK IMMEDIATE;",
                master
            );
            await alter.ExecuteNonQueryAsync(CancellationToken.None);
        }

        // The container is reused across runs, so start from no storage and let the initializers create it.
        await using var connection = new SqlConnection(DatabaseConnectionString);
        await connection.OpenAsync(CancellationToken.None);
        await using var reset = new SqlCommand(
            $"""
            IF OBJECT_ID(N'{IdempotencyStorageOptions.DefaultSchema}.records', N'U') IS NOT NULL DROP TABLE {_Records};
            IF SCHEMA_ID(N'{IdempotencyStorageOptions.DefaultSchema}') IS NOT NULL EXEC(N'DROP SCHEMA [{IdempotencyStorageOptions.DefaultSchema}]');
            IF OBJECT_ID(N'{FencingStorageOptions.DefaultSchema}.leases', N'U') IS NOT NULL DROP TABLE {_Leases};
            IF OBJECT_ID(N'{FencingStorageOptions.DefaultSchema}.lease_generations', N'SO') IS NOT NULL DROP SEQUENCE [{FencingStorageOptions.DefaultSchema}].[lease_generations];
            IF SCHEMA_ID(N'{FencingStorageOptions.DefaultSchema}') IS NOT NULL EXEC(N'DROP SCHEMA [{FencingStorageOptions.DefaultSchema}]');
            SELECT CAST(is_read_committed_snapshot_on AS int) FROM sys.databases WHERE database_id = DB_ID();
            """,
            connection
        );
        var probed = Convert.ToInt32(
            await reset.ExecuteScalarAsync(CancellationToken.None),
            CultureInfo.InvariantCulture
        );

        if (probed != (ReadCommittedSnapshot ? 1 : 0))
        {
            throw new InvalidOperationException(
                $"The database '{Database}' reports is_read_committed_snapshot_on = {probed}."
            );
        }
    }

    // Each host gets its own pool, and the contention tests run several hosts at once; a bounded pool keeps a runaway
    // test from exhausting the container's worker threads instead of contending.
    private string PooledConnectionString =>
        new SqlConnectionStringBuilder(DatabaseConnectionString) { MaxPoolSize = 30 }.ToString();

    public void ConfigureFencing(HeadlessFencingSetupBuilder setup)
    {
        setup.UseSqlServer(PooledConnectionString);
    }

    public void ConfigureIdempotency(HeadlessIdempotencySetupBuilder setup)
    {
        setup.UseSqlServer(PooledConnectionString);
    }

    public DbConnection CreateConnection()
    {
        return new SqlConnection(DatabaseConnectionString);
    }

    public ValueTask<IUnitOfWork> BeginOwnedAsync(
        IUnitOfWorkFactory factory,
        DbConnection connection,
        CancellationToken cancellationToken
    )
    {
        return factory.BeginAsync((SqlConnection)connection, cancellationToken: cancellationToken);
    }

    public async Task<StoredRecord?> ReadRecordAsync(IdempotencyRecordKey key, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(DatabaseConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(
            $"""
            SELECT [status], [fingerprint_algorithm], [fingerprint], [lease_generation], [result], [result_contract], [retention_until]
            FROM {_Records}
            WHERE [tenant_id] = @tenant AND [idempotency_key] = @recordKey
            """,
            connection
        );
        _AddKey(command, key);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new StoredRecord(
            (IdempotencyRecordStatus)reader.GetInt16(0),
            reader.GetString(1),
            await reader.GetFieldValueAsync<byte[]>(2, cancellationToken),
            await reader.IsDBNullAsync(3, cancellationToken) ? null : reader.GetInt64(3),
            await reader.IsDBNullAsync(4, cancellationToken)
                ? null
                : await reader.GetFieldValueAsync<byte[]>(4, cancellationToken),
            await reader.IsDBNullAsync(5, cancellationToken) ? null : reader.GetString(5),
            await reader.GetFieldValueAsync<DateTimeOffset>(6, cancellationToken)
        );
    }

    public async Task ShiftRecordIntoPastAsync(
        IdempotencyRecordKey key,
        TimeSpan by,
        CancellationToken cancellationToken
    )
    {
        await using var connection = new SqlConnection(DatabaseConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(
            $"""
            UPDATE {_Records}
            SET [retention_until] = {_Shifted("[retention_until]")}
            WHERE [tenant_id] = @tenant AND [idempotency_key] = @recordKey
            """,
            connection
        );
        _AddKey(command, key);
        _AddShift(command, by);

        (await command.ExecuteNonQueryAsync(cancellationToken)).Should().Be(1, "the record to age must exist");
    }

    public async Task<StoredLeaseRow?> ReadLeaseAsync(IdempotencyRecordKey key, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(DatabaseConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(
            $"""
            SELECT [generation], [state], [expires_at] FROM {_Leases}
            WHERE [tenant_id] = @tenant AND [kind] = @kind AND [resource] = @recordKey
            """,
            connection
        );
        _AddLeaseKey(command, key);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new StoredLeaseRow(
            reader.GetInt64(0),
            (StoredLeaseRowState)reader.GetInt16(1),
            await reader.GetFieldValueAsync<DateTimeOffset>(2, cancellationToken)
        );
    }

    public async Task ShiftLeaseIntoPastAsync(
        IdempotencyRecordKey key,
        TimeSpan by,
        CancellationToken cancellationToken
    )
    {
        await using var connection = new SqlConnection(DatabaseConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(
            $"""
            UPDATE {_Leases}
            SET [granted_at] = {_Shifted("[granted_at]")},
                [expires_at] = {_Shifted("[expires_at]")},
                [ended_at] = {_Shifted("[ended_at]")}
            WHERE [tenant_id] = @tenant AND [kind] = @kind AND [resource] = @recordKey
            """,
            connection
        );
        _AddLeaseKey(command, key);
        _AddShift(command, by);

        (await command.ExecuteNonQueryAsync(cancellationToken)).Should().Be(1, "the lease to age must exist");
    }

    private static void _AddKey(SqlCommand command, IdempotencyRecordKey key)
    {
        command.Parameters.Add(new SqlParameter("tenant", SqlDbType.NVarChar, 128) { Value = key.TenantId });
        command.Parameters.Add(new SqlParameter("recordKey", SqlDbType.NVarChar, 256) { Value = key.Key });
    }

    private static void _AddLeaseKey(SqlCommand command, IdempotencyRecordKey key)
    {
        _AddKey(command, key);
        command.Parameters.Add(
            new SqlParameter("kind", SqlDbType.NVarChar, 64) { Value = IdempotentAdmission.LeaseKind }
        );
    }

    // One DATEADD per unit, because a single int argument overflows in seconds or nanoseconds for long spans.
    private static string _Shifted(string column)
    {
        return $"DATEADD(nanosecond, -@ns, DATEADD(second, -@s, DATEADD(day, -@d, {column})))";
    }

    private static void _AddShift(SqlCommand command, TimeSpan by)
    {
        var ticksWithinDay = by.Ticks % TimeSpan.TicksPerDay;
        command.Parameters.Add(new SqlParameter("d", SqlDbType.Int) { Value = (int)(by.Ticks / TimeSpan.TicksPerDay) });
        command.Parameters.Add(
            new SqlParameter("s", SqlDbType.Int) { Value = (int)(ticksWithinDay / TimeSpan.TicksPerSecond) }
        );
        command.Parameters.Add(
            new SqlParameter("ns", SqlDbType.Int) { Value = (int)(ticksWithinDay % TimeSpan.TicksPerSecond * 100) }
        );
    }
}
