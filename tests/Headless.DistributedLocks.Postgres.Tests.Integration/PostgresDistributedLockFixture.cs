// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Testcontainers;
using Testcontainers.PostgreSql;

namespace Tests;

[UsedImplicitly]
[CollectionDefinition(DisableParallelization = true)]
public sealed class PostgresDistributedLockFixture
    : HeadlessPostgreSqlFixture,
        ICollectionFixture<PostgresDistributedLockFixture>
{
    public string ConnectionString => Container.GetConnectionString();

    protected override PostgreSqlBuilder Configure()
    {
        return base.Configure()
            .WithDatabase("distributed_locks_test")
            .WithUsername("postgres")
            .WithPassword("postgres");
    }
}
