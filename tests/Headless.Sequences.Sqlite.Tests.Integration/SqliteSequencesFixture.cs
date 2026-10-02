// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Hosting.Initialization;
using Headless.Sequences;
using Headless.Sequences.Sqlite;
using Headless.Sql.Sqlite;
using Headless.UnitOfWork;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>
/// SQLite leaf fixture for the sequences conformance suite: a database file holding the counters and a second, empty
/// one used to prove that a unit on another database is refused. Tests run serially because the blocking scenarios
/// measure how long a call waits.
/// </summary>
[UsedImplicitly]
[CollectionDefinition(DisableParallelization = true)]
public sealed class SqliteSequencesFixture
    : ICollectionFixture<SqliteSequencesFixture>,
        ISequencesFixture,
        IAsyncLifetime
{
    private readonly SqliteTestDatabase _database = SqliteTestDatabase.Create();
    private readonly SqliteTestDatabase _other = SqliteTestDatabase.Create();

    public string ConnectionString => _database.ConnectionString;

    public ValueTask InitializeAsync()
    {
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await _database.DisposeAsync();
        await _other.DisposeAsync();
    }

    public void ConfigureUnitOfWork(IServiceCollection services)
    {
        services.AddSqliteUnitOfWork();
    }

    public void ConfigureProvider(HeadlessSequencesSetupBuilder setup)
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

    public Task<long?> ReadValueAsync(SequenceKey key, CancellationToken cancellationToken)
    {
        return ReadValueAsync(
            key,
            HeadlessStorageDefaults.Schema,
            SqliteSequencesOptions.DefaultTableName,
            cancellationToken
        );
    }

    public async Task<long?> ReadValueAsync(
        SequenceKey key,
        string schema,
        string table,
        CancellationToken cancellationToken
    )
    {
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT value FROM {SqliteDialect.Instance.Qualify(schema, table)}
            WHERE tenant_id = @tenant AND name = @name AND "partition" = @partition
            """;
        command.Parameters.AddWithValue("tenant", key.TenantId);
        command.Parameters.AddWithValue("name", key.Name);
        command.Parameters.AddWithValue("partition", key.Partition);

        return await command.ExecuteScalarAsync(cancellationToken) is long value ? value : null;
    }

    public async Task<long> ScalarAsync(string sql, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        return (long)(await command.ExecuteScalarAsync(cancellationToken))!;
    }
}
