// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

#pragma warning disable CA1707 // Test names follow the repo's readable snake_case convention.

/// <summary>Runs the shared recovery-decision scenarios against SQL Server.</summary>
[Collection<SqlServerJobsCoordinationFixture>]
public sealed class SqlServerRecoveryPlannerTests(SqlServerJobsCoordinationFixture fixture)
    : JobsRecoveryPlannerConformanceTests<SqlServerJobsCoordinationFixture>(fixture, "sqlserver")
{
    [Fact]
    public override Task the_recovery_walk_resolves_the_backlog_identically_on_every_provider()
    {
        return base.the_recovery_walk_resolves_the_backlog_identically_on_every_provider();
    }

    [Fact]
    public override Task the_saturated_resolution_window_holds_identically_on_every_provider()
    {
        return base.the_saturated_resolution_window_holds_identically_on_every_provider();
    }
}
