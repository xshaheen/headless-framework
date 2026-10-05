// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

[Collection<SqlServerJobsCoordinationFixture>]
public sealed class SqlServerGenericCronClaimTests(SqlServerJobsCoordinationFixture fixture)
    : SqlServerGenericCronClaimTestsBase<SqlServerJobsCoordinationFixture>(fixture);

/// <summary>The SqlServer overrides of the shared suite, run on both the default and the retrying-strategy fixture.</summary>
public abstract class SqlServerGenericCronClaimTestsBase<TFixture>(TFixture fixture)
    : JobsGenericCronClaimConformanceTests<TFixture>(fixture)
    where TFixture : SqlServerJobsCoordinationFixture
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public override Task existing_cron_claim_returns_confirmed_state_and_preserves_snapshot(bool reclaimExpiredOwner)
    {
        return base.existing_cron_claim_returns_confirmed_state_and_preserves_snapshot(reclaimExpiredOwner);
    }
}
