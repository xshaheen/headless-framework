// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;

namespace Tests;

#pragma warning disable CA1707 // Test names follow the repo's readable snake_case convention.

/// <summary>
/// The shared recovery-decision conformance suite (#834). Every provider runs the SAME scenarios, so the storage-
/// agnostic decision in <c>CronRecoveryPlanner</c> is proven once per backend rather than described twice in
/// comments.
/// </summary>
/// <remarks>
/// Split in two so a failure names which half of the decision moved: the walk over missed instants, and the
/// resolution window a saturated evaluation is confined to. Together they cover every scenario in
/// <see cref="CronRecoveryScenarios.All" />.
/// </remarks>
public abstract class CronRecoveryPlannerConformanceTests : TestBase
{
    /// <summary>Creates this provider's backend. One instance per test method, disposed by the base.</summary>
    protected abstract ICronRecoveryScenarioBackend CreateBackend();

    /// <summary>
    /// Which instant the coalesce walk materializes at, which row it repurposes, which it steps past, and which
    /// residual rows it retires.
    /// </summary>
    public virtual Task the_recovery_walk_resolves_the_backlog_identically_on_every_provider()
    {
        return _RunAsync(CronRecoveryScenarios.All.Where(x => !x.EvaluationSaturated));
    }

    /// <summary>
    /// Where the resolution window ends when the evaluation saturated: confined to the examined prefix while the
    /// backlog is still owed a run, and extended over the full recovery span once that run is established.
    /// </summary>
    public virtual Task the_saturated_resolution_window_holds_identically_on_every_provider()
    {
        return _RunAsync(CronRecoveryScenarios.All.Where(x => x.EvaluationSaturated));
    }

    private async Task _RunAsync(IEnumerable<CronRecoveryScenario> scenarios)
    {
        var ct = AbortToken;
        await using var backend = CreateBackend();
        await backend.PrepareAsync(ct);

        foreach (var scenario in scenarios)
        {
            await CronRecoveryScenarioRunner.RunAsync(backend, scenario, ct);
        }
    }
}
