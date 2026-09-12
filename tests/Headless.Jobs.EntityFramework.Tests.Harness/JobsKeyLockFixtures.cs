// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Testcontainers;

namespace Tests;

// Reuse is scoped by the fixture's assembly. Keep these low-level lock fixtures in the harness assembly
// so Jobs integration workers cannot reset their database or stop their container during a contention test.
[CollectionDefinition(DisableParallelization = true)]
public sealed class JobsKeyLockPostgreSqlFixture
    : HeadlessPostgreSqlFixture,
        ICollectionFixture<JobsKeyLockPostgreSqlFixture>
{
    public string ConnectionString => Container.GetConnectionString();
}

[CollectionDefinition(DisableParallelization = true)]
public sealed class JobsKeyLockSqlServerFixture
    : HeadlessSqlServerFixture,
        ICollectionFixture<JobsKeyLockSqlServerFixture>;
