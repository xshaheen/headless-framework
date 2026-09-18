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
        // The membership tables now live in the feature-owned schema, so drop the schema itself rather than
        // three names that would resolve through search_path to whatever the old default was.
        await using var command = new NpgsqlCommand(
            $"""DROP SCHEMA IF EXISTS "{CoordinationStorageOptions.DefaultSchema}" CASCADE;""",
            connection
        );

        await command.ExecuteNonQueryAsync(CancellationToken.None);
    }

    public void ConfigureProvider(IServiceCollection services, HeadlessCoordinationSetupBuilder setup)
    {
        setup.UsePostgreSql(options => options.ConnectionString = ConnectionString);
    }
}
