// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

#pragma warning disable CA1707 // Test names follow the repo's readable snake_case convention.

/// <summary>Runs the RunOnly claim-filter conformance suite against SqlServer.</summary>
[Collection<SqlServerJobsCoordinationFixture>]
public sealed class SqlServerRunOnlyTests(SqlServerJobsCoordinationFixture fixture)
    : JobsRunOnlyConformanceTests<SqlServerJobsCoordinationFixture>(fixture)
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public override Task filtered_host_never_claims_a_filtered_time_job_that_an_unfiltered_host_claims(
        bool useNativeClaims
    )
    {
        return base.filtered_host_never_claims_a_filtered_time_job_that_an_unfiltered_host_claims(useNativeClaims);
    }

    [Fact]
    public override Task filtered_host_schedules_a_filtered_job_that_an_unfiltered_host_claims()
    {
        return base.filtered_host_schedules_a_filtered_job_that_an_unfiltered_host_claims();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public override Task filtered_host_never_claims_or_dispatches_a_filtered_cron_job(bool useNativeClaims)
    {
        return base.filtered_host_never_claims_or_dispatches_a_filtered_cron_job(useNativeClaims);
    }
}
