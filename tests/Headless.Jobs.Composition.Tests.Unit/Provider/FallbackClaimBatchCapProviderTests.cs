// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
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
/// AE5: the in-memory fallback-claim sweep caps each pass at 100 rows (mirror of the durable providers'
/// <c>Take(100)</c>), claiming the oldest by (ExecutionTime, Id) so a large idle backlog cannot be swept in one
/// unbounded batch. Covers both the time-job path (<see
/// cref="JobsInMemoryPersistenceProvider{TTimeJob,TCronJob}.QueueTimedOutTimeJobsAsync"/>) and the
/// cron-occurrence path (<see
/// cref="JobsInMemoryPersistenceProvider{TTimeJob,TCronJob}.QueueTimedOutCronJobOccurrencesAsync"/>).
/// </summary>
public sealed class FallbackClaimBatchCapProviderTests : TestBase
{
    private sealed class FakeTimeJob : TimeJobEntity<FakeTimeJob>;

    private sealed class FakeCronJob : CronJobEntity;

    private const string _NodeA = "node-a";

    // Mirrors JobsInMemoryPersistenceProvider._MaxFallbackClaimBatchSize (private in the provider).
    private const int _MaxFallbackClaimBatchSize = 100;

    private static readonly DateTime _Now = new(2026, 06, 17, 12, 00, 00, DateTimeKind.Utc);
    private static readonly TimeSpan _Lease = TimeSpan.FromMinutes(5);

    private static (JobsInMemoryPersistenceProvider<FakeTimeJob, FakeCronJob> Provider, FakeTimeProvider Time) _Create(
        string nodeId = _NodeA
    )
    {
        var time = new FakeTimeProvider(new DateTimeOffset(_Now, TimeSpan.Zero));
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(time);
        services.AddSingleton<IGuidGenerator>(new SequentialGuidGenerator(SequentialGuidType.Version7));
        services.AddSingleton(new SchedulerOptionsBuilder { NodeId = nodeId, LeaseDuration = _Lease });
        var sp = services.BuildServiceProvider();
        return (new JobsInMemoryPersistenceProvider<FakeTimeJob, FakeCronJob>(sp), time);
    }

    // A fallback-claimable time job: Idle + unowned, with an ExecutionTime well past the 1s fallback window.
    private static FakeTimeJob _FallbackTimeJob(DateTime executionTime)
    {
        return new FakeTimeJob
        {
            Id = Guid.NewGuid(),
            Function = "fn",
            Status = JobStatus.Idle,
            OwnerId = null,
            LockedUntil = null,
            OnNodeDeath = NodeDeathPolicy.Retry,
            ExecutionTime = executionTime,
        };
    }

    // A fallback-claimable cron occurrence: Idle + unowned, with an ExecutionTime well past the 1s fallback window.
    private static CronJobOccurrenceEntity<FakeCronJob> _FallbackCronOccurrence(DateTime executionTime)
    {
        return new CronJobOccurrenceEntity<FakeCronJob>
        {
            Id = Guid.NewGuid(),
            Status = JobStatus.Idle,
            OwnerId = null,
            LockedUntil = null,
            OnNodeDeath = NodeDeathPolicy.Retry,
            ExecutionTime = executionTime,
            CronJobId = Guid.NewGuid(),
            CronJob = new FakeCronJob { Function = "fn", Expression = "* * * * *" },
        };
    }

    [Fact]
    public async Task queue_timed_out_time_jobs_claims_at_most_100_oldest_then_sweeps_the_remainder()
    {
        var (provider, _) = _Create();

        // 101 eligible fallback-claimable jobs, each with a distinct ExecutionTime so the oldest-first ordering is
        // unambiguous (index 0 is oldest, index 100 is newest). All sit well before the 1s fallback threshold.
        var baseTime = _Now.AddMinutes(-10);
        var jobs = Enumerable.Range(0, 101).Select(i => _FallbackTimeJob(baseTime.AddSeconds(i))).ToArray();
        await provider.AddTimeJobsAsync(jobs, AbortToken);

        var oldestFirst = jobs.OrderBy(x => x.ExecutionTime).ThenBy(x => x.Id).ToArray();
        var expectedFirstBatch = oldestFirst.Take(_MaxFallbackClaimBatchSize).Select(x => x.Id).ToArray();
        var newestId = oldestFirst[^1].Id;

        var firstSweep = await provider.QueueTimedOutTimeJobsAsync(AbortToken).ToListAsync(AbortToken);

        // Exactly the oldest 100, in (ExecutionTime, Id) order; the single newest row is left for the next sweep.
        firstSweep.Should().HaveCount(_MaxFallbackClaimBatchSize);
        firstSweep.Select(x => x.Id).Should().Equal(expectedFirstBatch);
        firstSweep.Select(x => x.Id).Should().NotContain(newestId);

        // The first batch is now Queued with a live lease, so the follow-up sweep claims only the remaining row.
        var secondSweep = await provider.QueueTimedOutTimeJobsAsync(AbortToken).ToListAsync(AbortToken);

        secondSweep.Should().ContainSingle().Which.Id.Should().Be(newestId);
    }

    [Fact]
    public async Task queue_timed_out_cron_occurrences_claims_at_most_100_oldest_then_sweeps_the_remainder()
    {
        var (provider, _) = _Create();

        // 101 eligible fallback-claimable occurrences with distinct ExecutionTimes (index 0 oldest, 100 newest).
        var baseTime = _Now.AddMinutes(-10);
        var occurrences = Enumerable
            .Range(0, 101)
            .Select(i => _FallbackCronOccurrence(baseTime.AddSeconds(i)))
            .ToArray();
        await provider.InsertCronJobOccurrencesAsync(occurrences, AbortToken);

        var oldestFirst = occurrences.OrderBy(x => x.ExecutionTime).ThenBy(x => x.Id).ToArray();
        var expectedFirstBatch = oldestFirst.Take(_MaxFallbackClaimBatchSize).Select(x => x.Id).ToArray();
        var newestId = oldestFirst[^1].Id;

        var firstSweep = await provider.QueueTimedOutCronJobOccurrencesAsync(AbortToken).ToListAsync(AbortToken);

        firstSweep.Should().HaveCount(_MaxFallbackClaimBatchSize);
        firstSweep.Select(x => x.Id).Should().Equal(expectedFirstBatch);
        firstSweep.Select(x => x.Id).Should().NotContain(newestId);

        var secondSweep = await provider.QueueTimedOutCronJobOccurrencesAsync(AbortToken).ToListAsync(AbortToken);

        secondSweep.Should().ContainSingle().Which.Id.Should().Be(newestId);
    }
}
