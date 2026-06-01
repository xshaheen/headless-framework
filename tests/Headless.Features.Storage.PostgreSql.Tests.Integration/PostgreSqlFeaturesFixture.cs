// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Testcontainers;
using Testcontainers.PostgreSql;

namespace Tests;

[UsedImplicitly]
[CollectionDefinition(DisableParallelization = true)]
public sealed class PostgreSqlFeaturesFixture
    : HeadlessPostgreSqlFixture,
        ICollectionFixture<PostgreSqlFeaturesFixture>
{
    public string ConnectionString => Container.GetConnectionString();

    protected override PostgreSqlBuilder Configure()
    {
        return base.Configure().WithDatabase("features_storage_test").WithUsername("postgres").WithPassword("postgres");
    }
}
