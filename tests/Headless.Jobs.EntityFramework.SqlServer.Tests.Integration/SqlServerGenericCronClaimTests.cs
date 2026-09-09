// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

#pragma warning disable CA1707 // Test names follow the repo's readable snake_case convention.

[Collection<SqlServerJobsCoordinationFixture>]
public sealed class SqlServerGenericCronClaimTests(SqlServerJobsCoordinationFixture fixture)
    : JobsGenericCronClaimConformanceTests<SqlServerJobsCoordinationFixture>(fixture)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public override Task existing_cron_claim_returns_confirmed_state_and_preserves_snapshot(bool reclaimExpiredOwner)
    {
        return base.existing_cron_claim_returns_confirmed_state_and_preserves_snapshot(reclaimExpiredOwner);
    }
}
