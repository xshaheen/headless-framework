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
/// U5/KTD3: a timed chain descendant (<c>ExecutionTime != null</c>, <c>ParentId != null</c>) is claimable only at
/// the later of its parent's matching terminal state and its own execution time; a non-matching parent terminal
/// skips it with its subtree. This inverts the pre-#311 behavior where a timed child fired unconditionally at its
/// time. Proven here on the in-memory provider (gate + release/skip reconcile + poll-time safety net); cross-provider
/// parity and the native SQL gate land in the EF harness (U7).
/// </summary>
public sealed class TimedDescendantGatingProviderTests : TestBase
{
    private sealed class FakeTimeJob : TimeJobEntity<FakeTimeJob>;

    private sealed class FakeCronJob : CronJobEntity;

    private const string _NodeA = "node-a";
    private static readonly DateTime _Now = new(2026, 06, 17, 12, 00, 00, DateTimeKind.Utc);
    private static readonly TimeSpan _Lease = TimeSpan.FromMinutes(5);

    private static (JobsInMemoryPersistenceProvider<FakeTimeJob, FakeCronJob> Provider, FakeTimeProvider Time) _Create()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(_Now, TimeSpan.Zero));
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(time);
        services.AddHeadlessGuidGenerator();
        services.AddSingleton(new SchedulerOptionsBuilder { NodeId = _NodeA, LeaseDuration = _Lease });
        var sp = services.BuildServiceProvider();
        return (new JobsInMemoryPersistenceProvider<FakeTimeJob, FakeCronJob>(sp), time);
    }

    private static FakeTimeJob _Parent(JobStatus status, Guid? id = null) =>
        new()
        {
            Id = id ?? Guid.NewGuid(),
            Function = "parent",
            Status = status,
            ExecutionTime = _Now.AddMinutes(-1),
        };

    private static FakeTimeJob _TimedChild(
        Guid parentId,
        RunCondition condition,
        DateTime executionTime,
        Guid? id = null
    ) =>
        new()
        {
            Id = id ?? Guid.NewGuid(),
            Function = "child",
            Status = JobStatus.Idle,
            ParentId = parentId,
            RunCondition = condition,
            ExecutionTime = executionTime,
        };

    private static FakeTimeJob _NonTimedChild(Guid parentId, RunCondition condition, Guid? id = null) =>
        new()
        {
            Id = id ?? Guid.NewGuid(),
            Function = "descendant",
            Status = JobStatus.Idle,
            ParentId = parentId,
            RunCondition = condition,
            ExecutionTime = null,
        };

    // ----- AE8: claim gate -----

    [Fact]
    public async Task timed_child_is_not_claimable_while_parent_is_running_even_when_its_time_is_due()
    {
        var (provider, _) = _Create();
        var parent = _Parent(JobStatus.InProgress);
        var child = _TimedChild(parent.Id, RunCondition.OnSuccess, _Now);
        await provider.AddTimeJobsAsync([parent, child], AbortToken);

        var peeked = await provider.GetEarliestTimeJobsAsync(AbortToken);

        peeked.Should().NotContain(x => x.Id == child.Id, "the parent has not reached its matching terminal state");
    }

    [Fact]
    public async Task timed_child_becomes_claimable_after_parent_success_once_its_time_is_due()
    {
        var (provider, _) = _Create();
        var parent = _Parent(JobStatus.Succeeded);
        var child = _TimedChild(parent.Id, RunCondition.OnSuccess, _Now);
        await provider.AddTimeJobsAsync([parent, child], AbortToken);

        var peeked = await provider.GetEarliestTimeJobsAsync(AbortToken);

        peeked.Should().ContainSingle(x => x.Id == child.Id, "parent Succeeded matches the child's OnSuccess gate");
    }

    [Fact]
    public async Task timed_child_is_not_immediately_acquirable_while_parent_is_running()
    {
        var (provider, _) = _Create();
        var parent = _Parent(JobStatus.InProgress);
        var child = _TimedChild(parent.Id, RunCondition.OnSuccess, _Now);
        await provider.AddTimeJobsAsync([parent, child], AbortToken);

        var acquired = await provider.AcquireImmediateTimeJobsAsync([child.Id], AbortToken);

        acquired.Should().BeEmpty("the immediate-acquire path is gated too");
    }

    [Fact]
    public async Task timed_child_is_not_claimable_by_the_fallback_while_parent_is_running()
    {
        var (provider, _) = _Create();
        var parent = _Parent(JobStatus.InProgress);
        // Aged past the fallback window so only the timed-out path could claim it.
        var child = _TimedChild(parent.Id, RunCondition.OnSuccess, _Now.AddMinutes(-2));
        await provider.AddTimeJobsAsync([parent, child], AbortToken);

        var claimed = await provider.QueueTimedOutTimeJobsAsync(AbortToken).ToListAsync(AbortToken);

        claimed.Should().NotContain(x => x.Id == child.Id, "the fallback selects timed rows directly and is gated");
    }

    // ----- AE8: release re-stamp -----

    [Fact]
    public async Task reconcile_restamps_a_past_due_matching_child_so_the_main_peek_claims_it_promptly()
    {
        var (provider, _) = _Create();
        var parent = _Parent(JobStatus.Succeeded);
        // Past due AND older than the 1-second staleness window, so the main peek would ignore it un-restamped.
        var child = _TimedChild(parent.Id, RunCondition.OnSuccess, _Now.AddMinutes(-2));
        await provider.AddTimeJobsAsync([parent, child], AbortToken);

        (await provider.GetEarliestTimeJobsAsync(AbortToken))
            .Should()
            .NotContain(x => x.Id == child.Id, "the past-due child is stale until it is re-stamped");

        var earliest = await provider.ApplyParentTerminalRunConditionsAsync(parent.Id, AbortToken);

        earliest.Should().Be(_Now, "a released past-due child is re-stamped to now");
        var stored = await provider.GetTimeJobByIdAsync(child.Id, AbortToken);
        stored!.ExecutionTime.Should().Be(_Now);
        (await provider.GetEarliestTimeJobsAsync(AbortToken))
            .Should()
            .ContainSingle(x => x.Id == child.Id, "the re-stamped child is now within the main peek window");
    }

    [Fact]
    public async Task reconcile_leaves_a_future_matching_child_at_its_scheduled_time()
    {
        var (provider, _) = _Create();
        var parent = _Parent(JobStatus.Succeeded);
        var future = _Now.AddMinutes(5);
        var child = _TimedChild(parent.Id, RunCondition.OnSuccess, future);
        await provider.AddTimeJobsAsync([parent, child], AbortToken);

        var earliest = await provider.ApplyParentTerminalRunConditionsAsync(parent.Id, AbortToken);

        earliest.Should().Be(future, "a future matching child runs at its scheduled time, un-restamped");
        var stored = await provider.GetTimeJobByIdAsync(child.Id, AbortToken);
        stored!.ExecutionTime.Should().Be(future);
        stored.Status.Should().Be(JobStatus.Idle);
    }

    // ----- AE9: skip on non-matching parent terminal -----

    [Fact]
    public async Task parent_failure_skips_a_timed_on_success_child_and_its_whole_subtree()
    {
        var (provider, _) = _Create();
        var parent = _Parent(JobStatus.Failed);
        var child = _TimedChild(parent.Id, RunCondition.OnSuccess, _Now);
        var grandChild = _NonTimedChild(child.Id, RunCondition.OnSuccess);
        var greatGrandChild = _TimedChild(grandChild.Id, RunCondition.OnSuccess, _Now.AddMinutes(1));
        await provider.AddTimeJobsAsync([parent, child, grandChild, greatGrandChild], AbortToken);

        var earliest = await provider.ApplyParentTerminalRunConditionsAsync(parent.Id, AbortToken);

        earliest.Should().BeNull("nothing was released — the whole branch was skipped");
        foreach (var skipped in new[] { child, grandChild, greatGrandChild })
        {
            var stored = await provider.GetTimeJobByIdAsync(skipped.Id, AbortToken);
            stored!.Status.Should().Be(JobStatus.Skipped, $"{skipped.Function} is below a non-matching parent");
        }
    }

    [Fact]
    public async Task timed_catch_child_is_released_on_parent_failure_and_skipped_on_parent_success()
    {
        var (releasedProvider, _) = _Create();
        var failedParent = _Parent(JobStatus.Failed);
        var catchChild = _TimedChild(failedParent.Id, RunCondition.OnFailure, _Now.AddMinutes(-2));
        await releasedProvider.AddTimeJobsAsync([failedParent, catchChild], AbortToken);

        var earliest = await releasedProvider.ApplyParentTerminalRunConditionsAsync(failedParent.Id, AbortToken);

        earliest.Should().Be(_Now, "an OnFailure timed child is released when its parent failed");
        (await releasedProvider.GetTimeJobByIdAsync(catchChild.Id, AbortToken))!.Status.Should().Be(JobStatus.Idle);

        var (skippedProvider, _) = _Create();
        var succeededParent = _Parent(JobStatus.Succeeded);
        var idleCatch = _TimedChild(succeededParent.Id, RunCondition.OnFailure, _Now);
        await skippedProvider.AddTimeJobsAsync([succeededParent, idleCatch], AbortToken);

        await skippedProvider.ApplyParentTerminalRunConditionsAsync(succeededParent.Id, AbortToken);

        (await skippedProvider.GetTimeJobByIdAsync(idleCatch.Id, AbortToken))!
            .Status.Should()
            .Be(JobStatus.Skipped, "an OnFailure timed child is skipped when its parent succeeded");
    }

    // ----- set-based reconcile across all terminal parents (sweep follow-up) -----

    [Fact]
    public async Task set_based_reconcile_releases_matching_and_skips_non_matching_across_all_terminal_parents()
    {
        var (provider, _) = _Create();
        var succeededParent = _Parent(JobStatus.Succeeded);
        var failedParent = _Parent(JobStatus.Failed);
        var released = _TimedChild(succeededParent.Id, RunCondition.OnSuccess, _Now.AddMinutes(-2));
        var skippedChild = _TimedChild(failedParent.Id, RunCondition.OnSuccess, _Now);
        await provider.AddTimeJobsAsync([succeededParent, failedParent, released, skippedChild], AbortToken);

        var earliest = await provider.ApplyParentTerminalRunConditionsAsync(parentId: null, AbortToken);

        earliest.Should().Be(_Now, "the past-due matching child is released at now");
        (await provider.GetTimeJobByIdAsync(released.Id, AbortToken))!.Status.Should().Be(JobStatus.Idle);
        (await provider.GetTimeJobByIdAsync(released.Id, AbortToken))!.ExecutionTime.Should().Be(_Now);
        (await provider.GetTimeJobByIdAsync(skippedChild.Id, AbortToken))!.Status.Should().Be(JobStatus.Skipped);
    }

    // ----- poll-time skip-only safety net -----

    [Fact]
    public async Task safety_net_skips_a_stranded_non_matching_timed_child()
    {
        var (provider, _) = _Create();
        // Parent seeded directly terminal (simulating a terminalization path that never invoked the reconcile).
        var parent = _Parent(JobStatus.Failed);
        var child = _TimedChild(parent.Id, RunCondition.OnSuccess, _Now);
        await provider.AddTimeJobsAsync([parent, child], AbortToken);

        var skipped = await provider.SkipStrandedTimedChildrenAsync(AbortToken);

        skipped.Should().Be(1);
        (await provider.GetTimeJobByIdAsync(child.Id, AbortToken))!.Status.Should().Be(JobStatus.Skipped);
    }

    [Fact]
    public async Task safety_net_never_releases_a_matching_child()
    {
        var (provider, _) = _Create();
        var parent = _Parent(JobStatus.Succeeded);
        var child = _TimedChild(parent.Id, RunCondition.OnSuccess, _Now.AddMinutes(-2));
        await provider.AddTimeJobsAsync([parent, child], AbortToken);

        await provider.SkipStrandedTimedChildrenAsync(AbortToken);

        var stored = await provider.GetTimeJobByIdAsync(child.Id, AbortToken);
        stored!.Status.Should().Be(JobStatus.Idle, "the safety net is skip-only — it never makes a child eligible");
        stored.ExecutionTime.Should().Be(_Now.AddMinutes(-2), "and never re-stamps a matching child");
    }
}
