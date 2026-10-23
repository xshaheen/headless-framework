// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Testcontainers;

namespace Tests;

/// <summary>SQL Server container shared by the DistributedLocks integration tests.</summary>
[UsedImplicitly]
[CollectionDefinition(DisableParallelization = true)]
public sealed class SqlServerDistributedLockFixture
    : HeadlessSqlServerFixture,
        ICollectionFixture<SqlServerDistributedLockFixture>;
