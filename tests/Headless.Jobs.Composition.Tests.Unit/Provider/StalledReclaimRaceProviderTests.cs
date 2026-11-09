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
/// The in-memory stalled-lease reclaim decides from a scan of the store, and a terminal write can arrive between the
/// scan and the reclaim's swap. A settlement needs a live lease, so a run whose lease lapsed while it was finishing
/// has already lost the row: its late terminal write is refused rather than racing the reclaim, and the reclaim
/// hands the job back for another attempt instead of leaving it to whichever write lands first.
/// </summary>
public sealed class StalledReclaimRaceProviderTests : TestBase
{
    private const string _NodeA = "node-a";
    private static readonly DateTimeOffset _Now = new(2026, 06, 17, 12, 00, 00, TimeSpan.Zero);

    [Fact]
    public async Task stalled_reclaim_refuses_a_lapsed_run_terminal_write_that_arrives_after_the_scan()
    {
        var provider = _Create();
        var first = _StalledJob();
        var second = _StalledJob();
        await provider.AddTimeJobsAsync([first, second], AbortToken);
        var completions = new List<Task<int>>();

        // The reclaim's first clone runs after its scan and before any swap. Completing both jobs there lands the
        // terminal writes inside that window whichever job the sweep visits first.
        HookedTimeJob.OnConstructed = () =>
        {
            HookedTimeJob.OnConstructed = null;
            completions.Add(provider.UpdateTimeJobAsync(_Succeeded(first), AbortToken));
            completions.Add(provider.UpdateTimeJobAsync(_Succeeded(second), AbortToken));
        };

        int reclaimed;
        try
        {
            reclaimed = await provider.ReclaimStalledTimeJobsAsync(AbortToken);
        }
        finally
        {
            HookedTimeJob.OnConstructed = null;
        }

        (await Task.WhenAll(completions)).Should().Equal([0, 0], "a lapsed lease can no longer settle the run");
        reclaimed.Should().Be(2);
        foreach (var job in new[] { first, second })
        {
            var stored = await provider.GetTimeJobByIdAsync(job.Id, AbortToken);
            stored!.Status.Should().Be(JobStatus.Idle, "the reclaim hands the lost attempt back for a retry");
            stored.RetryCount.Should().Be(1, "the lost attempt is charged one retry");
            stored.Lease.State.Should().Be(RowLeaseState.Abandoned);
        }
    }

    private static JobsInMemoryPersistenceProvider<HookedTimeJob, HookedCronJob> _Create()
    {
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(new FakeTimeProvider(_Now));
        services.AddHeadlessGuidGenerator();
        services.AddSingleton(new SchedulerOptionsBuilder { NodeId = _NodeA, LeaseDuration = TimeSpan.FromMinutes(5) });
        return new JobsInMemoryPersistenceProvider<HookedTimeJob, HookedCronJob>(services.BuildServiceProvider());
    }

    private static HookedTimeJob _StalledJob()
    {
        return new HookedTimeJob
        {
            Id = Guid.NewGuid(),
            Function = "fn",
            Status = JobStatus.InProgress,
            Lease = TestLeases.Held(_NodeA, _Now.AddMinutes(-1)),
            OnNodeDeath = NodeDeathPolicy.Retry,
            ExecutionTime = _Now.UtcDateTime.AddMinutes(-2),
        };
    }

    private static JobExecutionState _Succeeded(HookedTimeJob job)
    {
        return new JobExecutionState
        {
            JobId = job.Id,
            FunctionName = job.Function,
            Type = JobType.TimeJob,
            ClaimOwner = _NodeA,
            ClaimGeneration = job.Lease.Generation,
        }
            .SetProperty(x => x.Status, JobStatus.Succeeded)
            .SetProperty(x => x.ExecutedAt, _Now)
            .SetProperty(x => x.ReleaseLock, true);
    }

    private sealed class HookedTimeJob : TimeJobEntity<HookedTimeJob>
    {
        // Thread-static: the in-memory reclaim runs synchronously on the test's thread, and no other test sees it.
        [field: ThreadStatic]
        public static Action? OnConstructed { get; set; }

        public HookedTimeJob()
        {
            OnConstructed?.Invoke();
        }
    }

    private sealed class HookedCronJob : CronJobEntity;
}
