// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Idempotency;
using Headless.Testing.Testcontainers;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>
/// SQL Server fixture for the end-to-end idempotency middleware suite: the fixture's own database holds both the
/// fenced leases and the idempotency records. Tests run serially because the concurrency scenarios measure how long
/// a request blocks.
/// </summary>
[UsedImplicitly]
[CollectionDefinition(DisableParallelization = true)]
public sealed class ResponseReplaySqlServerFixture
    : HeadlessSqlServerFixture,
        ICollectionFixture<ResponseReplaySqlServerFixture>
{
    // Each host gets its own pool, and a couple of tests run several hosts at once; a bounded pool keeps a runaway
    // test from exhausting the container's worker threads instead of contending.
    private string PooledConnectionString =>
        new SqlConnectionStringBuilder(ConnectionString) { MaxPoolSize = 30 }.ToString();

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
