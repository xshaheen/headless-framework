// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Hosting.Initialization;
using Headless.Idempotency;
using Headless.Sql;
using Headless.Sql.Sqlite;
using Headless.UnitOfWork;
using Microsoft.Data.Sqlite;

namespace Tests;

/// <summary>
/// SQLite leaf fixture for the idempotency conformance suite: one database file holding the idempotency records. Tests
/// run serially because the blocking scenarios measure how long a call waits.
/// </summary>
[UsedImplicitly]
[CollectionDefinition(DisableParallelization = true)]
public sealed class SqliteIdempotencyFixture
    : ICollectionFixture<SqliteIdempotencyFixture>,
        IIdempotencyFixture,
        IAsyncLifetime
{
    private static readonly SqliteDialect _Dialect = SqliteDialect.Instance;
    private static readonly string _Records = _Dialect.Qualify(HeadlessStorageDefaults.Schema, "idempotency_records");
    private readonly SqliteTestDatabase _database = SqliteTestDatabase.Create();

    public string ConnectionString => _database.ConnectionString;

    public bool SupportsEnlistedAdmission => false;

    public ValueTask InitializeAsync()
    {
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        return _database.DisposeAsync();
    }

    public void ConfigureIdempotency(HeadlessIdempotencySetupBuilder setup)
    {
        setup.UseSqlite(ConnectionString);
    }

    public DbConnection CreateConnection()
    {
        return new SqliteConnection(ConnectionString);
    }

    public ValueTask<IUnitOfWork> BeginOwnedAsync(
        IUnitOfWorkFactory factory,
        DbConnection connection,
        CancellationToken cancellationToken
    )
    {
        return factory.BeginAsync((SqliteConnection)connection, cancellationToken: cancellationToken);
    }

    public async Task<StoredRecord?> ReadRecordAsync(IdempotencyRecordKey key, CancellationToken cancellationToken)
    {
        await using var connection = await _OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT status, fingerprint_algorithm, fingerprint, generation, lease_expires_at, result, result_contract,
                retention_until, recovery_point, recovery_state, recovery_contract
            FROM {_Records}
            WHERE tenant_id = @tenant AND idempotency_key = @recordKey
            """;
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
                : await reader.GetFieldValueAsync<DateTimeOffset>(4, cancellationToken),
            await reader.IsDBNullAsync(5, cancellationToken)
                ? null
                : await reader.GetFieldValueAsync<byte[]>(5, cancellationToken),
            await reader.IsDBNullAsync(6, cancellationToken) ? null : reader.GetString(6),
            await reader.GetFieldValueAsync<DateTimeOffset>(7, cancellationToken),
            await reader.IsDBNullAsync(8, cancellationToken) ? null : reader.GetString(8),
            await reader.IsDBNullAsync(9, cancellationToken)
                ? null
                : await reader.GetFieldValueAsync<byte[]>(9, cancellationToken),
            await reader.IsDBNullAsync(10, cancellationToken) ? null : reader.GetString(10)
        );
    }

    public async Task ShiftRecordIntoPastAsync(
        IdempotencyRecordKey key,
        TimeSpan by,
        CancellationToken cancellationToken
    )
    {
        (await _ShiftAsync("retention_until", key, by, cancellationToken))
            .Should()
            .Be(1, "the record to age must exist");
    }

    public async Task ShiftLeaseIntoPastAsync(
        IdempotencyRecordKey key,
        TimeSpan by,
        CancellationToken cancellationToken
    )
    {
        (await _ShiftAsync("lease_expires_at", key, by, cancellationToken))
            .Should()
            .Be(1, "the lease to age must exist");
    }

    public async Task TouchAsync(IUnitOfWork unit, CancellationToken cancellationToken)
    {
        var resource = (IRelationalUnitOfWorkResource)unit.Resource!;
        await using var command = (SqliteCommand)resource.Connection.CreateCommand();
        command.Transaction = (SqliteTransaction)resource.Transaction;
        command.CommandText = "SELECT 1;";
        await command.ExecuteScalarAsync(cancellationToken);
    }

    private async Task<int> _ShiftAsync(
        string column,
        IdempotencyRecordKey key,
        TimeSpan by,
        CancellationToken cancellationToken
    )
    {
        await using var connection = await _OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            UPDATE {_Records} SET {column} = {_Dialect.ShiftByDuration(column, "age", subtract: true)}
            WHERE tenant_id = @tenant AND idempotency_key = @recordKey AND {column} IS NOT NULL
            """;
        _AddKey(command, key);
        _Dialect.AddDuration(command, "age", by);

        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<SqliteConnection> _OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);

        return connection;
    }

    private static void _AddKey(SqliteCommand command, IdempotencyRecordKey key)
    {
        _Dialect.AddParameter(command, "tenant", SqlColumnType.KeyText(0), key.TenantId);
        _Dialect.AddParameter(command, "recordKey", SqlColumnType.KeyText(0), key.Key);
    }
}
