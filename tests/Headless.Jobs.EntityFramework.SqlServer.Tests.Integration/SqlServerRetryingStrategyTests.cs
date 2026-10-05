// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.EntityFrameworkCore;

namespace Tests;

/// <summary>
/// The default fixture with the store's retrying execution strategy enabled. EF refuses a transaction begun outside a
/// retrying strategy, so every Jobs store write that opens one must run inside it; rerunning the conformance suites on
/// this fixture proves each write still behaves the same when it does.
/// </summary>
[UsedImplicitly]
[CollectionDefinition(DisableParallelization = true)]
public sealed class SqlServerRetryingStrategyFixture
    : SqlServerJobsCoordinationFixture,
        ICollectionFixture<SqlServerRetryingStrategyFixture>
{
    public override void ConfigureStore(DbContextOptionsBuilder db) => ConfigureRetryingStore(db);
}

[Collection<SqlServerRetryingStrategyFixture>]
public sealed class SqlServerRetryingConformanceTests(SqlServerRetryingStrategyFixture fixture)
    : SqlServerConformanceTestsBase<SqlServerRetryingStrategyFixture>(fixture);

[Collection<SqlServerRetryingStrategyFixture>]
public sealed class SqlServerRetryingClaimConformanceTests(SqlServerRetryingStrategyFixture fixture)
    : SqlServerClaimConformanceTestsBase<SqlServerRetryingStrategyFixture>(fixture);

[Collection<SqlServerRetryingStrategyFixture>]
public sealed class SqlServerRetryingChainConformanceTests(SqlServerRetryingStrategyFixture fixture)
    : SqlServerChainConformanceTestsBase<SqlServerRetryingStrategyFixture>(fixture);

[Collection<SqlServerRetryingStrategyFixture>]
public sealed class SqlServerRetryingClaimRetryConformanceTests(SqlServerRetryingStrategyFixture fixture)
    : SqlServerClaimRetryConformanceTestsBase<SqlServerRetryingStrategyFixture>(fixture);

[Collection<SqlServerRetryingStrategyFixture>]
public sealed class SqlServerRetryingDatabaseClockConformanceTests(SqlServerRetryingStrategyFixture fixture)
    : SqlServerDatabaseClockConformanceTestsBase<SqlServerRetryingStrategyFixture>(fixture);

[Collection<SqlServerRetryingStrategyFixture>]
public sealed class SqlServerRetryingRecoveryTests(SqlServerRetryingStrategyFixture fixture)
    : SqlServerRecoveryTestsBase<SqlServerRetryingStrategyFixture>(fixture);

[Collection<SqlServerRetryingStrategyFixture>]
public sealed class SqlServerRetryingRequeueTests(SqlServerRetryingStrategyFixture fixture)
    : SqlServerRequeueTestsBase<SqlServerRetryingStrategyFixture>(fixture);

[Collection<SqlServerRetryingStrategyFixture>]
public sealed class SqlServerRetryingGenericCronClaimTests(SqlServerRetryingStrategyFixture fixture)
    : SqlServerGenericCronClaimTestsBase<SqlServerRetryingStrategyFixture>(fixture);

[Collection<SqlServerRetryingStrategyFixture>]
public sealed class SqlServerRetryingSchedulePositionTests(SqlServerRetryingStrategyFixture fixture)
    : SqlServerSchedulePositionTestsBase<SqlServerRetryingStrategyFixture>(fixture);
