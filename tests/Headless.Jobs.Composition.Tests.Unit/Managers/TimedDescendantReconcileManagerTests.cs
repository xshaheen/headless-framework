// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Jobs;
using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Managers;
using Headless.Jobs.Models;
using Headless.Jobs.Provider;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Tests.Managers;

/// <summary>
/// U5/KTD3: the internal manager wraps the provider reconcile with the post-commit scheduler nudge
/// (<c>IJobsHostScheduler.RestartIfNeeded</c>) and runs the poll-time skip-only safety net inside
/// <c>GetNextJobs</c>. Uses the real in-memory provider so the release/skip semantics and the wiring are exercised
/// together; a substitute host scheduler captures the wake.
/// </summary>
public sealed class TimedDescendantReconcileManagerTests : TestBase
{
    private sealed class FakeTimeJob : TimeJobEntity<FakeTimeJob>;

    private sealed class FakeCronJob : CronJobEntity;

    private const string _NodeA = "node-a";
    private static readonly DateTime _Now = new(2026, 06, 17, 12, 00, 00, DateTimeKind.Utc);

    private static (
        InternalJobsManager<FakeTimeJob, FakeCronJob> Manager,
        JobsInMemoryPersistenceProvider<FakeTimeJob, FakeCronJob> Provider,
        IJobsHostScheduler Scheduler
    ) _Create()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(_Now, TimeSpan.Zero));
        var scheduler = Substitute.For<IJobsHostScheduler>();
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(time);
        services.AddHeadlessGuidGenerator();
        services.AddSingleton(new SchedulerOptionsBuilder { NodeId = _NodeA });
        services.AddSingleton(scheduler);
        var sp = services.BuildServiceProvider();

        var provider = new JobsInMemoryPersistenceProvider<FakeTimeJob, FakeCronJob>(sp);
        var manager = new InternalJobsManager<FakeTimeJob, FakeCronJob>(
            provider,
            time,
            Substitute.For<IJobsNotificationHubSender>(),
            new CronScheduleCache(TimeZoneInfo.Utc),
            NullLogger<InternalJobsManager<FakeTimeJob, FakeCronJob>>.Instance,
            JobsRequestSerializationOptions.Default,
            sp.GetRequiredService<IGuidGenerator>(),
            sp
        );

        return (manager, provider, scheduler);
    }

    private static FakeTimeJob _Parent(JobStatus status) =>
        new()
        {
            Id = Guid.NewGuid(),
            Function = "parent",
            Status = status,
            ExecutionTime = _Now.AddMinutes(-1),
        };

    private static FakeTimeJob _TimedChild(Guid parentId, RunCondition condition, DateTime executionTime) =>
        new()
        {
            Id = Guid.NewGuid(),
            Function = "child",
            Status = JobStatus.Idle,
            ParentId = parentId,
            RunCondition = condition,
            ExecutionTime = executionTime,
        };

    private static FakeTimeJob _NonTimedChild(Guid parentId, RunCondition condition) =>
        new()
        {
            Id = Guid.NewGuid(),
            Function = "descendant",
            Status = JobStatus.Idle,
            ParentId = parentId,
            RunCondition = condition,
            ExecutionTime = null,
        };

    [Fact]
    public async Task apply_parent_terminal_run_conditions_wakes_the_scheduler_for_the_released_child()
    {
        var (manager, provider, scheduler) = _Create();
        var parent = _Parent(JobStatus.Succeeded);
        var child = _TimedChild(parent.Id, RunCondition.OnSuccess, _Now.AddMinutes(-2));
        await provider.AddTimeJobsAsync([parent, child], AbortToken);

        await manager.ApplyParentTerminalRunConditionsAsync(parent.Id, AbortToken);

        // The provider commits the release before the manager nudges the scheduler with the earliest released time.
        scheduler.Received(1).RestartIfNeeded(_Now);
        (await provider.GetTimeJobByIdAsync(child.Id, AbortToken))!.ExecutionTime.Should().Be(_Now);
    }

    [Fact]
    public async Task apply_parent_terminal_run_conditions_does_not_wake_the_scheduler_when_nothing_is_released()
    {
        var (manager, provider, scheduler) = _Create();
        var parent = _Parent(JobStatus.Failed);
        var child = _TimedChild(parent.Id, RunCondition.OnSuccess, _Now);
        await provider.AddTimeJobsAsync([parent, child], AbortToken);

        await manager.ApplyParentTerminalRunConditionsAsync(parent.Id, AbortToken);

        scheduler.DidNotReceive().RestartIfNeeded(Arg.Any<DateTime?>());
        (await provider.GetTimeJobByIdAsync(child.Id, AbortToken))!.Status.Should().Be(JobStatus.Skipped);
    }

    [Fact]
    public async Task cancelling_an_idle_parent_skips_its_timed_on_success_children_with_their_subtree()
    {
        var (manager, provider, scheduler) = _Create();
        var parent = _Parent(JobStatus.Idle);
        var child = _TimedChild(parent.Id, RunCondition.OnSuccess, _Now);
        var grandChild = _NonTimedChild(child.Id, RunCondition.OnSuccess);
        await provider.AddTimeJobsAsync([parent, child, grandChild], AbortToken);

        var cancelled = await manager.RequestTimeJobCancellationAsync(parent.Id, AbortToken);

        cancelled.Should().BeTrue();
        (await provider.GetTimeJobByIdAsync(parent.Id, AbortToken))!.Status.Should().Be(JobStatus.Cancelled);
        // No timed child (nor its subtree) strands Idle behind the new gate.
        (await provider.GetTimeJobByIdAsync(child.Id, AbortToken))!
            .Status.Should()
            .Be(JobStatus.Skipped);
        (await provider.GetTimeJobByIdAsync(grandChild.Id, AbortToken))!.Status.Should().Be(JobStatus.Skipped);
        scheduler.DidNotReceive().RestartIfNeeded(Arg.Any<DateTime?>());
    }

    [Fact]
    public async Task cancelling_an_idle_parent_releases_a_matching_on_cancelled_child_and_wakes_the_scheduler()
    {
        var (manager, provider, scheduler) = _Create();
        var parent = _Parent(JobStatus.Idle);
        var child = _TimedChild(parent.Id, RunCondition.OnCancelled, _Now.AddMinutes(-2));
        await provider.AddTimeJobsAsync([parent, child], AbortToken);

        await manager.RequestTimeJobCancellationAsync(parent.Id, AbortToken);

        var stored = await provider.GetTimeJobByIdAsync(child.Id, AbortToken);
        stored!.Status.Should().Be(JobStatus.Idle, "OnCancelled matches a cancelled parent and is released");
        stored.ExecutionTime.Should().Be(_Now, "the past-due released child is re-stamped to now");
        // Finding 3: the cancellation-release earliest time reaches the same post-commit RestartIfNeeded wake.
        scheduler.Received(1).RestartIfNeeded(_Now);
    }

    [Fact]
    public async Task get_next_jobs_runs_the_poll_time_skip_only_safety_net()
    {
        var (manager, provider, scheduler) = _Create();
        // Parent seeded directly terminal — a terminalization path that never invoked the per-parent reconcile.
        var parent = _Parent(JobStatus.Failed);
        var child = _TimedChild(parent.Id, RunCondition.OnSuccess, _Now);
        await provider.AddTimeJobsAsync([parent, child], AbortToken);

        await manager.GetNextJobs(AbortToken);

        (await provider.GetTimeJobByIdAsync(child.Id, AbortToken))!
            .Status.Should()
            .Be(JobStatus.Skipped, "the poll-time safety net skips a stranded non-matching timed child");
        scheduler.DidNotReceive().RestartIfNeeded(Arg.Any<DateTime?>());
    }
}
