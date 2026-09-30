// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Coordination;
using Headless.Testing.Testcontainers;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

[UsedImplicitly]
[CollectionDefinition(DisableParallelization = true)]
public sealed class SqlServerMembershipFixture
    : HeadlessSqlServerFixture,
        IAsyncLifetime,
        ICollectionFixture<SqlServerMembershipFixture>,
        ICoordinationFixture
{
    // Re-implemented rather than overridden: the base fixture's InitializeAsync is not virtual. The container is reused
    // across runs, and the schema runner trusts its history, so a run that dropped the tables but not the history would
    // leave the next run believing the tables exist; start every run from neither.
    public new async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(CancellationToken.None);
        await using var reset = new SqlCommand(
            """
            DROP TABLE IF EXISTS headless.CoordinationLiveness;
            DROP TABLE IF EXISTS headless.CoordinationDescriptor;
            DROP TABLE IF EXISTS headless.CoordinationNodeGeneration;
            DROP TABLE IF EXISTS headless.headless_schema_history;
            """,
            connection
        );
        await reset.ExecuteNonQueryAsync(CancellationToken.None);
    }

    public void ConfigureProvider(IServiceCollection services, HeadlessCoordinationSetupBuilder setup)
    {
        setup.UseSqlServer(options => options.ConnectionString = ConnectionString);
    }
}
