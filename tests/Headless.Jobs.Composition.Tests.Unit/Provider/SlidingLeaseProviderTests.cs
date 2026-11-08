// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Fencing;
using Headless.Hosting;
using Headless.Jobs;
using Headless.Jobs.Provider;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Tests.Provider;

/// <summary>
/// Deterministic in-memory coverage for the #316 sliding execution lease: renewal, the renewal loss
/// detector, stalled-job reclaim, the node-death sweep's lease deferral, and the claim→start
/// ownership recheck. The real transactional behavior is proved cross-provider in the EF harness.
/// </summary>
public sealed class SlidingLeaseProviderTests : TestBase
{
    private sealed class FakeTimeJob : TimeJobEntity<FakeTimeJob>;

    private sealed class FakeCronJob : CronJobEntity;

    private const string _NodeA = "node-a";
    private const string _NodeB = "node-b";
    private static readonly DateTimeOffset _Now = new(2026, 06, 17, 12, 00, 00, TimeSpan.Zero);
    private static readonly TimeSpan _Lease = TimeSpan.FromMinutes(5);

    // The generation this node's claim drew. A row another node holds was granted to it under a later generation, so
    // this node's writes are stale for it.
    private const long _ClaimGeneration = 1;

    private static long _GenerationHeldBy(string? owner) =>
        string.Equals(owner, _NodeB, StringComparison.Ordinal) ? _ClaimGeneration + 1 : _ClaimGeneration;

    private static FencedRow<Guid> _Claimed(FakeTimeJob job) => JobsLeaseTables.TimeJob(job.Id, _ClaimGeneration);

    private static FencedRow<Guid> _ClaimedOccurrence(Guid id) => JobsLeaseTables.CronOccurrence(id, _ClaimGeneration);

    private static (JobsInMemoryPersistenceProvider<FakeTimeJob, FakeCronJob> Provider, FakeTimeProvider Time) _Create(
        string nodeId = _NodeA
    )
    {
        var time = new FakeTimeProvider(_Now);
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(time);
        services.AddHeadlessGuidGenerator();
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
            Lease = TestLeases.Held(owner, lockedUntil, _GenerationHeldBy(owner)),
            OnNodeDeath = policy,
            ExecutionTime = _Now.UtcDateTime.AddMinutes(-2),
        };
    }

    // ── Renewal is the loss detector ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task renew_time_job_lease_advances_lease_and_returns_one_for_an_owned_running_row()
    {
        var (provider, time) = _Create();
        // A renewal only extends a live lease, so the deadline lies beyond the point the clock advances to.
        var job = _TimeJob(JobStatus.InProgress, _NodeA, _Now.UtcDateTime.AddMinutes(3));
        await provider.AddTimeJobsAsync([job], AbortToken);

        time.Advance(TimeSpan.FromMinutes(2)); // job runs past part of its lease
        var affected = await provider.RenewTimeJobLeaseAsync(_Claimed(job), _NodeA, AbortToken);

        affected.Should().Be(1);
        var renewed = await provider.GetTimeJobByIdAsync(job.Id, AbortToken);
        renewed!.Lease.ExpiresAt.Should().Be(_Now.AddMinutes(2).Add(_Lease)); // now (T+2m) + LeaseDuration
    }

    [Fact]
    public async Task renew_time_job_lease_returns_zero_when_owner_changed()
    {
        var (provider, _) = _Create();
        var job = _TimeJob(JobStatus.InProgress, _NodeB, _Now.UtcDateTime.AddMinutes(1)); // owned by another node
        await provider.AddTimeJobsAsync([job], AbortToken);

        var affected = await provider.RenewTimeJobLeaseAsync(_Claimed(job), _NodeA, AbortToken);

        affected.Should().Be(0); // lease lost -> cancel-on-loss
    }

    [Fact]
    public async Task renew_time_job_lease_returns_zero_when_row_terminalized()
    {
        var (provider, _) = _Create();
        var job = _TimeJob(JobStatus.Succeeded, _NodeA, _Now.UtcDateTime.AddMinutes(1));
        await provider.AddTimeJobsAsync([job], AbortToken);

        var affected = await provider.RenewTimeJobLeaseAsync(_Claimed(job), _NodeA, AbortToken);

        affected.Should().Be(0);
    }

    [Fact]
    public async Task renew_time_job_lease_returns_zero_for_an_owned_queued_row()
    {
        // #13: renewal slides a RUNNING lease only. A Queued row hasn't started, so renewal must NOT extend it (a
        // returned 1 would read as "lease held" and suppress the cancel-on-loss signal).
        var (provider, _) = _Create();
        var job = _TimeJob(JobStatus.Queued, _NodeA, _Now.UtcDateTime.AddMinutes(1));
        await provider.AddTimeJobsAsync([job], AbortToken);

        (await provider.RenewTimeJobLeaseAsync(_Claimed(job), _NodeA, AbortToken)).Should().Be(0);
    }

    [Fact]
    public async Task renew_time_job_lease_returns_zero_after_a_retry_reclaim_released_the_row()
    {
        // #5 invariant: no reclaim arm leaves (InProgress, Owner==original) true, so renewal can never resurrect a
        // reclaimed lease. After a Retry reclaim the row is Idle + ownerless, so the original owner renews 0 rows.
        var (provider, _) = _Create();
        var job = _TimeJob(JobStatus.InProgress, _NodeA, _Now.UtcDateTime.AddMinutes(-1), NodeDeathPolicy.Retry);
        await provider.AddTimeJobsAsync([job], AbortToken);

        (await provider.ReclaimStalledTimeJobsAsync(AbortToken)).Should().Be(1);
        (await provider.RenewTimeJobLeaseAsync(_Claimed(job), _NodeA, AbortToken)).Should().Be(0);
    }

    [Fact]
    public async Task queue_time_jobs_claims_the_job_tree_and_preserves_retry_counts()
    {
        var (provider, _) = _Create();
        var root = _TimeJob(JobStatus.Idle, owner: null, lockedUntil: null);
        root.ExecutionTime = _Now.UtcDateTime.AddMilliseconds(500);
        root.RetryCount = 2;
        var child = _TimeJob(JobStatus.Idle, owner: null, lockedUntil: null);
        child.ParentId = root.Id;
        child.ExecutionTime = null;
        child.RetryCount = 3;
        var grandChild = _TimeJob(JobStatus.Idle, owner: null, lockedUntil: null);
        grandChild.ParentId = child.Id;
        grandChild.ExecutionTime = null;
        grandChild.RetryCount = 4;
        await provider.AddTimeJobsAsync([root, child, grandChild], AbortToken);
        var roots = (await provider.GetEarliestTimeJobsAsync(AbortToken)).Jobs;

        TimeJobEntity? claimed = null;
        await foreach (var job in provider.QueueTimeJobsAsync(roots, AbortToken))
        {
            claimed = job;
        }

        claimed.Should().NotBeNull();
        claimed!.RetryCount.Should().Be(2);
        claimed.Children.Should().ContainSingle().Which.RetryCount.Should().Be(3);
        claimed.Children.Single().Children.Should().ContainSingle().Which.RetryCount.Should().Be(4);

        var storedChild = await provider.GetTimeJobByIdAsync(child.Id, AbortToken);
        var storedGrandChild = await provider.GetTimeJobByIdAsync(grandChild.Id, AbortToken);
        storedChild!.Lease.Holder.Should().Be(_NodeA);
        storedChild.Lease.ExpiresAt.Should().Be(_Now.Add(_Lease));
        storedGrandChild!.Lease.Holder.Should().Be(_NodeA);
        storedGrandChild.Lease.ExpiresAt.Should().Be(_Now.Add(_Lease));
    }

    [Fact]
    public async Task queue_timed_out_time_jobs_does_not_steal_a_live_main_scheduler_claim()
    {
        var (provider, time) = _Create();
        var job = _TimeJob(JobStatus.Idle, owner: null, lockedUntil: null);
        await provider.AddTimeJobsAsync([job], AbortToken);
        var stored = await provider.GetTimeJobByIdAsync(job.Id, AbortToken);
        await foreach (
            var _ in provider.QueueTimeJobsAsync(
                [new TimeJobEntity { Id = job.Id, UpdatedAt = stored!.UpdatedAt }],
                AbortToken
            )
        ) { }

        var whileLeased = await provider.QueueTimedOutTimeJobsAsync(AbortToken).ToListAsync(AbortToken);

        whileLeased.Should().BeEmpty();

        time.Advance(_Lease.Add(TimeSpan.FromSeconds(1)));
        var afterExpiry = await provider.QueueTimedOutTimeJobsAsync(AbortToken).ToListAsync(AbortToken);

        afterExpiry.Should().ContainSingle().Which.Id.Should().Be(job.Id);
    }

    // ── Stalled-job reclaim (lapsed-lease InProgress, per policy) ────────────────────────────────────────────

    [Fact]
    public async Task queue_timed_out_time_jobs_claims_the_job_tree()
    {
        var (provider, _) = _Create();
        var root = _TimeJob(JobStatus.Idle, owner: null, lockedUntil: null);
        var child = _TimeJob(JobStatus.Idle, owner: null, lockedUntil: null);
        child.ParentId = root.Id;
        child.ExecutionTime = null;
        var grandChild = _TimeJob(JobStatus.Idle, owner: null, lockedUntil: null);
        grandChild.ParentId = child.Id;
        grandChild.ExecutionTime = null;
        await provider.AddTimeJobsAsync([root, child, grandChild], AbortToken);

        var claimed = await provider.QueueTimedOutTimeJobsAsync(AbortToken).ToListAsync(AbortToken);

        claimed.Should().ContainSingle().Which.Id.Should().Be(root.Id);
        claimed[0].Status.Should().Be(JobStatus.Queued);
        claimed[0].Children.Should().ContainSingle().Which.Id.Should().Be(child.Id);
        claimed[0].Children.Single().Children.Should().ContainSingle().Which.Id.Should().Be(grandChild.Id);

        var storedChild = await provider.GetTimeJobByIdAsync(child.Id, AbortToken);
        var storedGrandChild = await provider.GetTimeJobByIdAsync(grandChild.Id, AbortToken);
        storedChild!.Lease.Holder.Should().Be(_NodeA);
        storedChild.Lease.ExpiresAt.Should().Be(_Now.Add(_Lease));
        storedGrandChild!.Lease.Holder.Should().Be(_NodeA);
        storedGrandChild.Lease.ExpiresAt.Should().Be(_Now.Add(_Lease));

        var start = new JobExecutionState { FunctionName = "fn", ClaimOwner = _NodeA }.SetProperty(
            x => x.Status,
            JobStatus.InProgress
        );
        var rootGeneration = (await provider.GetTimeJobByIdAsync(root.Id, AbortToken))!.Lease.Generation;
        (
            await provider.UpdateTimeJobsWithUnifiedContextAsync(
                [JobsLeaseTables.TimeJob(root.Id, rootGeneration)],
                start,
                AbortToken
            )
        )
            .Should()
            .Equal(root.Id);
        (await provider.GetTimeJobByIdAsync(root.Id, AbortToken))!.Status.Should().Be(JobStatus.InProgress);
    }

    [Fact]
    public async Task reclaim_stalled_time_jobs_retry_releases_lapsed_row_to_idle()
    {
        var (provider, _) = _Create();
        var job = _TimeJob(JobStatus.InProgress, _NodeA, _Now.UtcDateTime.AddMinutes(-1), NodeDeathPolicy.Retry);
        await provider.AddTimeJobsAsync([job], AbortToken);

        var affected = await provider.ReclaimStalledTimeJobsAsync(AbortToken);

        affected.Should().Be(1);
        var reclaimed = await provider.GetTimeJobByIdAsync(job.Id, AbortToken);
        reclaimed!.Status.Should().Be(JobStatus.Idle);
        reclaimed.Lease.State.Should().NotBe(RowLeaseState.Active);
    }

    [Fact]
    public async Task reclaim_stalled_time_jobs_mark_failed_terminalizes_lapsed_row()
    {
        var (provider, _) = _Create();
        var job = _TimeJob(JobStatus.InProgress, _NodeA, _Now.UtcDateTime.AddMinutes(-1), NodeDeathPolicy.MarkFailed);
        await provider.AddTimeJobsAsync([job], AbortToken);

        await provider.ReclaimStalledTimeJobsAsync(AbortToken);

        var reclaimed = await provider.GetTimeJobByIdAsync(job.Id, AbortToken);
        reclaimed!.Status.Should().Be(JobStatus.Failed);
        reclaimed.Lease.State.Should().NotBe(RowLeaseState.Active);
        reclaimed.ExceptionMessage.Should().Be("Lease lapsed while running!");
    }

    [Fact]
    public async Task reclaim_stalled_time_jobs_skip_terminalizes_lapsed_row()
    {
        var (provider, _) = _Create();
        var job = _TimeJob(JobStatus.InProgress, _NodeA, _Now.UtcDateTime.AddMinutes(-1), NodeDeathPolicy.Skip);
        await provider.AddTimeJobsAsync([job], AbortToken);

        await provider.ReclaimStalledTimeJobsAsync(AbortToken);

        var reclaimed = await provider.GetTimeJobByIdAsync(job.Id, AbortToken);
        reclaimed!.Status.Should().Be(JobStatus.Skipped);
        reclaimed.Lease.State.Should().NotBe(RowLeaseState.Active);
        reclaimed.SkippedReason.Should().Be("Lease lapsed while running!");
    }

    [Fact]
    public async Task reclaim_stalled_time_jobs_leaves_a_healthy_renewing_job_untouched()
    {
        var (provider, _) = _Create();
        var job = _TimeJob(JobStatus.InProgress, _NodeA, _Now.UtcDateTime.AddMinutes(10)); // valid future lease
        await provider.AddTimeJobsAsync([job], AbortToken);

        var affected = await provider.ReclaimStalledTimeJobsAsync(AbortToken);

        affected.Should().Be(0);
        var untouched = await provider.GetTimeJobByIdAsync(job.Id, AbortToken);
        untouched!.Status.Should().Be(JobStatus.InProgress);
        untouched.Lease.Holder.Should().Be(_NodeA);
    }

    [Fact]
    public async Task reclaim_stalled_time_jobs_is_idempotent()
    {
        var (provider, _) = _Create();
        var job = _TimeJob(JobStatus.InProgress, _NodeA, _Now.UtcDateTime.AddMinutes(-1), NodeDeathPolicy.Retry);
        await provider.AddTimeJobsAsync([job], AbortToken);

        (await provider.ReclaimStalledTimeJobsAsync(AbortToken)).Should().Be(1);
        (await provider.ReclaimStalledTimeJobsAsync(AbortToken)).Should().Be(0); // already reclaimed -> no-op
    }

    // ── Node-death sweep defers InProgress to the lease ──────────────────────────────────────────────────────

    [Fact]
    public async Task dead_node_sweep_leaves_a_valid_lease_inprogress_row_to_the_lease()
    {
        var (provider, _) = _Create();
        var job = _TimeJob(JobStatus.InProgress, _NodeA, _Now.UtcDateTime.AddMinutes(10)); // still-valid lease
        await provider.AddTimeJobsAsync([job], AbortToken);

        var affected = await provider.ReleaseDeadNodeTimeJobResourcesAsync(_NodeA, AbortToken);

        affected.Should().Be(0); // the stalled-job reclaim path handles it once the lease lapses
        var row = await provider.GetTimeJobByIdAsync(job.Id, AbortToken);
        row!.Status.Should().Be(JobStatus.InProgress);
        row.Lease.Holder.Should().Be(_NodeA);
    }

    [Fact]
    public async Task dead_node_sweep_reclaims_a_lapsed_lease_inprogress_row_per_policy()
    {
        var (provider, _) = _Create();
        var job = _TimeJob(JobStatus.InProgress, _NodeA, _Now.UtcDateTime.AddMinutes(-1), NodeDeathPolicy.Retry);
        await provider.AddTimeJobsAsync([job], AbortToken);

        var affected = await provider.ReleaseDeadNodeTimeJobResourcesAsync(_NodeA, AbortToken);

        affected.Should().Be(1);
        var row = await provider.GetTimeJobByIdAsync(job.Id, AbortToken);
        row!.Status.Should().Be(JobStatus.Idle);
        row.Lease.State.Should().NotBe(RowLeaseState.Active);
    }

    [Fact]
    public async Task dead_node_sweep_reclaims_idle_and_queued_immediately_regardless_of_lease()
    {
        var (provider, _) = _Create();
        var idle = _TimeJob(JobStatus.Idle, _NodeA, _Now.UtcDateTime.AddMinutes(10));
        var queued = _TimeJob(JobStatus.Queued, _NodeA, _Now.UtcDateTime.AddMinutes(10));
        await provider.AddTimeJobsAsync([idle, queued], AbortToken);

        var affected = await provider.ReleaseDeadNodeTimeJobResourcesAsync(_NodeA, AbortToken);

        affected.Should().Be(2);
        (await provider.GetTimeJobByIdAsync(idle.Id, AbortToken))!.Lease.State.Should().NotBe(RowLeaseState.Active);
        (await provider.GetTimeJobByIdAsync(queued.Id, AbortToken))!.Lease.State.Should().NotBe(RowLeaseState.Active);
    }

    // ── Claim→start ownership recheck ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task unified_context_update_does_not_stamp_a_row_reclaimed_by_another_owner()
    {
        var (provider, _) = _Create(); // this node is NodeA
        var job = _TimeJob(JobStatus.Queued, _NodeB, _Now.UtcDateTime.AddMinutes(1)); // re-claimed by NodeB before we start it
        await provider.AddTimeJobsAsync([job], AbortToken);

        var unified = new JobExecutionState { FunctionName = "fn", ClaimOwner = _NodeA }.SetProperty(
            x => x.Status,
            JobStatus.InProgress
        );
        var stampedIds = await provider.UpdateTimeJobsWithUnifiedContextAsync([_Claimed(job)], unified, AbortToken);

        stampedIds.Should().BeEmpty();
        var row = await provider.GetTimeJobByIdAsync(job.Id, AbortToken);
        row!.Status.Should().Be(JobStatus.Queued); // untouched — still NodeB's
        row.Lease.Holder.Should().Be(_NodeB);
    }

    [Fact]
    public async Task unified_context_update_stamps_a_still_owned_row_inprogress()
    {
        var (provider, _) = _Create(); // this node is NodeA
        var job = _TimeJob(JobStatus.Queued, _NodeA, _Now.UtcDateTime.AddMinutes(1));
        await provider.AddTimeJobsAsync([job], AbortToken);

        var unified = new JobExecutionState { FunctionName = "fn", ClaimOwner = _NodeA }.SetProperty(
            x => x.Status,
            JobStatus.InProgress
        );
        var stampedIds = await provider.UpdateTimeJobsWithUnifiedContextAsync([_Claimed(job)], unified, AbortToken);

        stampedIds.Should().Equal(job.Id);
        (await provider.GetTimeJobByIdAsync(job.Id, AbortToken))!.Status.Should().Be(JobStatus.InProgress);
    }

    [Fact]
    public async Task unified_context_update_does_not_restamp_an_already_running_row()
    {
        var (provider, _) = _Create();
        var job = _TimeJob(JobStatus.InProgress, _NodeA, _Now.UtcDateTime.AddMinutes(1));
        await provider.AddTimeJobsAsync([job], AbortToken);
        var unified = new JobExecutionState { FunctionName = "fn", ClaimOwner = _NodeA }.SetProperty(
            x => x.Status,
            JobStatus.InProgress
        );

        var stampedIds = await provider.UpdateTimeJobsWithUnifiedContextAsync([_Claimed(job)], unified, AbortToken);

        stampedIds.Should().BeEmpty();
    }

    // ── Settling writes fence on the claim owner ─────────────────────────────────────────────────────────────
    // The in-memory store has no membership; its live identity is the configured node id. A row stamped with an
    // owner other than that id stands in for a run whose node identity is gone (left, fenced, or restarted).

    [Fact]
    public async Task completion_with_the_claim_owner_persists_when_it_is_no_longer_the_live_identity()
    {
        var (provider, _) = _Create(); // live identity is NodeA
        const string claimOwner = "node-a@1";
        var job = _TimeJob(JobStatus.InProgress, claimOwner, _Now.UtcDateTime.AddMinutes(1));
        await provider.AddTimeJobsAsync([job], AbortToken);

        var completion = new JobExecutionState
        {
            FunctionName = "fn",
            JobId = job.Id,
            ClaimOwner = claimOwner,
            ClaimGeneration = _ClaimGeneration,
        }.SetProperty(x => x.Status, JobStatus.Succeeded);

        (await provider.UpdateTimeJobAsync(completion, AbortToken)).Should().Be(1);
        (await provider.GetTimeJobByIdAsync(job.Id, AbortToken))!.Status.Should().Be(JobStatus.Succeeded);
    }

    [Fact]
    public async Task completion_with_the_claim_owner_matches_no_row_once_a_peer_reclaimed_it()
    {
        var (provider, _) = _Create();
        var released = _TimeJob(JobStatus.Idle, owner: null, lockedUntil: null);
        var reclaimed = _TimeJob(JobStatus.InProgress, _NodeB, _Now.UtcDateTime.AddMinutes(1));
        await provider.AddTimeJobsAsync([released, reclaimed], AbortToken);

        foreach (var id in new[] { released.Id, reclaimed.Id })
        {
            var completion = new JobExecutionState
            {
                FunctionName = "fn",
                JobId = id,
                ClaimOwner = _NodeA,
                ClaimGeneration = _ClaimGeneration,
            }.SetProperty(x => x.Status, JobStatus.Succeeded);

            (await provider.UpdateTimeJobAsync(completion, AbortToken)).Should().Be(0);
        }

        var releasedRow = (await provider.GetTimeJobByIdAsync(released.Id, AbortToken))!;
        releasedRow.Status.Should().Be(JobStatus.Idle);
        releasedRow.Lease.State.Should().NotBe(RowLeaseState.Active);
        var reclaimedRow = (await provider.GetTimeJobByIdAsync(reclaimed.Id, AbortToken))!;
        reclaimedRow.Status.Should().Be(JobStatus.InProgress);
        reclaimedRow.Lease.Holder.Should().Be(_NodeB);
    }

    [Fact]
    public async Task renewal_is_lost_when_the_claim_owner_is_no_longer_the_live_identity()
    {
        var (provider, _) = _Create(); // live identity is NodeA
        const string claimOwner = "node-a@1";
        var lease = _Now.UtcDateTime.AddMinutes(1);
        var job = _TimeJob(JobStatus.InProgress, claimOwner, lease);
        await provider.AddTimeJobsAsync([job], AbortToken);

        (await provider.RenewTimeJobLeaseAsync(_Claimed(job), claimOwner, AbortToken)).Should().Be(0);

        var row = (await provider.GetTimeJobByIdAsync(job.Id, AbortToken))!;
        row.Lease.ExpiresAt.Should().Be(new DateTimeOffset(lease));
        row.Lease.Holder.Should().Be(claimOwner);
    }

    [Fact]
    public async Task admission_matches_no_row_when_the_claim_owner_is_no_longer_the_live_identity()
    {
        var (provider, _) = _Create(); // live identity is NodeA
        const string claimOwner = "node-a@1";
        var job = _TimeJob(JobStatus.Queued, claimOwner, _Now.UtcDateTime.AddMinutes(1));
        await provider.AddTimeJobsAsync([job], AbortToken);

        var start = new JobExecutionState { FunctionName = "fn", ClaimOwner = claimOwner }.SetProperty(
            x => x.Status,
            JobStatus.InProgress
        );

        (await provider.UpdateTimeJobsWithUnifiedContextAsync([_Claimed(job)], start, AbortToken)).Should().BeEmpty();
        (await provider.GetTimeJobByIdAsync(job.Id, AbortToken))!.Status.Should().Be(JobStatus.Queued);
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
            Lease = TestLeases.Held(owner, lockedUntil, _GenerationHeldBy(owner)),
            OnNodeDeath = policy,
            ExecutionTime = _Now.UtcDateTime.AddMinutes(-2),
            CronJobId = Guid.NewGuid(),
        };
        occurrence.CronJob = new FakeCronJob
        {
            Id = occurrence.CronJobId,
            Function = "fn",
            Expression = "* * * * *",
        };
        await provider.InsertCronJobsAsync([occurrence.CronJob], AbortToken);
        await provider.InsertCronJobOccurrencesAsync([occurrence], AbortToken);
        return occurrence.Id;
    }

    [Fact]
    public async Task queue_timed_out_cron_jobs_does_not_steal_a_live_main_scheduler_claim()
    {
        var (provider, time) = _Create();
        var id = await _SeedCronOccurrence(provider, JobStatus.Queued, _NodeA, _Now.UtcDateTime.Add(_Lease));

        var whileLeased = await provider.QueueTimedOutCronJobOccurrencesAsync(AbortToken).ToListAsync(AbortToken);

        whileLeased.Should().BeEmpty();

        time.Advance(_Lease.Add(TimeSpan.FromSeconds(1)));
        var afterExpiry = await provider.QueueTimedOutCronJobOccurrencesAsync(AbortToken).ToListAsync(AbortToken);

        afterExpiry.Should().ContainSingle().Which.Id.Should().Be(id);
        afterExpiry[0].Status.Should().Be(JobStatus.Queued);

        var start = new JobExecutionState { FunctionName = "fn", ClaimOwner = _NodeA }.SetProperty(
            x => x.Status,
            JobStatus.InProgress
        );
        var claimedGeneration = afterExpiry[0].Lease.Generation;
        (
            await provider.UpdateCronJobOccurrencesWithUnifiedContextAsync(
                [JobsLeaseTables.CronOccurrence(id, claimedGeneration)],
                start,
                AbortToken
            )
        )
            .Should()
            .Equal(id);
        (await provider.GetAllCronJobOccurrencesAsync(x => x.Id == id, AbortToken))
            .Should()
            .ContainSingle()
            .Which.Status.Should()
            .Be(JobStatus.InProgress);
    }

    [Fact]
    public async Task renew_cron_job_occurrence_lease_returns_zero_when_owner_changed()
    {
        var (provider, _) = _Create();
        var id = await _SeedCronOccurrence(provider, JobStatus.InProgress, _NodeB, _Now.UtcDateTime.AddMinutes(1));

        (await provider.RenewCronJobOccurrenceLeaseAsync(_ClaimedOccurrence(id), _NodeA, AbortToken)).Should().Be(0);
    }

    [Fact]
    public async Task reclaim_stalled_cron_job_occurrences_releases_lapsed_retry_to_idle()
    {
        var (provider, _) = _Create();
        var id = await _SeedCronOccurrence(provider, JobStatus.InProgress, _NodeA, _Now.UtcDateTime.AddMinutes(-1));

        (await provider.ReclaimStalledCronJobOccurrencesAsync(AbortToken)).Should().Be(1);
        var rows = await provider.GetAllCronJobOccurrencesAsync(x => x.Id == id, AbortToken);
        rows.Should().ContainSingle().Which.Status.Should().Be(JobStatus.Idle);
    }

    [Fact]
    public async Task reclaim_stalled_cron_job_occurrences_mark_failed_terminalizes_lapsed_row()
    {
        // #23: cron mirror of the time-job MarkFailed reclaim — parity gap that had no unit coverage.
        var (provider, _) = _Create();
        var id = await _SeedCronOccurrence(
            provider,
            JobStatus.InProgress,
            _NodeA,
            _Now.UtcDateTime.AddMinutes(-1),
            NodeDeathPolicy.MarkFailed
        );

        await provider.ReclaimStalledCronJobOccurrencesAsync(AbortToken);

        var row = (await provider.GetAllCronJobOccurrencesAsync(x => x.Id == id, AbortToken))
            .Should()
            .ContainSingle()
            .Subject;
        row.Status.Should().Be(JobStatus.Failed);
        row.Lease.State.Should().NotBe(RowLeaseState.Active);
        row.ExceptionMessage.Should().Be("Lease lapsed while running!");
    }

    [Fact]
    public async Task reclaim_stalled_cron_job_occurrences_skip_terminalizes_lapsed_row()
    {
        // #23: cron mirror of the time-job Skip reclaim.
        var (provider, _) = _Create();
        var id = await _SeedCronOccurrence(
            provider,
            JobStatus.InProgress,
            _NodeA,
            _Now.UtcDateTime.AddMinutes(-1),
            NodeDeathPolicy.Skip
        );

        await provider.ReclaimStalledCronJobOccurrencesAsync(AbortToken);

        var row = (await provider.GetAllCronJobOccurrencesAsync(x => x.Id == id, AbortToken))
            .Should()
            .ContainSingle()
            .Subject;
        row.Status.Should().Be(JobStatus.Skipped);
        row.Lease.State.Should().NotBe(RowLeaseState.Active);
        row.SkippedReason.Should().Be("Lease lapsed while running!");
    }

    [Fact]
    public async Task renew_cron_job_occurrence_lease_returns_zero_for_an_owned_queued_row()
    {
        // #13 (cron): renewal slides a RUNNING occurrence lease only.
        var (provider, _) = _Create();
        var id = await _SeedCronOccurrence(provider, JobStatus.Queued, _NodeA, _Now.UtcDateTime.AddMinutes(1));

        (await provider.RenewCronJobOccurrenceLeaseAsync(_ClaimedOccurrence(id), _NodeA, AbortToken)).Should().Be(0);
    }

    [Fact]
    public async Task queue_cron_job_occurrences_re_queue_restamps_on_node_death_from_the_cron_definition()
    {
        // #464: re-queuing an existing occurrence re-stamps OnNodeDeath from the cron def (context), not the stored
        // value, so EF and in-memory agree and a mid-flight policy edit takes effect.
        var (provider, _) = _Create();
        var occId = await _SeedCronOccurrence(
            provider,
            JobStatus.Queued,
            _NodeA,
            _Now.UtcDateTime.AddMinutes(1),
            NodeDeathPolicy.Retry
        );

        var storedBeforeQueue = await provider.GetAllCronJobOccurrencesAsync(x => x.Id == occId, AbortToken);
        var context = new JobManagerDispatchContext(storedBeforeQueue.Single().CronJobId)
        {
            FunctionName = "fn",
            Expression = "* * * * *",
            OnNodeDeath = NodeDeathPolicy.Skip, // policy changed on the def since the occurrence was created
            NextCronOccurrence = new NextCronOccurrence(occId, _Now),
        };

        var yielded = new List<CronJobOccurrenceEntity<FakeCronJob>>();
        await foreach (
            var occurrence in provider.QueueCronJobOccurrencesAsync(
                (_Now.UtcDateTime.AddMinutes(-2), [context]),
                AbortToken
            )
        )
        {
            yielded.Add(occurrence);
        }

        yielded.Should().ContainSingle().Which.OnNodeDeath.Should().Be(NodeDeathPolicy.Skip);
        var stored = await provider.GetAllCronJobOccurrencesAsync(x => x.Id == occId, AbortToken);
        stored.Should().ContainSingle().Which.OnNodeDeath.Should().Be(NodeDeathPolicy.Skip);
    }

    // ── A lapsed claim that never started is reclaimable under every policy ─────────────────────────────────

    [Theory]
    [InlineData(NodeDeathPolicy.Retry)]
    [InlineData(NodeDeathPolicy.MarkFailed)]
    [InlineData(NodeDeathPolicy.Skip)]
    public async Task acquire_reclaims_a_lapsed_queued_row_of_another_owner_under_every_policy(NodeDeathPolicy policy)
    {
        var (provider, _) = _Create();
        var job = _TimeJob(JobStatus.Queued, _NodeB, _Now.UtcDateTime.AddMinutes(-1), policy);
        await provider.AddTimeJobsAsync([job], AbortToken);

        var claimed = await provider.AcquireImmediateTimeJobsAsync([job.Id], AbortToken);

        claimed.Should().ContainSingle().Which.Lease.Holder.Should().Be(_NodeA);
    }

    [Theory]
    [InlineData(NodeDeathPolicy.Retry)]
    [InlineData(NodeDeathPolicy.MarkFailed)]
    [InlineData(NodeDeathPolicy.Skip)]
    public async Task fallback_reclaims_a_lapsed_queued_row_of_another_owner_under_every_policy(NodeDeathPolicy policy)
    {
        var (provider, _) = _Create();
        var job = _TimeJob(JobStatus.Queued, _NodeB, _Now.UtcDateTime.AddMinutes(-1), policy);
        await provider.AddTimeJobsAsync([job], AbortToken);

        var claimed = await provider.QueueTimedOutTimeJobsAsync(AbortToken).ToArrayAsync(AbortToken);

        claimed.Should().ContainSingle().Which.Lease.Holder.Should().Be(_NodeA);
    }

    [Theory]
    [InlineData(NodeDeathPolicy.MarkFailed)]
    [InlineData(NodeDeathPolicy.Skip)]
    public async Task acquire_leaves_a_live_queued_row_of_another_owner(NodeDeathPolicy policy)
    {
        var (provider, _) = _Create();
        var job = _TimeJob(JobStatus.Queued, _NodeB, _Now.UtcDateTime.AddMinutes(1), policy);
        await provider.AddTimeJobsAsync([job], AbortToken);

        var claimed = await provider.AcquireImmediateTimeJobsAsync([job.Id], AbortToken);

        claimed.Should().BeEmpty();
    }
}
