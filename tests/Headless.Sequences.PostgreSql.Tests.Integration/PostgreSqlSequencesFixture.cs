// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Sequences;
using Headless.Sequences.PostgreSql;
using Headless.Testing.Testcontainers;
using Headless.UnitOfWork;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Tests;

/// <summary>
/// PostgreSQL leaf fixture for the sequences conformance suite: one container holding the counter database and a
/// second, empty database used to prove that a unit on another database is refused. Tests run serially because the
/// blocking scenarios measure how long a call waits.
/// </summary>
[UsedImplicitly]
[CollectionDefinition(DisableParallelization = true)]
public sealed class PostgreSqlSequencesFixture
    : HeadlessPostgreSqlFixture,
        ICollectionFixture<PostgreSqlSequencesFixture>,
        ISequencesFixture
{
    private const string _OtherDatabase = "sequences_other";

    public string ConnectionString => Container.GetConnectionString();

    public string OtherDatabaseConnectionString =>
        new NpgsqlConnectionStringBuilder(ConnectionString) { Database = _OtherDatabase }.ToString();

    protected override PostgreSqlBuilder Configure()
    {
        return base.Configure().WithDatabase("sequences_test").WithUsername("postgres").WithPassword("postgres");
    }

    protected override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(CancellationToken.None);

        // The container is reused across runs, so start from no counter table and let the initializer create it.
        await using (
            var drop = new NpgsqlCommand(
                $"""DROP SCHEMA IF EXISTS "{PostgreSqlSequencesOptions.DefaultSchema}" CASCADE;""",
                connection
            )
        )
        {
            await drop.ExecuteNonQueryAsync(CancellationToken.None);
        }

        await using var exists = new NpgsqlCommand("SELECT 1 FROM pg_database WHERE datname = @name", connection);
        exists.Parameters.AddWithValue("name", _OtherDatabase);

        if (await exists.ExecuteScalarAsync(CancellationToken.None) is null)
        {
            await using var create = new NpgsqlCommand($"""CREATE DATABASE "{_OtherDatabase}";""", connection);
            await create.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }

    public void ConfigureUnitOfWork(IServiceCollection services)
    {
        services.AddPostgreSqlUnitOfWork();
    }

    public void ConfigureProvider(HeadlessSequencesSetupBuilder setup)
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

    public Task<long?> ReadValueAsync(SequenceKey key, CancellationToken cancellationToken)
    {
        return ReadValueAsync(
            key,
            PostgreSqlSequencesOptions.DefaultSchema,
            PostgreSqlSequencesOptions.DefaultTableName,
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
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            $"""
            SELECT value FROM "{schema}"."{table}"
            WHERE tenant_id = @tenant AND name = @name AND "partition" = @partition
            """,
            connection
        );
        command.Parameters.AddWithValue("tenant", key.TenantId);
        command.Parameters.AddWithValue("name", key.Name);
        command.Parameters.AddWithValue("partition", key.Partition);

        return await command.ExecuteScalarAsync(cancellationToken) is long value ? value : null;
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
}
