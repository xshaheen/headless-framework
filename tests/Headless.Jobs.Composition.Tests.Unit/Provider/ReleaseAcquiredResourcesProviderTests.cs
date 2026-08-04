// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Jobs;
using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Provider;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Tests.Provider;

/// <summary>
/// Pins the release contract: release is scoped to THIS owner's Queued (claimed-but-not-started) rows on both
/// the explicit-id and the empty-id (release everything) forms. It must never sweep foreign or unowned rows —
/// the empty form previously combined with the acquire predicate and released every claimable row cluster-wide —
/// and must never strip Idle owned rows, which are a running chain's claimed descendants.
/// </summary>
public sealed class ReleaseAcquiredResourcesProviderTests : TestBase
{
    private sealed class FakeTimeJob : TimeJobEntity<FakeTimeJob>;

    private sealed class FakeCronJob : CronJobEntity;

    private const string _NodeA = "node-a";
    private const string _NodeB = "node-b";
    private static readonly DateTimeOffset _Now = new(2026, 06, 17, 12, 00, 00, TimeSpan.Zero);
    private static readonly TimeSpan _Lease = TimeSpan.FromMinutes(5);

    [Fact]
    public async Task release_all_releases_only_this_owners_queued_time_jobs()
    {
        var (provider, _) = _Create();
        var mineQueued = _TimeJob(JobStatus.Queued, _NodeA, _Now.UtcDateTime.Add(_Lease));
        var mineInProgress = _TimeJob(JobStatus.InProgress, _NodeA, _Now.UtcDateTime.Add(_Lease));
        var mineIdleDescendant = _TimeJob(JobStatus.Idle, _NodeA, _Now.UtcDateTime.Add(_Lease));
        var foreignQueued = _TimeJob(JobStatus.Queued, _NodeB, _Now.UtcDateTime.Add(_Lease));
        var unownedIdle = _TimeJob(JobStatus.Idle, owner: null, lockedUntil: null);
        await provider.AddTimeJobsAsync(
            [mineQueued, mineInProgress, mineIdleDescendant, foreignQueued, unownedIdle],
            AbortToken
        );
        var unownedBefore = await provider.GetTimeJobByIdAsync(unownedIdle.Id, AbortToken);

        await provider.ReleaseAcquiredTimeJobsAsync([], AbortToken);

        var released = await provider.GetTimeJobByIdAsync(mineQueued.Id, AbortToken);
        released!.Status.Should().Be(JobStatus.Idle);
        released.OwnerId.Should().BeNull();
        released.LockedUntil.Should().BeNull();

        (await provider.GetTimeJobByIdAsync(mineInProgress.Id, AbortToken))!
            .Status.Should()
            .Be(JobStatus.InProgress, "running work is never released");
        var descendant = await provider.GetTimeJobByIdAsync(mineIdleDescendant.Id, AbortToken);
        descendant!.OwnerId.Should().Be(_NodeA, "Idle owned rows are a running chain's claimed descendants");
        var foreign = await provider.GetTimeJobByIdAsync(foreignQueued.Id, AbortToken);
        foreign!.OwnerId.Should().Be(_NodeB, "a release must never touch another node's claim");
        foreign.Status.Should().Be(JobStatus.Queued);
        var unownedAfter = await provider.GetTimeJobByIdAsync(unownedIdle.Id, AbortToken);
        unownedAfter!
            .UpdatedAt.Should()
            .Be(unownedBefore!.UpdatedAt, "an unowned row must not have its CAS token bumped by a foreign release");
    }

    [Fact]
    public async Task release_with_ids_releases_only_listed_queued_rows_of_this_owner()
    {
        var (provider, _) = _Create();
        var listedMine = _TimeJob(JobStatus.Queued, _NodeA, _Now.UtcDateTime.Add(_Lease));
        var unlistedMine = _TimeJob(JobStatus.Queued, _NodeA, _Now.UtcDateTime.Add(_Lease));
        var listedForeign = _TimeJob(JobStatus.Queued, _NodeB, _Now.UtcDateTime.Add(_Lease));
        await provider.AddTimeJobsAsync([listedMine, unlistedMine, listedForeign], AbortToken);

        await provider.ReleaseAcquiredTimeJobsAsync([listedMine.Id, listedForeign.Id], AbortToken);

        (await provider.GetTimeJobByIdAsync(listedMine.Id, AbortToken))!.Status.Should().Be(JobStatus.Idle);
        (await provider.GetTimeJobByIdAsync(unlistedMine.Id, AbortToken))!.Status.Should().Be(JobStatus.Queued);
        var foreign = await provider.GetTimeJobByIdAsync(listedForeign.Id, AbortToken);
        foreign!.Status.Should().Be(JobStatus.Queued);
        foreign.OwnerId.Should().Be(_NodeB);
    }

    [Fact]
    public async Task release_all_releases_only_this_owners_queued_cron_occurrences()
    {
        var (provider, _) = _Create();
        var mineQueued = _Occurrence(JobStatus.Queued, _NodeA);
        var mineInProgress = _Occurrence(JobStatus.InProgress, _NodeA);
        var foreignQueued = _Occurrence(JobStatus.Queued, _NodeB);
        await provider.InsertCronJobOccurrencesAsync([mineQueued, mineInProgress, foreignQueued], AbortToken);

        await provider.ReleaseAcquiredCronJobOccurrencesAsync([], AbortToken);

        var all = await provider.GetAllCronJobOccurrencesAsync(predicate: null, AbortToken);
        var released = all.Single(x => x.Id == mineQueued.Id);
        released.Status.Should().Be(JobStatus.Idle);
        released.OwnerId.Should().BeNull();
        all.Single(x => x.Id == mineInProgress.Id).Status.Should().Be(JobStatus.InProgress);
        var foreign = all.Single(x => x.Id == foreignQueued.Id);
        foreign.OwnerId.Should().Be(_NodeB);
        foreign.Status.Should().Be(JobStatus.Queued);
    }

    [Fact]
    public async Task get_active_owner_ids_returns_distinct_owners_of_non_terminal_rows_only()
    {
        var (provider, _) = _Create();
        var mineQueued = _TimeJob(JobStatus.Queued, _NodeA, _Now.UtcDateTime.AddMinutes(5));
        var mineInProgress = _TimeJob(JobStatus.InProgress, _NodeA, _Now.UtcDateTime.AddMinutes(5));
        var otherIdle = _TimeJob(JobStatus.Idle, _NodeB, _Now.UtcDateTime.AddMinutes(5));
        var terminalOwned = _TimeJob(JobStatus.Failed, "gone@1", lockedUntil: null);
        var unowned = _TimeJob(JobStatus.Idle, owner: null, lockedUntil: null);
        await provider.AddTimeJobsAsync([mineQueued, mineInProgress, otherIdle, terminalOwned, unowned], AbortToken);
        var cronOwned = _Occurrence(JobStatus.InProgress, "cron-owner@9");
        await provider.InsertCronJobOccurrencesAsync([cronOwned], AbortToken);

        var owners = await provider.GetActiveOwnerIdsAsync(AbortToken);

        owners
            .Should()
            .BeEquivalentTo(
                [_NodeA, _NodeB, "cron-owner@9"],
                "terminal and unowned rows carry no reclaimable ownership"
            );
    }

    [Fact]
    public async Task remove_time_jobs_removes_the_whole_descendant_subtree()
    {
        // One-level removal orphaned grandchildren whose parent-gate lookup could never resolve again —
        // stranded Idle forever.
        var (provider, _) = _Create();
        var root = _TimeJob(JobStatus.Idle, owner: null, lockedUntil: null);
        var child = _TimeJob(JobStatus.Idle, owner: null, lockedUntil: null);
        child.ParentId = root.Id;
        var grandChild = _TimeJob(JobStatus.Idle, owner: null, lockedUntil: null);
        grandChild.ParentId = child.Id;
        var greatGrandChild = _TimeJob(JobStatus.Idle, owner: null, lockedUntil: null);
        greatGrandChild.ParentId = grandChild.Id;
        await provider.AddTimeJobsAsync([root, child, grandChild, greatGrandChild], AbortToken);

        var removed = await provider.RemoveTimeJobsAsync([root.Id], AbortToken);

        removed.Should().Be(4, "the whole subtree is deleted, not just root and direct children");
        (await provider.GetTimeJobByIdAsync(grandChild.Id, AbortToken)).Should().BeNull();
        (await provider.GetTimeJobByIdAsync(greatGrandChild.Id, AbortToken)).Should().BeNull();
    }

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

    private static FakeTimeJob _TimeJob(JobStatus status, string? owner, DateTime? lockedUntil)
    {
        return new FakeTimeJob
        {
            Id = Guid.NewGuid(),
            Function = "fn",
            Status = status,
            OwnerId = owner,
            LockedUntil = lockedUntil,
            OnNodeDeath = NodeDeathPolicy.Retry,
            ExecutionTime = _Now.UtcDateTime.AddMinutes(-2),
        };
    }

    private static CronJobOccurrenceEntity<FakeCronJob> _Occurrence(JobStatus status, string? owner)
    {
        return new CronJobOccurrenceEntity<FakeCronJob>
        {
            Id = Guid.NewGuid(),
            Status = status,
            OwnerId = owner,
            LockedUntil = _Now.UtcDateTime.Add(_Lease),
            OnNodeDeath = NodeDeathPolicy.Retry,
            ExecutionTime = _Now.UtcDateTime.AddMinutes(-1),
            CronJobId = Guid.NewGuid(),
            CronJob = new FakeCronJob { Function = "fn", Expression = "* * * * *" },
        };
    }
}
