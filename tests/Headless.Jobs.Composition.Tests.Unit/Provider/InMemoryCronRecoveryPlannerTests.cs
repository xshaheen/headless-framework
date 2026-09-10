// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests.Provider;

#pragma warning disable CA1707 // Test names follow the repo's readable snake_case convention.

/// <summary>Runs the shared recovery-decision scenarios against the in-memory provider.</summary>
public sealed class InMemoryCronRecoveryPlannerTests : CronRecoveryPlannerConformanceTests
{
    protected override ICronRecoveryScenarioBackend CreateBackend()
    {
        return new InMemoryCronRecoveryScenarioBackend();
    }

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
