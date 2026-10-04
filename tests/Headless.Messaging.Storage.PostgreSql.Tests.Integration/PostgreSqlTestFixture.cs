// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;
using Headless.Testing.Testcontainers;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Tests;

/// <summary>
/// Collection fixture providing a PostgreSQL container for integration tests.
/// </summary>
/// <remarks>
/// The container is reused across runs of this checkout, so it starts by dropping the default messaging schema, history
/// included. Tests that apply the schema without dropping it first would otherwise meet whatever an older run left.
/// </remarks>
[UsedImplicitly]
[CollectionDefinition(DisableParallelization = true)]
public sealed class PostgreSqlTestFixture
    : HeadlessPostgreSqlFixture,
        ICollectionFixture<PostgreSqlTestFixture>,
        IAsyncLifetime
{
    /// <summary>Gets the PostgreSQL connection string.</summary>
    public string ConnectionString => Container.GetConnectionString();

    protected override PostgreSqlBuilder Configure()
    {
        return base.Configure().WithDatabase("messages_test").WithUsername("postgres").WithPassword("postgres");
    }

    async ValueTask IAsyncLifetime.InitializeAsync()
    {
        await InitializeAsync();
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            $"""DROP SCHEMA IF EXISTS "{HeadlessStorageDefaults.Schema}" CASCADE;""",
            connection
        );
        await command.ExecuteNonQueryAsync();
    }
}
