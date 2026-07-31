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
/// Pins the crash-durable retry budget on the reclaim side: reclaiming a STARTED attempt (InProgress with a
/// lapsed lease, Retry policy) consumes one budget unit by incrementing the persisted RetryCount, while
/// releasing claimed-but-unstarted (Idle/Queued) rows never touches the count. Without the increment a handler
/// that reliably kills its host is reclaimed and re-run forever with a fresh budget each cycle.
/// </summary>
public sealed class CrashRetryBudgetProviderTests : TestBase
{
    private sealed class FakeTimeJob : TimeJobEntity<FakeTimeJob>;

    private sealed class FakeCronJob : CronJobEntity;

    private const string _NodeA = "node-a";
    private static readonly DateTimeOffset _Now = new(2026, 06, 17, 12, 00, 00, TimeSpan.Zero);

    [Fact]
    public async Task stalled_retry_reclaim_increments_the_persisted_retry_count_for_time_jobs()
    {
        var (provider, _) = _Create();
        var job = _TimeJob(JobStatus.InProgress, _NodeA, lockedUntil: _Now.UtcDateTime.AddMinutes(-1));
        job.RetryCount = 1;
        await provider.AddTimeJobsAsync([job], AbortToken);

        var reclaimed = await provider.ReclaimStalledTimeJobsAsync(AbortToken);

        reclaimed.Should().Be(1);
        var stored = await provider.GetTimeJobByIdAsync(job.Id, AbortToken);
        stored!.Status.Should().Be(JobStatus.Idle);
        stored.RetryCount.Should().Be(2, "the interrupted attempt consumes one retry-budget unit");
    }

    [Fact]
    public async Task stalled_retry_reclaim_increments_the_persisted_retry_count_for_cron_occurrences()
    {
        var (provider, _) = _Create();
        var occurrence = _Occurrence(JobStatus.InProgress, _NodeA, lockedUntil: _Now.UtcDateTime.AddMinutes(-1));
        occurrence.RetryCount = 1;
        await provider.InsertCronJobOccurrencesAsync([occurrence], AbortToken);

        var reclaimed = await provider.ReclaimStalledCronJobOccurrencesAsync(AbortToken);

        reclaimed.Should().Be(1);
        var all = await provider.GetAllCronJobOccurrencesAsync(predicate: null, AbortToken);
        var stored = all.Single(x => x.Id == occurrence.Id);
        stored.Status.Should().Be(JobStatus.Idle);
        stored.RetryCount.Should().Be(2);
    }

    [Fact]
    public async Task dead_node_release_consumes_budget_only_for_started_rows()
    {
        var (provider, _) = _Create();
        var queuedNeverStarted = _TimeJob(JobStatus.Queued, _NodeA, lockedUntil: _Now.UtcDateTime.AddMinutes(5));
        var interruptedInProgress = _TimeJob(
            JobStatus.InProgress,
            _NodeA,
            lockedUntil: _Now.UtcDateTime.AddMinutes(-1)
        );
        interruptedInProgress.RetryCount = 1;
        await provider.AddTimeJobsAsync([queuedNeverStarted, interruptedInProgress], AbortToken);

        var released = await provider.ReleaseDeadNodeTimeJobResourcesAsync(_NodeA, AbortToken);

        released.Should().Be(2);
        var neverStarted = await provider.GetTimeJobByIdAsync(queuedNeverStarted.Id, AbortToken);
        neverStarted!.Status.Should().Be(JobStatus.Idle);
        neverStarted.RetryCount.Should().Be(0, "a claimed-but-unstarted row never invoked user code");
        var interrupted = await provider.GetTimeJobByIdAsync(interruptedInProgress.Id, AbortToken);
        interrupted!.Status.Should().Be(JobStatus.Idle);
        interrupted.RetryCount.Should().Be(2, "the started attempt lost to node death consumes a budget unit");
    }

    private static (JobsInMemoryPersistenceProvider<FakeTimeJob, FakeCronJob> Provider, FakeTimeProvider Time) _Create()
    {
        var time = new FakeTimeProvider(_Now);
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(time);
        services.AddHeadlessGuidGenerator();
        services.AddSingleton(new SchedulerOptionsBuilder { NodeId = _NodeA, LeaseDuration = TimeSpan.FromMinutes(5) });
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

    private static CronJobOccurrenceEntity<FakeCronJob> _Occurrence(
        JobStatus status,
        string? owner,
        DateTime lockedUntil
    )
    {
        return new CronJobOccurrenceEntity<FakeCronJob>
        {
            Id = Guid.NewGuid(),
            Status = status,
            OwnerId = owner,
            LockedUntil = lockedUntil,
            OnNodeDeath = NodeDeathPolicy.Retry,
            ExecutionTime = _Now.UtcDateTime.AddMinutes(-1),
            CronJobId = Guid.NewGuid(),
            CronJob = new FakeCronJob { Function = "fn", Expression = "* * * * *" },
        };
    }
}
