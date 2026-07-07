// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Coordination;
using Headless.Testing.Testcontainers;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Tests;

[UsedImplicitly]
[CollectionDefinition(DisableParallelization = true)]
public sealed class PostgreSqlMembershipFixture
    : HeadlessPostgreSqlFixture,
        ICollectionFixture<PostgreSqlMembershipFixture>,
        ICoordinationFixture
{
    public string ConnectionString => Container.GetConnectionString();

    protected override PostgreSqlBuilder Configure()
    {
        return base.Configure().WithDatabase("coordination_test").WithUsername("postgres").WithPassword("postgres");
    }

    protected override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(CancellationToken.None);
        await using var command = new NpgsqlCommand(
            "DROP TABLE IF EXISTS coordination_liveness, coordination_descriptor, coordination_node_generation CASCADE;",
            connection
        );

        await command.ExecuteNonQueryAsync(CancellationToken.None);
    }

    public void ConfigureProvider(IServiceCollection services, HeadlessCoordinationSetupBuilder setup)
    {
        setup.UsePostgreSql(options => options.ConnectionString = ConnectionString);
    }
}
