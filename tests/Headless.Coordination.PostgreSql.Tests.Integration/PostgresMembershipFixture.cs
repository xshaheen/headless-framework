// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Coordination.PostgreSql;
using Headless.Testing.Testcontainers;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace Tests;

[UsedImplicitly]
[CollectionDefinition(DisableParallelization = true)]
public sealed class PostgresMembershipFixture : HeadlessPostgreSqlFixture, ICollectionFixture<PostgresMembershipFixture>
    , ICoordinationFixture
{
    public string ConnectionString => Container.GetConnectionString();

    protected override PostgreSqlBuilder Configure()
    {
        return base.Configure().WithDatabase("coordination_test").WithUsername("postgres").WithPassword("postgres");
    }

    public void ConfigureProvider(IServiceCollection services)
    {
        services.AddPostgresCoordination(options => options.ConnectionString = ConnectionString);
    }
}
