// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Idempotency;
using Headless.Testing.Testcontainers;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>
/// SQL Server fixture for the end-to-end idempotency middleware suite: one container whose database holds both the
/// fenced leases and the idempotency records. Tests run serially because the concurrency scenarios measure how long
/// a request blocks.
/// </summary>
[UsedImplicitly]
[CollectionDefinition(DisableParallelization = true)]
public sealed class ApiIdempotencySqlServerFixture
    : HeadlessSqlServerFixture,
        IAsyncLifetime,
        ICollectionFixture<ApiIdempotencySqlServerFixture>
{
    private const string _Database = "api_idempotency_test";

    /// <summary>Connection string of the shared database (the base one points at master).</summary>
    private string DatabaseConnectionString =>
        new SqlConnectionStringBuilder(ConnectionString) { InitialCatalog = _Database }.ToString();

    // Each host gets its own pool, and a couple of tests run several hosts at once; a bounded pool keeps a runaway
    // test from exhausting the container's worker threads instead of contending.
    private string PooledConnectionString =>
        new SqlConnectionStringBuilder(DatabaseConnectionString) { MaxPoolSize = 30 }.ToString();

    // Re-implemented rather than overridden: the base fixture's InitializeAsync is not virtual, and the database can
    // only be created once its container accepts logins.
    public new async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();

        await using (var master = new SqlConnection(ConnectionString))
        {
            await master.OpenAsync(CancellationToken.None);

            await using var create = new SqlCommand(
                $"IF DB_ID(N'{_Database}') IS NULL CREATE DATABASE [{_Database}];",
                master
            );
            await create.ExecuteNonQueryAsync(CancellationToken.None);
        }

        // The container is reused across runs, so start from no storage and let the initializers create it.
        await using var connection = new SqlConnection(DatabaseConnectionString);
        await connection.OpenAsync(CancellationToken.None);
        await using var reset = new SqlCommand(
            $"""
            IF OBJECT_ID(N'{IdempotencyStorageOptions.DefaultSchema}.records', N'U') IS NOT NULL DROP TABLE [{IdempotencyStorageOptions.DefaultSchema}].[records];
            IF OBJECT_ID(N'{IdempotencyStorageOptions.DefaultSchema}.record_generations', N'SO') IS NOT NULL DROP SEQUENCE [{IdempotencyStorageOptions.DefaultSchema}].[record_generations];
            IF SCHEMA_ID(N'{IdempotencyStorageOptions.DefaultSchema}') IS NOT NULL EXEC(N'DROP SCHEMA [{IdempotencyStorageOptions.DefaultSchema}]');
            """,
            connection
        );
        await reset.ExecuteNonQueryAsync(CancellationToken.None);
    }

    /// <summary>Registers idempotency on this fixture's database.</summary>
    public void ConfigureStore(IServiceCollection services)
    {
        services.AddHeadlessIdempotency(setup =>
        {
            setup.UseSqlServer(PooledConnectionString);
            // The retention purge only matters over hours; leaving it on would just add an idle background loop.
            setup.ConfigureOptions(options => options.PurgeInterval = null);
        });
    }
}
