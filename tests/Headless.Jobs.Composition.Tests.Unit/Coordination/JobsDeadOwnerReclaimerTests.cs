// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Coordination;
using Headless.Jobs;
using Headless.Jobs.Coordination;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging;
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

    [Fact]
    public void reconcile_interval_defaults_to_half_the_dead_visibility_window_without_a_warning()
    {
        // With stock defaults the interval used to be 60s against a 30s Dead-visibility window, so every default host
        // logged the clamp warning and the orphan sweep ran on the unclamped minute.
        var logger = new RecordingLogger();
        var reclaimer = new JobsDeadOwnerReclaimer(
            Substitute.For<IInternalJobManager>(),
            new SchedulerOptionsBuilder(),
            Options.Create(new CoordinationOptions()),
            logger
        );

        reclaimer.ReconcileInterval.Should().Be(TimeSpan.FromSeconds(15));
        logger.EventIds.Should().BeEmpty();
    }

    [Fact]
    public void reconcile_interval_is_clamped_to_half_the_dead_visibility_window()
    {
        // An explicit 60s vs a [DeadThreshold, DeadThreshold + DeadRetentionWindow) visibility window only 30s wide —
        // a dead owner whose NodeLeft event was missed had roughly coin-flip odds of ever being observed Dead by the
        // "authoritative backstop".
        var logger = new RecordingLogger();
        var reclaimer = new JobsDeadOwnerReclaimer(
            Substitute.For<IInternalJobManager>(),
            new SchedulerOptionsBuilder { DeadNodeReconcileInterval = TimeSpan.FromMinutes(1) },
            Options.Create(new CoordinationOptions { DeadRetentionWindow = TimeSpan.FromSeconds(30) }),
            logger
        );

        reclaimer.ReconcileInterval.Should().Be(TimeSpan.FromSeconds(15));
        logger.EventIds.Should().Equal(14);
    }

    [Fact]
    public void reconcile_interval_stays_configured_without_a_warning_when_the_host_has_no_membership_provider()
    {
        var logger = new RecordingLogger();
        var reclaimer = new JobsDeadOwnerReclaimer(
            Substitute.For<IInternalJobManager>(),
            new SchedulerOptionsBuilder { DeadNodeReconcileInterval = TimeSpan.FromMinutes(1) },
            Options.Create(new CoordinationOptions()),
            logger,
            new NullNodeMembership()
        );

        reclaimer.ReconcileInterval.Should().Be(TimeSpan.FromMinutes(1));
        logger.EventIds.Should().BeEmpty();
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

    private sealed class RecordingLogger : ILogger<JobsDeadOwnerReclaimer>
    {
        public List<int> EventIds { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            EventIds.Add(eventId.Id);
        }
    }
}
