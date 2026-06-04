// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Testcontainers;

namespace Tests;

[UsedImplicitly]
[CollectionDefinition(DisableParallelization = true)]
public sealed class SqlServerDistributedLockFixture
    : HeadlessSqlServerFixture,
        ICollectionFixture<SqlServerDistributedLockFixture>;
