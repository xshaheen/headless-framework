// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs;
using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Provider;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Tests.Provider;

public sealed class DurableCancellationProviderTests : TestBase
{
    private sealed class FakeTimeJob : TimeJobEntity<FakeTimeJob>;

    private sealed class FakeCronJob : CronJobEntity;

    private const string _Owner = "node-a@incarnation";
    private static readonly DateTime _Now = new(2026, 07, 15, 10, 30, 00, DateTimeKind.Utc);

    [Fact]
    public async Task cancellation_uses_one_compare_and_swap_transition_for_each_supported_state()
    {
        var (provider, _) = _Create();
        var lockedUntil = _Now.AddMinutes(5);
        var idle = _Job(JobStatus.Idle, _Owner, lockedUntil);
        var queued = _Job(JobStatus.Queued, _Owner, lockedUntil);
        var inProgress = _Job(JobStatus.InProgress, _Owner, lockedUntil);
        var terminal = _Job(JobStatus.Succeeded, _Owner, lockedUntil);
        terminal.ExecutedAt = _Now.AddMinutes(-1);
        var terminalUpdatedAt = terminal.UpdatedAt;
        await provider.AddTimeJobsAsync([idle, queued, inProgress, terminal], AbortToken);

        (await provider.IsTimeJobCancellationRequestedAsync(inProgress.Id, AbortToken)).Should().BeFalse();
        (await provider.IsTimeJobCancellationRequestedAsync(queued.Id, AbortToken)).Should().BeNull();
        (await provider.RequestTimeJobCancellationAsync(idle.Id, AbortToken)).Should().BeTrue();
        var queuedRequests = await Task.WhenAll(
            Enumerable.Range(0, 8).Select(_ => provider.RequestTimeJobCancellationAsync(queued.Id, AbortToken))
        );
        (await provider.RequestTimeJobCancellationAsync(inProgress.Id, AbortToken)).Should().BeTrue();

        queuedRequests.Count(static accepted => accepted).Should().Be(1);
        (await provider.RequestTimeJobCancellationAsync(queued.Id, AbortToken)).Should().BeFalse();
        (await provider.RequestTimeJobCancellationAsync(terminal.Id, AbortToken)).Should().BeFalse();
        (await provider.RequestTimeJobCancellationAsync(Guid.NewGuid(), AbortToken)).Should().BeFalse();
        (await provider.IsTimeJobCancellationRequestedAsync(inProgress.Id, AbortToken)).Should().BeTrue();
        (await provider.IsTimeJobCancellationRequestedAsync(terminal.Id, AbortToken)).Should().BeNull();

        var cancelledIdle = await provider.GetTimeJobByIdAsync(idle.Id, AbortToken);
        cancelledIdle!.Status.Should().Be(JobStatus.Cancelled);
        cancelledIdle.CancelRequested.Should().BeTrue();
        cancelledIdle.ExecutedAt.Should().Be(_Now);
        cancelledIdle.UpdatedAt.Should().Be(_Now);
        cancelledIdle.OwnerId.Should().BeNull();
        cancelledIdle.LockedUntil.Should().BeNull();

        var requestedQueued = await provider.GetTimeJobByIdAsync(queued.Id, AbortToken);
        requestedQueued!.Status.Should().Be(JobStatus.Queued);
        requestedQueued.CancelRequested.Should().BeTrue();
        requestedQueued.ExecutedAt.Should().BeNull();
        requestedQueued.OwnerId.Should().Be(_Owner);
        requestedQueued.LockedUntil.Should().Be(lockedUntil);

        var requestedInProgress = await provider.GetTimeJobByIdAsync(inProgress.Id, AbortToken);
        requestedInProgress!.Status.Should().Be(JobStatus.InProgress);
        requestedInProgress.CancelRequested.Should().BeTrue();
        requestedInProgress.ExecutedAt.Should().BeNull();
        requestedInProgress.OwnerId.Should().Be(_Owner);
        requestedInProgress.LockedUntil.Should().Be(lockedUntil);

        var rejectedTerminal = await provider.GetTimeJobByIdAsync(terminal.Id, AbortToken);
        rejectedTerminal!.Status.Should().Be(JobStatus.Succeeded);
        rejectedTerminal.CancelRequested.Should().BeFalse();
        rejectedTerminal.ExecutedAt.Should().Be(_Now.AddMinutes(-1));
        rejectedTerminal.UpdatedAt.Should().Be(terminalUpdatedAt);
        rejectedTerminal.OwnerId.Should().Be(_Owner);
        rejectedTerminal.LockedUntil.Should().Be(lockedUntil);
    }

    [Fact]
    public async Task idle_parent_cancellation_releases_matching_children_and_skips_rejected_branches()
    {
        var (provider, _) = _Create();
        var eligible = _Job(JobStatus.Idle, owner: null, lockedUntil: null, executionTime: null);
        eligible.ExecutionTime = null;
        eligible.RunCondition = RunCondition.OnCancelled;
        var eligibleOnFailure = _Job(JobStatus.Idle, owner: null, lockedUntil: null, executionTime: null);
        eligibleOnFailure.ExecutionTime = null;
        eligibleOnFailure.RunCondition = RunCondition.OnFailureOrCancelled;
        var eligibleAlways = _Job(JobStatus.Idle, owner: null, lockedUntil: null, executionTime: null);
        eligibleAlways.ExecutionTime = null;
        eligibleAlways.RunCondition = RunCondition.OnAnyCompletedStatus;
        var rejectedGrandchild = _Job(JobStatus.Idle, owner: null, lockedUntil: null, executionTime: null);
        rejectedGrandchild.ExecutionTime = null;
        rejectedGrandchild.RunCondition = RunCondition.OnCancelled;
        var rejected = _Job(JobStatus.Idle, owner: null, lockedUntil: null, executionTime: null);
        rejected.ExecutionTime = null;
        rejected.RunCondition = RunCondition.OnSuccess;
        rejected.Children = [rejectedGrandchild];
        var root = _Job(JobStatus.Idle, owner: null, lockedUntil: null, executionTime: _Now.AddMinutes(10));
        root.Children = [eligible, eligibleOnFailure, eligibleAlways, rejected];
        await provider.AddTimeJobsAsync([root], AbortToken);

        (await provider.RequestTimeJobCancellationAsync(root.Id, AbortToken)).Should().BeTrue();

        foreach (var id in new[] { eligible.Id, eligibleOnFailure.Id, eligibleAlways.Id })
        {
            var released = await provider.GetTimeJobByIdAsync(id, AbortToken);
            released!.Status.Should().Be(JobStatus.Idle);
            released.ExecutionTime.Should().Be(_Now);
            released.ExecutedAt.Should().BeNull();
        }

        var skipped = await provider.GetTimeJobByIdAsync(rejected.Id, AbortToken);
        skipped!.Status.Should().Be(JobStatus.Skipped);
        skipped.ExecutedAt.Should().Be(_Now);
        skipped.SkippedReason.Should().Be("Parent cancellation did not satisfy the job run condition.");

        var skippedDescendant = await provider.GetTimeJobByIdAsync(rejectedGrandchild.Id, AbortToken);
        skippedDescendant!.Status.Should().Be(JobStatus.Skipped);
        skippedDescendant.ExecutedAt.Should().Be(_Now);
        skippedDescendant.SkippedReason.Should().Be("Ancestor job was skipped after parent cancellation.");
    }

    [Fact]
    public async Task stale_queue_candidate_cannot_resurrect_idle_cancellation_at_the_same_clock_tick()
    {
        var (provider, _) = _Create();
        var staleCandidate = _Job(JobStatus.Idle, owner: null, lockedUntil: null);
        staleCandidate.UpdatedAt = _Now;
        await provider.AddTimeJobsAsync([staleCandidate], AbortToken);

        (await provider.RequestTimeJobCancellationAsync(staleCandidate.Id, AbortToken)).Should().BeTrue();
        var queued = await provider
            .QueueTimeJobsAsync([new TimeJobEntity { Id = staleCandidate.Id, UpdatedAt = _Now }], AbortToken)
            .ToArrayAsync(AbortToken);

        queued.Should().BeEmpty();
        var persisted = await provider.GetTimeJobByIdAsync(staleCandidate.Id, AbortToken);
        persisted!.Status.Should().Be(JobStatus.Cancelled);
        persisted.CancelRequested.Should().BeTrue();
    }

    private static (JobsInMemoryPersistenceProvider<FakeTimeJob, FakeCronJob> Provider, FakeTimeProvider Time) _Create()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(_Now, TimeSpan.Zero));
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(time);
        services.AddSingleton(new SchedulerOptionsBuilder { NodeId = _Owner });
        var serviceProvider = services.BuildServiceProvider();
        return (new JobsInMemoryPersistenceProvider<FakeTimeJob, FakeCronJob>(serviceProvider), time);
    }

    private static FakeTimeJob _Job(
        JobStatus status,
        string? owner,
        DateTime? lockedUntil,
        DateTime? executionTime = null
    ) =>
        new()
        {
            Id = Guid.NewGuid(),
            Function = "durable-cancellation",
            Status = status,
            OwnerId = owner,
            LockedUntil = lockedUntil,
            ExecutionTime = executionTime ?? _Now.AddMinutes(1),
            CreatedAt = _Now.AddMinutes(-5),
            UpdatedAt = _Now.AddMinutes(-2),
        };
}
