// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

#pragma warning disable CA1707 // Test names follow the repo's readable snake_case convention.

[Collection<SqlServerJobsCoordinationFixture>]
public sealed class SqlServerNativeCronClaimTests(SqlServerJobsCoordinationFixture fixture)
    : JobsNativeCronClaimConformanceTests<SqlServerJobsCoordinationFixture>(fixture)
{
    [Fact]
    public override Task concurrent_cron_batches_with_opposite_order_are_deduplicated()
    {
        return base.concurrent_cron_batches_with_opposite_order_are_deduplicated();
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(4, false)]
    [InlineData(4, true)]
    [InlineData(8, false)]
    [InlineData(8, true)]
    public override Task cron_claim_batches_reads_and_returns_stored_state(int existingCount, bool reclaimExpiredOwner)
    {
        return base.cron_claim_batches_reads_and_returns_stored_state(existingCount, reclaimExpiredOwner);
    }
}
