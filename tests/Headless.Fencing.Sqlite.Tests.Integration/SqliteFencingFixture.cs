// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Fencing;
using Headless.Hosting.Initialization;
using Headless.Sql;
using Headless.Sql.Sqlite;
using Headless.UnitOfWork;
using Microsoft.Data.Sqlite;

namespace Tests;

/// <summary>
/// SQLite leaf fixture for the fencing conformance suite: a database file holding the leases and a second, empty one
/// used to prove that a unit on another database is refused. Tests run serially because the blocking scenarios measure
/// how long a call waits.
/// </summary>
[UsedImplicitly]
[CollectionDefinition(DisableParallelization = true)]
public sealed class SqliteFencingFixture : ICollectionFixture<SqliteFencingFixture>, ILeasesFixture, IAsyncLifetime
{
    private const string _HandoffTable = "fencing_handoffs";
    private static readonly SqliteDialect _Dialect = SqliteDialect.Instance;
    private static readonly string _Leases = _Dialect.Qualify(HeadlessStorageDefaults.Schema, "fencing_leases");
    private readonly SqliteTestDatabase _database = SqliteTestDatabase.Create();
    private readonly SqliteTestDatabase _other = SqliteTestDatabase.Create();

    public string ConnectionString => _database.ConnectionString;

    public bool SupportsEnlistedGrant => false;

    public async ValueTask InitializeAsync()
    {
        await using var connection = await _OpenAsync(CancellationToken.None);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            CREATE TABLE {_HandoffTable} (
                id INTEGER PRIMARY KEY,
                tenant_id TEXT NOT NULL,
                kind TEXT NOT NULL,
                resource TEXT NOT NULL,
                generation INTEGER NOT NULL
            );
            """;
        await command.ExecuteNonQueryAsync(CancellationToken.None);
    }

    public async ValueTask DisposeAsync()
    {
        await _database.DisposeAsync();
        await _other.DisposeAsync();
    }

    public void ConfigureProvider(HeadlessFencingSetupBuilder setup)
    {
        setup.UseSqlite(ConnectionString);
    }

    public DbConnection CreateConnection()
    {
        return new SqliteConnection(ConnectionString);
    }

    public DbConnection CreateOtherDatabaseConnection()
    {
        return new SqliteConnection(_other.ConnectionString);
    }

    public ValueTask<IUnitOfWork> BeginOwnedAsync(
        IUnitOfWorkFactory factory,
        DbConnection connection,
        CancellationToken cancellationToken
    )
    {
        return factory.BeginAsync((SqliteConnection)connection, cancellationToken: cancellationToken);
    }

    public IUnitOfWork Enlist(IUnitOfWorkFactory factory, DbConnection connection, DbTransaction transaction)
    {
        return factory.Enlist((SqliteConnection)connection, (SqliteTransaction)transaction);
    }

    public async Task<StoredLease?> ReadLeaseAsync(LeaseKey key, CancellationToken cancellationToken)
    {
        await using var connection = await _OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT generation, state, granted_at, expires_at, ended_at FROM {_Leases}
            WHERE tenant_id = @tenant AND kind = @kind AND resource = @resource
            """;
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
        static string back(string column) => _Dialect.ShiftByDuration(column, "by", subtract: true);

        await using var connection = await _OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            UPDATE {_Leases}
            SET granted_at = {back("granted_at")}, expires_at = {back("expires_at")}, ended_at = {back("ended_at")}
            WHERE tenant_id = @tenant AND kind = @kind AND resource = @resource
            """;
        _AddKey(command, key);
        _Dialect.AddDuration(command, "by", by);

        (await command.ExecuteNonQueryAsync(cancellationToken)).Should().Be(1, "the lease row to age must exist");
    }

    public async Task WriteHandoffAsync(IUnitOfWork unit, ExpiredLease lease, CancellationToken cancellationToken)
    {
        var resource = (IRelationalUnitOfWorkResource)unit.Resource!;
        await using var command = (SqliteCommand)resource.Connection.CreateCommand();
        command.Transaction = (SqliteTransaction)resource.Transaction;
        command.CommandText =
            $"INSERT INTO {_HandoffTable} (tenant_id, kind, resource, generation) VALUES (@tenant, @kind, @resource, @generation)";
        command.Parameters.AddWithValue("tenant", lease.TenantId ?? "");
        command.Parameters.AddWithValue("kind", lease.Kind);
        command.Parameters.AddWithValue("resource", lease.Resource);
        command.Parameters.AddWithValue("generation", lease.Generation);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<LeaseHandoff>> ReadHandoffsAsync(string kind, CancellationToken cancellationToken)
    {
        await using var connection = await _OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT tenant_id, resource, generation FROM {_HandoffTable} WHERE kind = @kind";
        command.Parameters.AddWithValue(nameof(kind), kind);

        var handoffs = new List<LeaseHandoff>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            handoffs.Add(new LeaseHandoff(reader.GetString(0), reader.GetString(1), reader.GetInt64(2)));
        }

        return handoffs;
    }

    private async Task<SqliteConnection> _OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);

        return connection;
    }

    private static void _AddKey(SqliteCommand command, LeaseKey key)
    {
        _Dialect.AddParameter(command, "tenant", SqlColumnType.KeyText(0), key.TenantId);
        _Dialect.AddParameter(command, "kind", SqlColumnType.KeyText(0), key.Kind);
        _Dialect.AddParameter(command, "resource", SqlColumnType.KeyText(0), key.Resource);
    }
}
