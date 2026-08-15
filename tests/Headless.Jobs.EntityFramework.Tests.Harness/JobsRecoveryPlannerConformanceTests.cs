// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

/// <summary>
/// Runs the shared recovery-decision scenarios (#834) against a relational provider. The in-memory suite proves the
/// same scenarios, but it cannot prove them <i>for</i> a relational backend: the decision is applied as fenced writes
/// inside one transaction, and whether that composition holds is a property of the store.
/// </summary>
public abstract class JobsRecoveryPlannerConformanceTests<TFixture>(TFixture fixture, string backendName)
    : CronRecoveryPlannerConformanceTests
    where TFixture : class, IJobsCoordinationFixture
{
    protected override ICronRecoveryScenarioBackend CreateBackend()
    {
        return new RelationalCronRecoveryScenarioBackend(fixture, backendName);
    }
}
