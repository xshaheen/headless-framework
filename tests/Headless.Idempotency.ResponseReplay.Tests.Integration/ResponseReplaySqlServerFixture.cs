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
public sealed class ResponseReplaySqlServerFixture
    : HeadlessSqlServerFixture,
        ICollectionFixture<ResponseReplaySqlServerFixture>
{
    private const string _Database = "api_idempotency_test";

    /// <summary>Connection string of the shared database (the base one points at master).</summary>
    private string DatabaseConnectionString =>
        new SqlConnectionStringBuilder(ConnectionString) { InitialCatalog = _Database }.ToString();

    // Each host gets its own pool, and a couple of tests run several hosts at once; a bounded pool keeps a runaway
    // test from exhausting the container's worker threads instead of contending.
    private string PooledConnectionString =>
        new SqlConnectionStringBuilder(DatabaseConnectionString) { MaxPoolSize = 30 }.ToString();

    // The database can only be created once the container accepts logins.
    protected override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();

        await using var master = new SqlConnection(ConnectionString);
        await master.OpenAsync(CancellationToken.None);

        await using var create = new SqlCommand(
            $"IF DB_ID(N'{_Database}') IS NULL CREATE DATABASE [{_Database}];",
            master
        );
        await create.ExecuteNonQueryAsync(CancellationToken.None);
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
