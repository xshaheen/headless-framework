// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs;
using Headless.Jobs.Coordination;
using Headless.Testing.Tests;

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
            .ReleaseDeadNodeResources(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new AggregateException(new InvalidOperationException("store blip"))));
        var reclaimer = new JobsDeadOwnerReclaimer(manager, new SchedulerOptionsBuilder());

        var act = () => reclaimer.ReclaimAsync(["node-a@1", "node-b@1", "node-c@1"], AbortToken);

        var thrown = await act.Should().ThrowAsync<AggregateException>();
        thrown.Which.InnerExceptions.Should().ContainSingle().Which.Should().BeOfType<InvalidOperationException>();
        await manager
            .Received(1)
            .ReleaseDeadNodeResources(
                Arg.Is<IReadOnlyCollection<string>>(owners =>
                    owners.SequenceEqual(new[] { "node-a@1", "node-b@1", "node-c@1" })
                ),
                CancellationToken.None
            );
    }

    [Theory]
    [InlineData(null)]
    [InlineData(60)]
    public void reconcile_interval_hands_the_configured_value_to_the_bridge(int? seconds)
    {
        // The bridge derives the default and clamps against the coordination retention window, so the reclaimer
        // passes the scheduler option through unchanged.
        TimeSpan? configured = seconds is null ? null : TimeSpan.FromSeconds(seconds.Value);
        var reclaimer = new JobsDeadOwnerReclaimer(
            Substitute.For<IInternalJobManager>(),
            new SchedulerOptionsBuilder { DeadNodeReconcileInterval = configured }
        );

        reclaimer.ReconcileInterval.Should().Be(configured);
    }
}
