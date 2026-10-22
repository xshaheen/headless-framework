// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Idempotency;
using Headless.Testing.Testcontainers;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Tests;

/// <summary>
/// PostgreSQL fixture for the end-to-end idempotency middleware suite: one container whose database holds both the
/// fenced leases and the idempotency records. Tests run serially because the concurrency scenarios measure how long
/// a request blocks.
/// </summary>
[UsedImplicitly]
[CollectionDefinition(DisableParallelization = true)]
public sealed class ResponseReplayPostgreSqlFixture
    : HeadlessPostgreSqlFixture,
        ICollectionFixture<ResponseReplayPostgreSqlFixture>
{
    public string ConnectionString => Container.GetConnectionString();

    // Each host gets its own pool, and a couple of tests run several hosts at once; the default pool of 100 per host
    // would let them exceed the container's max_connections and fail with 53300 instead of contending.
    private string PooledConnectionString =>
        new NpgsqlConnectionStringBuilder(ConnectionString) { MaxPoolSize = 30 }.ToString();

    protected override PostgreSqlBuilder Configure()
    {
        return base.Configure().WithDatabase("api_idempotency_test").WithUsername("postgres").WithPassword("postgres");
    }

    protected override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
    }

    /// <summary>Registers idempotency on this fixture's database.</summary>
    public void ConfigureStore(IServiceCollection services)
    {
        services.AddHeadlessIdempotency(setup =>
        {
            setup.UsePostgreSql(PooledConnectionString);
            // The retention purge only matters over hours; leaving it on would just add an idle background loop.
            setup.ConfigureOptions(options => options.PurgeInterval = null);
        });
    }
}
