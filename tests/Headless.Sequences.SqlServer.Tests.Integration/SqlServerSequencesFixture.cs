// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Sequences;
using Headless.Sequences.SqlServer;
using Headless.Testing.Testcontainers;
using Headless.UnitOfWork;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>
/// SQL Server leaf fixture for the sequences conformance suite: one container holding the counter database and a
/// second, empty database used to prove that a unit on another database is refused. Tests run serially because the
/// blocking scenarios measure how long a call waits.
/// </summary>
[UsedImplicitly]
[CollectionDefinition(DisableParallelization = true)]
public sealed class SqlServerSequencesFixture
    : HeadlessSqlServerFixture,
        IAsyncLifetime,
        ICollectionFixture<SqlServerSequencesFixture>,
        ISequencesFixture
{
    private const string _CountersDatabase = "sequences_test";
    private const string _OtherDatabase = "sequences_other";

    /// <summary>Connection string of the database that holds the counters (the base one points at master).</summary>
    public string CountersConnectionString =>
        new SqlConnectionStringBuilder(ConnectionString) { InitialCatalog = _CountersDatabase }.ToString();

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

            foreach (var database in (string[])[_CountersDatabase, _OtherDatabase])
            {
                await using var create = new SqlCommand(
                    $"IF DB_ID(N'{database}') IS NULL CREATE DATABASE [{database}];",
                    master
                );
                await create.ExecuteNonQueryAsync(CancellationToken.None);
            }
        }

        // The container is reused across runs, so start from no counter table and let the initializer create it.
        await ExecuteAsync(
            DropSchemaSql(SqlServerSequencesOptions.DefaultSchema, SqlServerSequencesOptions.DefaultTableName),
            CancellationToken.None
        );
    }

    public void ConfigureUnitOfWork(IServiceCollection services)
    {
        services.AddSqlServerUnitOfWork();
    }

    public void ConfigureProvider(HeadlessSequencesSetupBuilder setup)
    {
        setup.UseSqlServer(CountersConnectionString);
    }

    public DbConnection CreateConnection()
    {
        return new SqlConnection(CountersConnectionString);
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

    public Task<long?> ReadValueAsync(SequenceKey key, CancellationToken cancellationToken)
    {
        return ReadValueAsync(
            key,
            SqlServerSequencesOptions.DefaultSchema,
            SqlServerSequencesOptions.DefaultTableName,
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
        await using var connection = new SqlConnection(CountersConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(
            $"""
            SELECT [value] FROM [{schema}].[{table}]
            WHERE [tenant_id] = @tenant AND [name] = @name AND [partition] = @partition
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
        await using var connection = new SqlConnection(CountersConnectionString);
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
        await using var connection = new SqlConnection(CountersConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>Drops <paramref name="table" /> and then its schema, when they exist.</summary>
    public static string DropSchemaSql(string schema, string table)
    {
        return $"""
            IF OBJECT_ID(N'{schema}.{table}', N'U') IS NOT NULL DROP TABLE [{schema}].[{table}];
            IF SCHEMA_ID(N'{schema}') IS NOT NULL EXEC(N'DROP SCHEMA [{schema}]');
            """;
    }
}
