// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Fencing;
using Headless.Idempotency;
using Headless.Testing.Testcontainers;
using Headless.UnitOfWork;
using Npgsql;
using NpgsqlTypes;
using Testcontainers.PostgreSql;

namespace Tests;

/// <summary>
/// PostgreSQL leaf fixture for the idempotency conformance suite: one container whose database holds both the fenced
/// leases and the idempotency records. Tests run serially because the blocking scenarios measure how long a call
/// waits.
/// </summary>
[UsedImplicitly]
[CollectionDefinition(DisableParallelization = true)]
public sealed class PostgreSqlIdempotencyFixture
    : HeadlessPostgreSqlFixture,
        ICollectionFixture<PostgreSqlIdempotencyFixture>,
        IIdempotencyFixture
{
    private const string _Records = $"\"{IdempotencyStorageOptions.DefaultSchema}\".records";
    private const string _Leases = $"\"{FencingStorageOptions.DefaultSchema}\".leases";

    public string ConnectionString => Container.GetConnectionString();

    // Each host gets its own pool, and the contention tests run several hosts at once; the default pool of 100 per
    // host would let them exceed the container's max_connections and fail with 53300 instead of contending.
    private string PooledConnectionString =>
        new NpgsqlConnectionStringBuilder(ConnectionString) { MaxPoolSize = 30 }.ToString();

    protected override PostgreSqlBuilder Configure()
    {
        return base.Configure().WithDatabase("idempotency_test").WithUsername("postgres").WithPassword("postgres");
    }

    protected override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(CancellationToken.None);

        // The container is reused across runs, so start from no storage and let the initializers create it.
        await using var reset = new NpgsqlCommand(
            $"""
            DROP SCHEMA IF EXISTS "{IdempotencyStorageOptions.DefaultSchema}" CASCADE;
            DROP SCHEMA IF EXISTS "{FencingStorageOptions.DefaultSchema}" CASCADE;
            """,
            connection
        );
        await reset.ExecuteNonQueryAsync(CancellationToken.None);
    }

    public void ConfigureFencing(HeadlessFencingSetupBuilder setup)
    {
        setup.UsePostgreSql(PooledConnectionString);
    }

    public void ConfigureIdempotency(HeadlessIdempotencySetupBuilder setup)
    {
        setup.UsePostgreSql(PooledConnectionString);
    }

    public DbConnection CreateConnection()
    {
        return new NpgsqlConnection(ConnectionString);
    }

    public ValueTask<IUnitOfWork> BeginOwnedAsync(
        IUnitOfWorkFactory factory,
        DbConnection connection,
        CancellationToken cancellationToken
    )
    {
        return factory.BeginAsync((NpgsqlConnection)connection, cancellationToken: cancellationToken);
    }

    public async Task<StoredRecord?> ReadRecordAsync(IdempotencyRecordKey key, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            $"""
            SELECT status, fingerprint_algorithm, fingerprint, lease_generation, result, result_contract, retention_until
            FROM {_Records}
            WHERE tenant_id = @tenant AND idempotency_key = @recordKey
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
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            $"""
            UPDATE {_Records} SET retention_until = retention_until - @age
            WHERE tenant_id = @tenant AND idempotency_key = @recordKey
            """,
            connection
        );
        _AddKey(command, key);
        command.Parameters.Add(new NpgsqlParameter<TimeSpan>("age", NpgsqlDbType.Interval) { TypedValue = by });

        (await command.ExecuteNonQueryAsync(cancellationToken)).Should().Be(1, "the record to age must exist");
    }

    public async Task<StoredLeaseRow?> ReadLeaseAsync(IdempotencyRecordKey key, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            $"""
            SELECT generation, state, expires_at FROM {_Leases}
            WHERE tenant_id = @tenant AND kind = @kind AND resource = @recordKey
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
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            $"""
            UPDATE {_Leases}
            SET granted_at = granted_at - @age, expires_at = expires_at - @age, ended_at = ended_at - @age
            WHERE tenant_id = @tenant AND kind = @kind AND resource = @recordKey
            """,
            connection
        );
        _AddLeaseKey(command, key);
        command.Parameters.Add(new NpgsqlParameter<TimeSpan>("age", NpgsqlDbType.Interval) { TypedValue = by });

        (await command.ExecuteNonQueryAsync(cancellationToken)).Should().Be(1, "the lease to age must exist");
    }

    private static void _AddKey(NpgsqlCommand command, IdempotencyRecordKey key)
    {
        command.Parameters.AddWithValue("tenant", key.TenantId);
        command.Parameters.AddWithValue("recordKey", key.Key);
    }

    private static void _AddLeaseKey(NpgsqlCommand command, IdempotencyRecordKey key)
    {
        _AddKey(command, key);
        command.Parameters.AddWithValue("kind", IdempotentAdmission.LeaseKind);
    }
}
