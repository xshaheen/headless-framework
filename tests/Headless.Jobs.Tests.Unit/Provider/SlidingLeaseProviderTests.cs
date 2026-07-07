// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs;
using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Models;
using Headless.Jobs.Provider;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Tests.Provider;

/// <summary>
/// Deterministic in-memory coverage for the #316 sliding execution lease: renewal (U1), the renewal loss
/// detector (U2), stalled-job reclaim (U3), the node-death sweep's lease deferral (U4), and the claim→start
/// ownership recheck (U5). The real transactional behavior is proved cross-provider in the EF harness (U7).
/// </summary>
public sealed class SlidingLeaseProviderTests : TestBase
{
    private sealed class FakeTimeJob : TimeJobEntity<FakeTimeJob>;

    private sealed class FakeCronJob : CronJobEntity;

    private const string _NodeA = "node-a";
    private const string _NodeB = "node-b";
    private static readonly DateTime _Now = new(2026, 06, 17, 12, 00, 00, DateTimeKind.Utc);
    private static readonly TimeSpan _Lease = TimeSpan.FromMinutes(5);

    private static (JobsInMemoryPersistenceProvider<FakeTimeJob, FakeCronJob> Provider, FakeTimeProvider Time) _Create(
        string nodeId = _NodeA
    )
    {
        var time = new FakeTimeProvider(new DateTimeOffset(_Now, TimeSpan.Zero));
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(time);
        services.AddSingleton(new SchedulerOptionsBuilder { NodeId = nodeId, LeaseDuration = _Lease });
        var sp = services.BuildServiceProvider();
        return (new JobsInMemoryPersistenceProvider<FakeTimeJob, FakeCronJob>(sp), time);
    }

    private static FakeTimeJob _TimeJob(
        JobStatus status,
        string? owner,
        DateTime? lockedUntil,
        NodeDeathPolicy policy = NodeDeathPolicy.Retry
    )
    {
        return new FakeTimeJob
        {
            Id = Guid.NewGuid(),
            Function = "fn",
            Status = status,
            OwnerId = owner,
            LockedUntil = lockedUntil,
            OnNodeDeath = policy,
            ExecutionTime = _Now.AddMinutes(-2),
        };
    }

    // ── U1 / U2: renewal is the loss detector ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task RenewTimeJobLease_advances_lease_and_returns_one_for_an_owned_running_row()
    {
        var (provider, time) = _Create();
        var job = _TimeJob(JobStatus.InProgress, _NodeA, _Now.AddMinutes(1));
        await provider.AddTimeJobsAsync([job], AbortToken);

        time.Advance(TimeSpan.FromMinutes(2)); // job runs past part of its lease
        var affected = await provider.RenewTimeJobLeaseAsync(job.Id, AbortToken);

        affected.Should().Be(1);
        var renewed = await provider.GetTimeJobByIdAsync(job.Id, AbortToken);
        renewed!.LockedUntil.Should().Be(_Now.AddMinutes(2).Add(_Lease)); // now (T+2m) + LeaseDuration
    }

    [Fact]
    public async Task RenewTimeJobLease_returns_zero_when_owner_changed()
    {
        var (provider, _) = _Create();
        var job = _TimeJob(JobStatus.InProgress, _NodeB, _Now.AddMinutes(1)); // owned by another node
        await provider.AddTimeJobsAsync([job], AbortToken);

        var affected = await provider.RenewTimeJobLeaseAsync(job.Id, AbortToken);

        affected.Should().Be(0); // lease lost -> cancel-on-loss
    }

    [Fact]
    public async Task RenewTimeJobLease_returns_zero_when_row_terminalized()
    {
        var (provider, _) = _Create();
        var job = _TimeJob(JobStatus.Succeeded, _NodeA, _Now.AddMinutes(1));
        await provider.AddTimeJobsAsync([job], AbortToken);

        var affected = await provider.RenewTimeJobLeaseAsync(job.Id, AbortToken);

        affected.Should().Be(0);
    }

    [Fact]
    public async Task RenewTimeJobLease_returns_zero_for_an_owned_queued_row()
    {
        // #13: renewal slides a RUNNING lease only. A Queued row hasn't started, so renewal must NOT extend it (a
        // returned 1 would read as "lease held" and suppress the cancel-on-loss signal).
        var (provider, _) = _Create();
        var job = _TimeJob(JobStatus.Queued, _NodeA, _Now.AddMinutes(1));
        await provider.AddTimeJobsAsync([job], AbortToken);

        (await provider.RenewTimeJobLeaseAsync(job.Id, AbortToken)).Should().Be(0);
    }

    [Fact]
    public async Task RenewTimeJobLease_returns_zero_after_a_retry_reclaim_released_the_row()
    {
        // #5 invariant: no reclaim arm leaves (InProgress, Owner==original) true, so renewal can never resurrect a
        // reclaimed lease. After a Retry reclaim the row is Idle + ownerless, so the original owner renews 0 rows.
        var (provider, _) = _Create();
        var job = _TimeJob(JobStatus.InProgress, _NodeA, _Now.AddMinutes(-1), NodeDeathPolicy.Retry);
        await provider.AddTimeJobsAsync([job], AbortToken);

        (await provider.ReclaimStalledTimeJobsAsync(AbortToken)).Should().Be(1);
        (await provider.RenewTimeJobLeaseAsync(job.Id, AbortToken)).Should().Be(0);
    }

    // ── U3: stalled-job reclaim (lapsed-lease InProgress, per policy) ────────────────────────────────────────

    [Fact]
    public async Task ReclaimStalledTimeJobs_retry_releases_lapsed_row_to_idle()
    {
        var (provider, _) = _Create();
        var job = _TimeJob(JobStatus.InProgress, _NodeA, _Now.AddMinutes(-1), NodeDeathPolicy.Retry);
        await provider.AddTimeJobsAsync([job], AbortToken);

        var affected = await provider.ReclaimStalledTimeJobsAsync(AbortToken);

        affected.Should().Be(1);
        var reclaimed = await provider.GetTimeJobByIdAsync(job.Id, AbortToken);
        reclaimed!.Status.Should().Be(JobStatus.Idle);
        reclaimed.OwnerId.Should().BeNull();
        reclaimed.LockedUntil.Should().BeNull();
    }

    [Fact]
    public async Task ReclaimStalledTimeJobs_markFailed_terminalizes_lapsed_row()
    {
        var (provider, _) = _Create();
        var job = _TimeJob(JobStatus.InProgress, _NodeA, _Now.AddMinutes(-1), NodeDeathPolicy.MarkFailed);
        await provider.AddTimeJobsAsync([job], AbortToken);

        await provider.ReclaimStalledTimeJobsAsync(AbortToken);

        var reclaimed = await provider.GetTimeJobByIdAsync(job.Id, AbortToken);
        reclaimed!.Status.Should().Be(JobStatus.Failed);
        reclaimed.LockedUntil.Should().BeNull();
        reclaimed.ExceptionMessage.Should().Be("Lease lapsed while running!");
    }

    [Fact]
    public async Task ReclaimStalledTimeJobs_skip_terminalizes_lapsed_row()
    {
        var (provider, _) = _Create();
        var job = _TimeJob(JobStatus.InProgress, _NodeA, _Now.AddMinutes(-1), NodeDeathPolicy.Skip);
        await provider.AddTimeJobsAsync([job], AbortToken);

        await provider.ReclaimStalledTimeJobsAsync(AbortToken);

        var reclaimed = await provider.GetTimeJobByIdAsync(job.Id, AbortToken);
        reclaimed!.Status.Should().Be(JobStatus.Skipped);
        reclaimed.LockedUntil.Should().BeNull();
        reclaimed.SkippedReason.Should().Be("Lease lapsed while running!");
    }

    [Fact]
    public async Task ReclaimStalledTimeJobs_leaves_a_healthy_renewing_job_untouched()
    {
        var (provider, _) = _Create();
        var job = _TimeJob(JobStatus.InProgress, _NodeA, _Now.AddMinutes(10)); // valid future lease
        await provider.AddTimeJobsAsync([job], AbortToken);

        var affected = await provider.ReclaimStalledTimeJobsAsync(AbortToken);

        affected.Should().Be(0);
        var untouched = await provider.GetTimeJobByIdAsync(job.Id, AbortToken);
        untouched!.Status.Should().Be(JobStatus.InProgress);
        untouched.OwnerId.Should().Be(_NodeA);
    }

    [Fact]
    public async Task ReclaimStalledTimeJobs_is_idempotent()
    {
        var (provider, _) = _Create();
        var job = _TimeJob(JobStatus.InProgress, _NodeA, _Now.AddMinutes(-1), NodeDeathPolicy.Retry);
        await provider.AddTimeJobsAsync([job], AbortToken);

        (await provider.ReclaimStalledTimeJobsAsync(AbortToken)).Should().Be(1);
        (await provider.ReclaimStalledTimeJobsAsync(AbortToken)).Should().Be(0); // already reclaimed -> no-op
    }

    // ── U4: node-death sweep defers InProgress to the lease ──────────────────────────────────────────────────

    [Fact]
    public async Task DeadNodeSweep_leaves_a_valid_lease_inprogress_row_to_the_lease()
    {
        var (provider, _) = _Create();
        var job = _TimeJob(JobStatus.InProgress, _NodeA, _Now.AddMinutes(10)); // still-valid lease
        await provider.AddTimeJobsAsync([job], AbortToken);

        var affected = await provider.ReleaseDeadNodeTimeJobResourcesAsync(_NodeA, AbortToken);

        affected.Should().Be(0); // U3 handles it once the lease lapses
        var row = await provider.GetTimeJobByIdAsync(job.Id, AbortToken);
        row!.Status.Should().Be(JobStatus.InProgress);
        row.OwnerId.Should().Be(_NodeA);
    }

    [Fact]
    public async Task DeadNodeSweep_reclaims_a_lapsed_lease_inprogress_row_per_policy()
    {
        var (provider, _) = _Create();
        var job = _TimeJob(JobStatus.InProgress, _NodeA, _Now.AddMinutes(-1), NodeDeathPolicy.Retry);
        await provider.AddTimeJobsAsync([job], AbortToken);

        var affected = await provider.ReleaseDeadNodeTimeJobResourcesAsync(_NodeA, AbortToken);

        affected.Should().Be(1);
        var row = await provider.GetTimeJobByIdAsync(job.Id, AbortToken);
        row!.Status.Should().Be(JobStatus.Idle);
        row.OwnerId.Should().BeNull();
    }

    [Fact]
    public async Task DeadNodeSweep_reclaims_idle_and_queued_immediately_regardless_of_lease()
    {
        var (provider, _) = _Create();
        var idle = _TimeJob(JobStatus.Idle, _NodeA, _Now.AddMinutes(10));
        var queued = _TimeJob(JobStatus.Queued, _NodeA, _Now.AddMinutes(10));
        await provider.AddTimeJobsAsync([idle, queued], AbortToken);

        var affected = await provider.ReleaseDeadNodeTimeJobResourcesAsync(_NodeA, AbortToken);

        affected.Should().Be(2);
        (await provider.GetTimeJobByIdAsync(idle.Id, AbortToken))!.OwnerId.Should().BeNull();
        (await provider.GetTimeJobByIdAsync(queued.Id, AbortToken))!.OwnerId.Should().BeNull();
    }

    // ── U5: claim→start ownership recheck ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task UnifiedContextUpdate_does_not_stamp_a_row_reclaimed_by_another_owner()
    {
        var (provider, _) = _Create(); // this node is NodeA
        var job = _TimeJob(JobStatus.Queued, _NodeB, _Now.AddMinutes(1)); // re-claimed by NodeB before we start it
        await provider.AddTimeJobsAsync([job], AbortToken);

        var unified = new JobExecutionState { FunctionName = "fn" }.SetProperty(x => x.Status, JobStatus.InProgress);
        var stampedIds = await provider.UpdateTimeJobsWithUnifiedContextAsync([job.Id], unified, AbortToken);

        stampedIds.Should().BeEmpty();
        var row = await provider.GetTimeJobByIdAsync(job.Id, AbortToken);
        row!.Status.Should().Be(JobStatus.Queued); // untouched — still NodeB's
        row.OwnerId.Should().Be(_NodeB);
    }

    [Fact]
    public async Task UnifiedContextUpdate_stamps_a_still_owned_row_inprogress()
    {
        var (provider, _) = _Create(); // this node is NodeA
        var job = _TimeJob(JobStatus.Queued, _NodeA, _Now.AddMinutes(1));
        await provider.AddTimeJobsAsync([job], AbortToken);

        var unified = new JobExecutionState { FunctionName = "fn" }.SetProperty(x => x.Status, JobStatus.InProgress);
        var stampedIds = await provider.UpdateTimeJobsWithUnifiedContextAsync([job.Id], unified, AbortToken);

        stampedIds.Should().Equal(job.Id);
        (await provider.GetTimeJobByIdAsync(job.Id, AbortToken))!.Status.Should().Be(JobStatus.InProgress);
    }

    // ── cron occurrence mirrors (renew + reclaim) ────────────────────────────────────────────────────────────

    private async Task<Guid> _SeedCronOccurrence(
        JobsInMemoryPersistenceProvider<FakeTimeJob, FakeCronJob> provider,
        JobStatus status,
        string? owner,
        DateTime? lockedUntil,
        NodeDeathPolicy policy = NodeDeathPolicy.Retry
    )
    {
        var occurrence = new CronJobOccurrenceEntity<FakeCronJob>
        {
            Id = Guid.NewGuid(),
            Status = status,
            OwnerId = owner,
            LockedUntil = lockedUntil,
            OnNodeDeath = policy,
            ExecutionTime = _Now.AddMinutes(-2),
            CronJobId = Guid.NewGuid(),
            CronJob = new FakeCronJob { Function = "fn", Expression = "* * * * *" },
        };
        await provider.InsertCronJobOccurrencesAsync([occurrence], CancellationToken.None);
        return occurrence.Id;
    }

    [Fact]
    public async Task RenewCronJobOccurrenceLease_returns_zero_when_owner_changed()
    {
        var (provider, _) = _Create();
        var id = await _SeedCronOccurrence(provider, JobStatus.InProgress, _NodeB, _Now.AddMinutes(1));

        (await provider.RenewCronJobOccurrenceLeaseAsync(id, AbortToken)).Should().Be(0);
    }

    [Fact]
    public async Task ReclaimStalledCronJobOccurrences_releases_lapsed_retry_to_idle()
    {
        var (provider, _) = _Create();
        var id = await _SeedCronOccurrence(provider, JobStatus.InProgress, _NodeA, _Now.AddMinutes(-1));

        (await provider.ReclaimStalledCronJobOccurrencesAsync(AbortToken)).Should().Be(1);
        var rows = await provider.GetAllCronJobOccurrencesAsync(x => x.Id == id, AbortToken);
        rows.Should().ContainSingle().Which.Status.Should().Be(JobStatus.Idle);
    }

    [Fact]
    public async Task ReclaimStalledCronJobOccurrences_markFailed_terminalizes_lapsed_row()
    {
        // #23: cron mirror of the time-job MarkFailed reclaim — parity gap that had no unit coverage.
        var (provider, _) = _Create();
        var id = await _SeedCronOccurrence(
            provider,
            JobStatus.InProgress,
            _NodeA,
            _Now.AddMinutes(-1),
            NodeDeathPolicy.MarkFailed
        );

        await provider.ReclaimStalledCronJobOccurrencesAsync(AbortToken);

        var row = (await provider.GetAllCronJobOccurrencesAsync(x => x.Id == id, AbortToken))
            .Should()
            .ContainSingle()
            .Subject;
        row.Status.Should().Be(JobStatus.Failed);
        row.LockedUntil.Should().BeNull();
        row.ExceptionMessage.Should().Be("Lease lapsed while running!");
    }

    [Fact]
    public async Task ReclaimStalledCronJobOccurrences_skip_terminalizes_lapsed_row()
    {
        // #23: cron mirror of the time-job Skip reclaim.
        var (provider, _) = _Create();
        var id = await _SeedCronOccurrence(
            provider,
            JobStatus.InProgress,
            _NodeA,
            _Now.AddMinutes(-1),
            NodeDeathPolicy.Skip
        );

        await provider.ReclaimStalledCronJobOccurrencesAsync(AbortToken);

        var row = (await provider.GetAllCronJobOccurrencesAsync(x => x.Id == id, AbortToken))
            .Should()
            .ContainSingle()
            .Subject;
        row.Status.Should().Be(JobStatus.Skipped);
        row.LockedUntil.Should().BeNull();
        row.SkippedReason.Should().Be("Lease lapsed while running!");
    }

    [Fact]
    public async Task RenewCronJobOccurrenceLease_returns_zero_for_an_owned_queued_row()
    {
        // #13 (cron): renewal slides a RUNNING occurrence lease only.
        var (provider, _) = _Create();
        var id = await _SeedCronOccurrence(provider, JobStatus.Queued, _NodeA, _Now.AddMinutes(1));

        (await provider.RenewCronJobOccurrenceLeaseAsync(id, AbortToken)).Should().Be(0);
    }

    [Fact]
    public async Task QueueCronJobOccurrences_re_queue_restamps_OnNodeDeath_from_the_cron_definition()
    {
        // #464: re-queuing an existing occurrence re-stamps OnNodeDeath from the cron def (context), not the stored
        // value, so EF and in-memory agree and a mid-flight policy edit takes effect.
        var (provider, _) = _Create();
        var occId = await _SeedCronOccurrence(
            provider,
            JobStatus.Queued,
            _NodeA,
            _Now.AddMinutes(1),
            NodeDeathPolicy.Retry
        );

        var context = new JobManagerDispatchContext(Guid.NewGuid())
        {
            FunctionName = "fn",
            Expression = "* * * * *",
            OnNodeDeath = NodeDeathPolicy.Skip, // policy changed on the def since the occurrence was created
            NextCronOccurrence = new NextCronOccurrence(occId, _Now),
        };

        var yielded = new List<CronJobOccurrenceEntity<FakeCronJob>>();
        await foreach (
            var occurrence in provider.QueueCronJobOccurrencesAsync((_Now.AddMinutes(-2), [context]), AbortToken)
        )
        {
            yielded.Add(occurrence);
        }

        yielded.Should().ContainSingle().Which.OnNodeDeath.Should().Be(NodeDeathPolicy.Skip);
        var stored = await provider.GetAllCronJobOccurrencesAsync(x => x.Id == occId, AbortToken);
        stored.Should().ContainSingle().Which.OnNodeDeath.Should().Be(NodeDeathPolicy.Skip);
    }
}
