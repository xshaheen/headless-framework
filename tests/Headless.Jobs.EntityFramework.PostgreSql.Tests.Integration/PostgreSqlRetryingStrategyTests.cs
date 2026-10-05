// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Tests;

/// <summary>
/// The default fixture with the store's retrying execution strategy enabled. EF refuses a transaction begun outside a
/// retrying strategy, so every Jobs store write that opens one must run inside it; rerunning the conformance suites on
/// this fixture proves each write still behaves the same when it does.
/// </summary>
[UsedImplicitly]
[CollectionDefinition(DisableParallelization = true)]
public sealed class PostgreSqlRetryingStrategyFixture
    : PostgreSqlJobsCoordinationFixture,
        ICollectionFixture<PostgreSqlRetryingStrategyFixture>
{
    // A database name of its own gives this fixture its own reused container, apart from the default fixture's.
    protected override PostgreSqlBuilder Configure() => base.Configure().WithDatabase("jobs_retrying_strategy_test");

    public override void ConfigureStore(DbContextOptionsBuilder db) => ConfigureRetryingStore(db);
}

[Collection<PostgreSqlRetryingStrategyFixture>]
public sealed class PostgreSqlRetryingConformanceTests(PostgreSqlRetryingStrategyFixture fixture)
    : PostgreSqlConformanceTestsBase<PostgreSqlRetryingStrategyFixture>(fixture);

[Collection<PostgreSqlRetryingStrategyFixture>]
public sealed class PostgreSqlRetryingClaimConformanceTests(PostgreSqlRetryingStrategyFixture fixture)
    : PostgreSqlClaimConformanceTestsBase<PostgreSqlRetryingStrategyFixture>(fixture);

[Collection<PostgreSqlRetryingStrategyFixture>]
public sealed class PostgreSqlRetryingChainConformanceTests(PostgreSqlRetryingStrategyFixture fixture)
    : PostgreSqlChainConformanceTestsBase<PostgreSqlRetryingStrategyFixture>(fixture);

[Collection<PostgreSqlRetryingStrategyFixture>]
public sealed class PostgreSqlRetryingClaimRetryConformanceTests(PostgreSqlRetryingStrategyFixture fixture)
    : PostgreSqlClaimRetryConformanceTestsBase<PostgreSqlRetryingStrategyFixture>(fixture);

[Collection<PostgreSqlRetryingStrategyFixture>]
public sealed class PostgreSqlRetryingDatabaseClockConformanceTests(PostgreSqlRetryingStrategyFixture fixture)
    : PostgreSqlDatabaseClockConformanceTestsBase<PostgreSqlRetryingStrategyFixture>(fixture);

[Collection<PostgreSqlRetryingStrategyFixture>]
public sealed class PostgreSqlRetryingRecoveryTests(PostgreSqlRetryingStrategyFixture fixture)
    : PostgreSqlRecoveryTestsBase<PostgreSqlRetryingStrategyFixture>(fixture);

[Collection<PostgreSqlRetryingStrategyFixture>]
public sealed class PostgreSqlRetryingRequeueTests(PostgreSqlRetryingStrategyFixture fixture)
    : PostgreSqlRequeueTestsBase<PostgreSqlRetryingStrategyFixture>(fixture);

[Collection<PostgreSqlRetryingStrategyFixture>]
public sealed class PostgreSqlRetryingGenericCronClaimTests(PostgreSqlRetryingStrategyFixture fixture)
    : PostgreSqlGenericCronClaimTestsBase<PostgreSqlRetryingStrategyFixture>(fixture);

[Collection<PostgreSqlRetryingStrategyFixture>]
public sealed class PostgreSqlRetryingSchedulePositionTests(PostgreSqlRetryingStrategyFixture fixture)
    : PostgreSqlSchedulePositionTestsBase<PostgreSqlRetryingStrategyFixture>(fixture);
