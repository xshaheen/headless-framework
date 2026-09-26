// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Data.Common;
using Headless.Fencing;
using Headless.Testing.Testcontainers;
using Headless.UnitOfWork;
using Microsoft.Data.SqlClient;

namespace Tests;

/// <summary>
/// SQL Server leaf fixture for the fencing conformance suite: one container holding the lease database and a
/// second, empty database used to prove that a unit on another database is refused. The lease database runs with
/// read committed snapshot isolation off; <see cref="SqlServerRcsiFencingFixture" /> runs the same storage with it
/// on. Tests run serially because the blocking scenarios measure how long a call waits.
/// </summary>
[UsedImplicitly]
[CollectionDefinition(DisableParallelization = true)]
public sealed class SqlServerFencingFixture : SqlServerFencingFixtureBase, ICollectionFixture<SqlServerFencingFixture>
{
    protected override string LeaseDatabase => "fencing_test";

    public override bool ReadCommittedSnapshot => false;
}

/// <summary>
/// The fencing fixture over a lease database with read committed snapshot isolation on, where <c>READPAST</c> at
/// READ COMMITTED is refused unless the read also takes locks.
/// </summary>
[UsedImplicitly]
[CollectionDefinition(DisableParallelization = true)]
public sealed class SqlServerRcsiFencingFixture
    : SqlServerFencingFixtureBase,
        ICollectionFixture<SqlServerRcsiFencingFixture>
{
    protected override string LeaseDatabase => "fencing_rcsi_test";

    public override bool ReadCommittedSnapshot => true;
}

public abstract class SqlServerFencingFixtureBase : HeadlessSqlServerFixture, IAsyncLifetime, ILeasesFixture
{
    private const string _OtherDatabase = "fencing_other";
    private const string _HandoffTable = "[dbo].[fencing_handoffs]";

    protected abstract string LeaseDatabase { get; }

    /// <summary>Whether the lease database runs read committed snapshot isolation.</summary>
    public abstract bool ReadCommittedSnapshot { get; }

    /// <summary>Connection string of the database that holds the leases (the base one points at master).</summary>
    public string LeaseConnectionString =>
        new SqlConnectionStringBuilder(ConnectionString) { InitialCatalog = LeaseDatabase }.ToString();

    public string OtherDatabaseConnectionString =>
        new SqlConnectionStringBuilder(ConnectionString) { InitialCatalog = _OtherDatabase }.ToString();

    // Re-implemented rather than overridden: the base fixture's InitializeAsync is not virtual, and the databases can
    // only be created once its container accepts logins.
    public new async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();

        await using (var master = new SqlConnection(ConnectionString))
        {
            await master.OpenAsync(CancellationToken.None);

            foreach (var database in (string[])[LeaseDatabase, _OtherDatabase])
            {
                await using var create = new SqlCommand(
                    $"IF DB_ID(N'{database}') IS NULL CREATE DATABASE [{database}];",
                    master
                );
                await create.ExecuteNonQueryAsync(CancellationToken.None);
            }

            var snapshot = ReadCommittedSnapshot ? "ON" : "OFF";
            await using var alter = new SqlCommand(
                $"ALTER DATABASE [{LeaseDatabase}] SET READ_COMMITTED_SNAPSHOT {snapshot} WITH ROLLBACK IMMEDIATE;",
                master
            );
            await alter.ExecuteNonQueryAsync(CancellationToken.None);
        }

        // The container is reused across runs, so start from no lease storage and let the initializer create it.
        await ExecuteAsync(
            $"""
            {DropStorageSql(FencingStorageOptions.DefaultSchema)}
            IF OBJECT_ID(N'{_HandoffTable}', N'U') IS NOT NULL DROP TABLE {_HandoffTable};
            CREATE TABLE {_HandoffTable} (
                [id] bigint IDENTITY(1, 1) PRIMARY KEY,
                [tenant_id] nvarchar(128) NOT NULL,
                [kind] nvarchar(64) NOT NULL,
                [resource] nvarchar(256) NOT NULL,
                [generation] bigint NOT NULL
            );
            """,
            CancellationToken.None
        );

        var probed = await ScalarAsync(
            "SELECT CAST(is_read_committed_snapshot_on AS int) FROM sys.databases WHERE database_id = DB_ID()",
            CancellationToken.None
        );

        if (probed != (ReadCommittedSnapshot ? 1 : 0))
        {
            throw new InvalidOperationException(
                $"The lease database '{LeaseDatabase}' reports is_read_committed_snapshot_on = {probed}."
            );
        }
    }

    public void ConfigureProvider(HeadlessFencingSetupBuilder setup)
    {
        // Each host gets its own pool, and the contention tests run several hosts at once; a bounded pool keeps a
        // runaway test from exhausting the container's worker threads instead of contending.
        setup.UseSqlServer(new SqlConnectionStringBuilder(LeaseConnectionString) { MaxPoolSize = 30 }.ToString());
    }

    public DbConnection CreateConnection()
    {
        return new SqlConnection(LeaseConnectionString);
    }

    public DbConnection CreateOtherDatabaseConnection()
    {
        return new SqlConnection(OtherDatabaseConnectionString);
    }

    public ValueTask<IUnitOfWork> BeginOwnedAsync(
        IUnitOfWorkFactory factory,
        DbConnection connection,
        CancellationToken cancellationToken
    )
    {
        return factory.BeginAsync((SqlConnection)connection, cancellationToken: cancellationToken);
    }

    public IUnitOfWork Enlist(IUnitOfWorkFactory factory, DbConnection connection, DbTransaction transaction)
    {
        return factory.Enlist((SqlConnection)connection, (SqlTransaction)transaction);
    }

    public Task<StoredLease?> ReadLeaseAsync(LeaseKey key, CancellationToken cancellationToken)
    {
        return ReadLeaseAsync(key, FencingStorageOptions.DefaultSchema, cancellationToken);
    }

    public async Task<StoredLease?> ReadLeaseAsync(LeaseKey key, string schema, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(LeaseConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(
            $"""
            SELECT [generation], [state], [granted_at], [expires_at], [ended_at] FROM [{schema}].[leases]
            WHERE [tenant_id] = @tenant AND [kind] = @kind AND [resource] = @resource
            """,
            connection
        );
        _AddKey(command, key);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new StoredLease(
            reader.GetInt64(0),
            (StoredLeaseState)reader.GetInt16(1),
            await reader.GetFieldValueAsync<DateTimeOffset>(2, cancellationToken),
            await reader.GetFieldValueAsync<DateTimeOffset>(3, cancellationToken),
            await reader.IsDBNullAsync(4, cancellationToken)
                ? null
                : await reader.GetFieldValueAsync<DateTimeOffset>(4, cancellationToken)
        );
    }

    public async Task ShiftIntoPastAsync(LeaseKey key, TimeSpan by, CancellationToken cancellationToken)
    {
        // One DATEADD per unit, because a single int argument overflows in seconds or nanoseconds for long spans.
        const string shift = "DATEADD(nanosecond, -@ns, DATEADD(second, -@s, DATEADD(day, -@d, {0})))";

        await using var connection = new SqlConnection(LeaseConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(
            $"""
            UPDATE [{FencingStorageOptions.DefaultSchema}].[leases]
            SET [granted_at] = {string.Format(CultureInfo.InvariantCulture, shift, "[granted_at]")},
                [expires_at] = {string.Format(CultureInfo.InvariantCulture, shift, "[expires_at]")},
                [ended_at] = {string.Format(CultureInfo.InvariantCulture, shift, "[ended_at]")}
            WHERE [tenant_id] = @tenant AND [kind] = @kind AND [resource] = @resource
            """,
            connection
        );
        _AddKey(command, key);
        var ticksWithinDay = by.Ticks % TimeSpan.TicksPerDay;
        command.Parameters.Add(new SqlParameter("d", SqlDbType.Int) { Value = (int)(by.Ticks / TimeSpan.TicksPerDay) });
        command.Parameters.Add(
            new SqlParameter("s", SqlDbType.Int) { Value = (int)(ticksWithinDay / TimeSpan.TicksPerSecond) }
        );
        command.Parameters.Add(
            new SqlParameter("ns", SqlDbType.Int) { Value = (int)(ticksWithinDay % TimeSpan.TicksPerSecond * 100) }
        );

        (await command.ExecuteNonQueryAsync(cancellationToken)).Should().Be(1, "the lease row to age must exist");
    }

    public async Task WriteHandoffAsync(IUnitOfWork unit, ExpiredLease lease, CancellationToken cancellationToken)
    {
        var resource = (IRelationalUnitOfWorkResource)unit.Resource!;
        await using var command = new SqlCommand(
            $"INSERT INTO {_HandoffTable} ([tenant_id], [kind], [resource], [generation]) VALUES (@tenant, @kind, @resource, @generation)",
            (SqlConnection)resource.Connection,
            (SqlTransaction)resource.Transaction
        );
        command.Parameters.AddWithValue("tenant", lease.TenantId ?? "");
        command.Parameters.AddWithValue("kind", lease.Kind);
        command.Parameters.AddWithValue("resource", lease.Resource);
        command.Parameters.AddWithValue("generation", lease.Generation);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<LeaseHandoff>> ReadHandoffsAsync(string kind, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(LeaseConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(
            $"SELECT [tenant_id], [resource], [generation] FROM {_HandoffTable} WHERE [kind] = @kind",
            connection
        );
        command.Parameters.AddWithValue(nameof(kind), kind);

        var handoffs = new List<LeaseHandoff>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            handoffs.Add(new LeaseHandoff(reader.GetString(0), reader.GetString(1), reader.GetInt64(2)));
        }

        return handoffs;
    }

    public async Task<int> ScalarAsync(
        string sql,
        CancellationToken cancellationToken,
        params (string Name, object Value)[] parameters
    )
    {
        await using var connection = new SqlConnection(LeaseConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);

        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
    }

    public async Task ExecuteAsync(string sql, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(LeaseConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>Drops the lease table, the generation sequence, and then their schema, when they exist.</summary>
    public static string DropStorageSql(string schema)
    {
        return $"""
            IF OBJECT_ID(N'{schema}.leases', N'U') IS NOT NULL DROP TABLE [{schema}].[leases];
            IF OBJECT_ID(N'{schema}.lease_generations', N'SO') IS NOT NULL DROP SEQUENCE [{schema}].[lease_generations];
            IF SCHEMA_ID(N'{schema}') IS NOT NULL EXEC(N'DROP SCHEMA [{schema}]');
            """;
    }

    private static void _AddKey(SqlCommand command, LeaseKey key)
    {
        command.Parameters.AddWithValue("tenant", key.TenantId);
        command.Parameters.AddWithValue("kind", key.Kind);
        command.Parameters.AddWithValue("resource", key.Resource);
    }
}
