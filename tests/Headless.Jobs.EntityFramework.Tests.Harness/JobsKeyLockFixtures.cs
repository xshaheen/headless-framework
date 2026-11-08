// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Testcontainers;

namespace Tests;

// The key-lock contention tests get a database of their own, so a Jobs test resetting its database never drops the
// tables a contention test is racing on. The server itself may be shared with other fixtures and runs.
[CollectionDefinition(DisableParallelization = true)]
public sealed class JobsKeyLockPostgreSqlFixture
    : HeadlessPostgreSqlFixture,
        ICollectionFixture<JobsKeyLockPostgreSqlFixture>;

[CollectionDefinition(DisableParallelization = true)]
public sealed class JobsKeyLockSqlServerFixture
    : HeadlessSqlServerFixture,
        ICollectionFixture<JobsKeyLockSqlServerFixture>;
