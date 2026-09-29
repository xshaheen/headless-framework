// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Fencing;
using Headless.Testing.Testcontainers;
using Headless.UnitOfWork;
using Npgsql;
using NpgsqlTypes;
using Testcontainers.PostgreSql;

namespace Tests;

/// <summary>
/// PostgreSQL leaf fixture for the fencing conformance suite: one container holding the lease database and a
/// second, empty database used to prove that a unit on another database is refused. Tests run serially because the
/// blocking scenarios measure how long a call waits.
/// </summary>
[UsedImplicitly]
[CollectionDefinition(DisableParallelization = true)]
public sealed class PostgreSqlFencingFixture
    : HeadlessPostgreSqlFixture,
        ICollectionFixture<PostgreSqlFencingFixture>,
        ILeasesFixture
{
    private const string _OtherDatabase = "fencing_other";
    private const string _HandoffTable = "fencing_handoffs";

    public string ConnectionString => Container.GetConnectionString();

    public string OtherDatabaseConnectionString =>
        new NpgsqlConnectionStringBuilder(ConnectionString) { Database = _OtherDatabase }.ToString();

    protected override PostgreSqlBuilder Configure()
    {
        return base.Configure().WithDatabase("fencing_test").WithUsername("postgres").WithPassword("postgres");
    }

    protected override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(CancellationToken.None);

        // The container is reused across runs, so start from no lease table and let the initializer create it.
        await using (
            var reset = new NpgsqlCommand(
                $"""
                DROP SCHEMA IF EXISTS "{FencingStorageOptions.DefaultSchema}" CASCADE;
                DROP TABLE IF EXISTS {_HandoffTable};
                CREATE TABLE {_HandoffTable} (
                    id bigserial PRIMARY KEY,
                    tenant_id text NOT NULL,
                    kind text NOT NULL,
                    resource text NOT NULL,
                    generation bigint NOT NULL
                );
                """,
                connection
            )
        )
        {
            await reset.ExecuteNonQueryAsync(CancellationToken.None);
        }

        await using var exists = new NpgsqlCommand("SELECT 1 FROM pg_database WHERE datname = @name", connection);
        exists.Parameters.AddWithValue("name", _OtherDatabase);

        if (await exists.ExecuteScalarAsync(CancellationToken.None) is null)
        {
            await using var create = new NpgsqlCommand($"""CREATE DATABASE "{_OtherDatabase}";""", connection);
            await create.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }

    public void ConfigureProvider(HeadlessFencingSetupBuilder setup)
    {
        // Each host gets its own pool, and the contention tests run several hosts at once; the default pool of 100
        // per host would let them exceed the container's max_connections and fail with 53300 instead of contending.
        setup.UsePostgreSql(new NpgsqlConnectionStringBuilder(ConnectionString) { MaxPoolSize = 30 }.ToString());
    }

    public DbConnection CreateConnection()
    {
        return new NpgsqlConnection(ConnectionString);
    }

    public DbConnection CreateOtherDatabaseConnection()
    {
        return new NpgsqlConnection(OtherDatabaseConnectionString);
    }

    public ValueTask<IUnitOfWork> BeginOwnedAsync(
        IUnitOfWorkFactory factory,
        DbConnection connection,
        CancellationToken cancellationToken
    )
    {
        return factory.BeginAsync((NpgsqlConnection)connection, cancellationToken: cancellationToken);
    }

    public IUnitOfWork Enlist(IUnitOfWorkFactory factory, DbConnection connection, DbTransaction transaction)
    {
        return factory.Enlist((NpgsqlConnection)connection, (NpgsqlTransaction)transaction);
    }

    public Task<StoredLease?> ReadLeaseAsync(LeaseKey key, CancellationToken cancellationToken)
    {
        return ReadLeaseAsync(key, FencingStorageOptions.DefaultSchema, cancellationToken);
    }

    public async Task<StoredLease?> ReadLeaseAsync(LeaseKey key, string schema, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            $"""
            SELECT generation, state, granted_at, expires_at, ended_at FROM "{schema}".leases
            WHERE tenant_id = @tenant AND kind = @kind AND resource = @resource
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
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            $"""
            UPDATE "{FencingStorageOptions.DefaultSchema}".leases
            SET granted_at = granted_at - @by, expires_at = expires_at - @by, ended_at = ended_at - @by
            WHERE tenant_id = @tenant AND kind = @kind AND resource = @resource
            """,
            connection
        );
        _AddKey(command, key);
        command.Parameters.Add(new NpgsqlParameter<TimeSpan>("by", NpgsqlDbType.Interval) { TypedValue = by });

        (await command.ExecuteNonQueryAsync(cancellationToken)).Should().Be(1, "the lease row to age must exist");
    }

    public async Task WriteHandoffAsync(IUnitOfWork unit, ExpiredLease lease, CancellationToken cancellationToken)
    {
        var resource = (IRelationalUnitOfWorkResource)unit.Resource!;
        await using var command = new NpgsqlCommand(
            $"INSERT INTO {_HandoffTable} (tenant_id, kind, resource, generation) VALUES (@tenant, @kind, @resource, @generation)",
            (NpgsqlConnection)resource.Connection,
            (NpgsqlTransaction)resource.Transaction
        );
        command.Parameters.AddWithValue("tenant", lease.TenantId ?? "");
        command.Parameters.AddWithValue("kind", lease.Kind);
        command.Parameters.AddWithValue("resource", lease.Resource);
        command.Parameters.AddWithValue("generation", lease.Generation);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<LeaseHandoff>> ReadHandoffsAsync(string kind, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            $"SELECT tenant_id, resource, generation FROM {_HandoffTable} WHERE kind = @kind",
            connection
        );
        command.Parameters.AddWithValue("kind", kind);

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
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);

        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
    }

    public async Task ExecuteAsync(string sql, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void _AddKey(NpgsqlCommand command, LeaseKey key)
    {
        command.Parameters.AddWithValue("tenant", key.TenantId);
        command.Parameters.AddWithValue("kind", key.Kind);
        command.Parameters.AddWithValue("resource", key.Resource);
    }
}
