// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Hosting;
using Headless.Sequences;
using Headless.Sequences.SqlServer;
using Headless.Testing.Testcontainers;
using Headless.UnitOfWork;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>
/// SQL Server leaf fixture for the sequences conformance suite: the fixture's own database holds the counters, and a
/// second, empty database derived from it proves that a unit on another database is refused. Tests run serially
/// because the blocking scenarios measure how long a call waits.
/// </summary>
[UsedImplicitly]
[CollectionDefinition(DisableParallelization = true)]
public sealed class SqlServerSequencesFixture
    : HeadlessSqlServerFixture,
        ICollectionFixture<SqlServerSequencesFixture>,
        ISequencesFixture
{
    /// <summary>Connection string of the database that holds the counters: the fixture's own database.</summary>
    public string CountersConnectionString => ConnectionString;

    public string OtherDatabaseConnectionString => ConnectionStringFor(DerivedDatabaseName("other"));

    // The database can only be created once the container accepts logins.
    protected override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await CreateDatabaseAsync(DerivedDatabaseName("other"), CancellationToken.None);
    }

    public void ConfigureUnitOfWork(IServiceCollection services)
    {
        services.AddUnitOfWork();
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
        return factory.BeginAsync((SqlConnection)connection, cancellationToken);
    }

    public IUnitOfWork Enlist(IUnitOfWorkFactory factory, DbConnection connection, DbTransaction transaction)
    {
        return factory.Enlist((SqlConnection)connection, (SqlTransaction)transaction);
    }

    public Task<long?> ReadValueAsync(SequenceKey key, CancellationToken cancellationToken)
    {
        return ReadValueAsync(
            key,
            HeadlessStorageDefaults.Schema,
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
            SELECT [Value] FROM [{schema}].[{table}]
            WHERE [TenantId] = @tenant AND [Name] = @name AND [Partition] = @partition
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

    /// <summary>
    /// Drops <paramref name="table" />, the schema runner's history, and then the schema, when they exist. The history
    /// goes too: the runner trusts it, so a history that outlived its table would stop the table being recreated.
    /// </summary>
    public static string DropSchemaSql(string schema, string table)
    {
        return $"""
            IF OBJECT_ID(N'{schema}.{table}', N'U') IS NOT NULL DROP TABLE [{schema}].[{table}];
            IF OBJECT_ID(N'{schema}.headless_schema_history', N'U') IS NOT NULL DROP TABLE [{schema}].[headless_schema_history];
            IF SCHEMA_ID(N'{schema}') IS NOT NULL EXEC(N'DROP SCHEMA [{schema}]');
            """;
    }
}
