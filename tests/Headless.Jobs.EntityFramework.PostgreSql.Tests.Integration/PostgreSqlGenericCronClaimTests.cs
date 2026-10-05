// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

[Collection<PostgreSqlJobsCoordinationFixture>]
public sealed class PostgreSqlGenericCronClaimTests(PostgreSqlJobsCoordinationFixture fixture)
    : PostgreSqlGenericCronClaimTestsBase<PostgreSqlJobsCoordinationFixture>(fixture);

/// <summary>The PostgreSql overrides of the shared suite, run on both the default and the retrying-strategy fixture.</summary>
public abstract class PostgreSqlGenericCronClaimTestsBase<TFixture>(TFixture fixture)
    : JobsGenericCronClaimConformanceTests<TFixture>(fixture)
    where TFixture : PostgreSqlJobsCoordinationFixture
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public override Task existing_cron_claim_returns_confirmed_state_and_preserves_snapshot(bool reclaimExpiredOwner)
    {
        return base.existing_cron_claim_returns_confirmed_state_and_preserves_snapshot(reclaimExpiredOwner);
    }
}
