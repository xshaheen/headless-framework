// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Coordination;
using Headless.Jobs;
using Headless.Jobs.Coordination;
using Headless.Jobs.Interfaces.Managers;
using Headless.Testing.Tests;
using Microsoft.Extensions.Options;

namespace Tests.Coordination;

public sealed class JobsDeadOwnerReclaimerTests : TestBase
{
    [Fact]
    public async Task reclaim_continues_past_a_failing_owner_and_aggregates_the_failure()
    {
        // On relational providers the Dead state is visible for only tens of seconds, so an owner skipped because
        // an EARLIER owner in the batch failed can age out of the snapshot before the next reconcile tick and
        // never be reclaimed. Every owner must be attempted; failures aggregate so the bridge's retry stays intact.
        var manager = Substitute.For<IInternalJobManager>();
        manager
            .ReleaseDeadNodeResources("node-b@1", Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("store blip")));
        manager
            .ReleaseDeadNodeResources(Arg.Is<string>(x => x != "node-b@1"), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        var reclaimer = new JobsDeadOwnerReclaimer(manager, new SchedulerOptionsBuilder());

        var act = () => reclaimer.ReclaimAsync(["node-a@1", "node-b@1", "node-c@1"], AbortToken);

        var thrown = await act.Should().ThrowAsync<AggregateException>();
        thrown.Which.InnerExceptions.Should().ContainSingle().Which.Should().BeOfType<InvalidOperationException>();
        await manager.Received(1).ReleaseDeadNodeResources("node-a@1", CancellationToken.None);
        await manager.Received(1).ReleaseDeadNodeResources("node-b@1", CancellationToken.None);
        await manager.Received(1).ReleaseDeadNodeResources("node-c@1", CancellationToken.None);
    }

    [Fact]
    public void reconcile_interval_is_clamped_to_half_the_dead_visibility_window()
    {
        // Stock defaults: reconcile 60s vs a [DeadThreshold, DeadThreshold + DeadRetentionWindow) visibility
        // window only 30s wide — a dead owner whose NodeLeft event was missed had roughly coin-flip odds of ever
        // being observed Dead by the "authoritative backstop".
        var reclaimer = new JobsDeadOwnerReclaimer(
            Substitute.For<IInternalJobManager>(),
            new SchedulerOptionsBuilder { DeadNodeReconcileInterval = TimeSpan.FromMinutes(1) },
            Options.Create(new CoordinationOptions { DeadRetentionWindow = TimeSpan.FromSeconds(30) })
        );

        reclaimer.ReconcileInterval.Should().Be(TimeSpan.FromSeconds(15));
    }

    [Fact]
    public void reconcile_interval_stays_configured_when_already_inside_the_window_or_without_coordination()
    {
        var withoutCoordination = new JobsDeadOwnerReclaimer(
            Substitute.For<IInternalJobManager>(),
            new SchedulerOptionsBuilder { DeadNodeReconcileInterval = TimeSpan.FromMinutes(1) }
        );
        withoutCoordination.ReconcileInterval.Should().Be(TimeSpan.FromMinutes(1));

        var alreadyTight = new JobsDeadOwnerReclaimer(
            Substitute.For<IInternalJobManager>(),
            new SchedulerOptionsBuilder { DeadNodeReconcileInterval = TimeSpan.FromSeconds(10) },
            Options.Create(new CoordinationOptions { DeadRetentionWindow = TimeSpan.FromSeconds(30) })
        );
        alreadyTight.ReconcileInterval.Should().Be(TimeSpan.FromSeconds(10));
    }
}
