// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;
using Headless.Jobs;
using Headless.Jobs.Provider;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Tests.Provider;

/// <summary>
/// The in-memory store keeps a failed run's failure fingerprint: the executor's carried fingerprint on a failure or
/// retry write, a fixed fingerprint per cause on a failure the store writes itself, only the winning write's value
/// when two writers race to end one run, and none after a requeue. The relational providers run the same scenarios
/// in the failure-fingerprint conformance suite.
/// </summary>
public sealed class JobsInMemoryFailureFingerprintTests : TestBase
{
    private sealed class FakeTimeJob : TimeJobEntity<FakeTimeJob>;

    private sealed class FakeCronJob : CronJobEntity;

    private const string _Owner = "node-a@1";
    private const string _Carried = "v1:0123456789abcdef";
    private static readonly DateTimeOffset _Now = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task should_store_the_carried_fingerprint_on_a_failure_write_and_clear_it_on_requeue()
    {
        var store = _Create();
        var job = _TimeJob(lockedUntil: _Now.UtcDateTime.AddMinutes(5), NodeDeathPolicy.Retry);
        await store.AddTimeJobsAsync([job], AbortToken);

        var failure = _State(job.Id)
            .SetProperty(x => x.Status, JobStatus.Failed)
            .SetProperty(x => x.ExceptionDetails, "boom")
            .SetProperty(x => x.FailureFingerprint, _Carried);
        (await store.UpdateTimeJobAsync(failure, AbortToken)).Should().Be(1);
        (await store.GetTimeJobByIdAsync(job.Id, AbortToken))!.FailureFingerprint.Should().Be(_Carried);

        (await store.RequeueTimeJobAsync(job.Id, traceParent: null, AbortToken))
            .Should()
            .Be(JobRequeueOutcome.Requeued);
        (await store.GetTimeJobByIdAsync(job.Id, AbortToken))!.FailureFingerprint.Should().BeNull();
    }

    [Fact]
    public async Task should_store_the_lease_lapsed_fingerprint_when_the_stalled_sweep_fails_a_run()
    {
        var store = _Create();
        var job = _TimeJob(lockedUntil: _Now.UtcDateTime.AddMinutes(-1), NodeDeathPolicy.MarkFailed);
        await store.AddTimeJobsAsync([job], AbortToken);
        var occurrence = await _AddOccurrenceAsync(store, lockedUntil: _Now.UtcDateTime.AddMinutes(-1));

        (await store.ReclaimStalledTimeJobsAsync(AbortToken)).Should().Be(1);
        (await store.ReclaimStalledCronJobOccurrencesAsync(AbortToken)).Should().Be(1);

        var stored = (await store.GetTimeJobByIdAsync(job.Id, AbortToken))!;
        stored.Status.Should().Be(JobStatus.Failed);
        stored.FailureFingerprint.Should().Be("v1:lease-lapsed");
        var storedOccurrence = await _OccurrenceAsync(store, occurrence.Id);
        storedOccurrence.Status.Should().Be(JobStatus.Failed);
        storedOccurrence.FailureFingerprint.Should().Be("v1:lease-lapsed");
    }

    [Fact]
    public async Task should_store_the_node_not_alive_fingerprint_when_the_dead_node_sweep_fails_a_run()
    {
        var store = _Create();
        var job = _TimeJob(lockedUntil: _Now.UtcDateTime.AddMinutes(-1), NodeDeathPolicy.MarkFailed);
        await store.AddTimeJobsAsync([job], AbortToken);
        var occurrence = await _AddOccurrenceAsync(store, lockedUntil: _Now.UtcDateTime.AddMinutes(-1));

        (await store.ReleaseDeadNodeTimeJobResourcesAsync(_Owner, AbortToken)).Should().Be(1);
        (await store.ReleaseDeadNodeOccurrenceResourcesAsync(_Owner, AbortToken)).Should().Be(1);

        (await store.GetTimeJobByIdAsync(job.Id, AbortToken))!.FailureFingerprint.Should().Be("v1:node-not-alive");
        (await _OccurrenceAsync(store, occurrence.Id)).FailureFingerprint.Should().Be("v1:node-not-alive");
    }

    [Fact]
    public async Task should_store_the_shutdown_fingerprint_when_a_stopping_node_settles_a_run_as_failed()
    {
        var store = _Create();
        var job = _TimeJob(lockedUntil: _Now.UtcDateTime.AddMinutes(5), NodeDeathPolicy.MarkFailed);
        await store.AddTimeJobsAsync([job], AbortToken);
        var occurrence = await _AddOccurrenceAsync(store, lockedUntil: _Now.UtcDateTime.AddMinutes(5));

        (await store.SettleInterruptedTimeJobAsync(job.Id, _Owner, "host stopped", null, AbortToken)).Should().Be(1);
        (await store.SettleInterruptedCronJobOccurrenceAsync(occurrence.Id, _Owner, "host stopped", AbortToken))
            .Should()
            .Be(1);

        (await store.GetTimeJobByIdAsync(job.Id, AbortToken))!
            .FailureFingerprint.Should()
            .Be("v1:interrupted-by-shutdown");
        (await _OccurrenceAsync(store, occurrence.Id)).FailureFingerprint.Should().Be("v1:interrupted-by-shutdown");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task should_keep_only_the_winning_writers_fingerprint_when_two_writers_race_to_fail_a_run(
        bool executorFirst
    )
    {
        var store = _Create();
        var job = _TimeJob(lockedUntil: _Now.UtcDateTime.AddMinutes(-1), NodeDeathPolicy.MarkFailed);
        await store.AddTimeJobsAsync([job], AbortToken);
        var failure = _State(job.Id)
            .SetProperty(x => x.Status, JobStatus.Failed)
            .SetProperty(x => x.FailureFingerprint, _Carried);

        int executorWrote;
        int sweepWrote;
        if (executorFirst)
        {
            executorWrote = await store.UpdateTimeJobAsync(failure, AbortToken);
            sweepWrote = await store.ReclaimStalledTimeJobsAsync(AbortToken);
        }
        else
        {
            sweepWrote = await store.ReclaimStalledTimeJobsAsync(AbortToken);
            executorWrote = await store.UpdateTimeJobAsync(failure, AbortToken);
        }

        (executorWrote + sweepWrote).Should().Be(1, "exactly one writer ends the run");
        (await store.GetTimeJobByIdAsync(job.Id, AbortToken))!
            .FailureFingerprint.Should()
            .Be(executorFirst ? _Carried : "v1:lease-lapsed");
    }

    private static JobsInMemoryPersistenceProvider<FakeTimeJob, FakeCronJob> _Create()
    {
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(new FakeTimeProvider(_Now));
        services.AddHeadlessGuidGenerator();
        services.AddSingleton(
            new SchedulerOptionsBuilder { NodeId = "node-a", LeaseDuration = TimeSpan.FromMinutes(5) }
        );

        return new JobsInMemoryPersistenceProvider<FakeTimeJob, FakeCronJob>(services.BuildServiceProvider());
    }

    private static FakeTimeJob _TimeJob(DateTime lockedUntil, NodeDeathPolicy policy)
    {
        return new FakeTimeJob
        {
            Id = Guid.NewGuid(),
            Function = "fn",
            Status = JobStatus.InProgress,
            OwnerId = _Owner,
            LockedUntil = lockedUntil,
            OnNodeDeath = policy,
            ExecutionTime = _Now.UtcDateTime.AddMinutes(-2),
        };
    }

    private static JobExecutionState _State(Guid id)
    {
        return new JobExecutionState
        {
            FunctionName = "fn",
            JobId = id,
            ClaimOwner = _Owner,
        };
    }

    private async Task<CronJobOccurrenceEntity<FakeCronJob>> _AddOccurrenceAsync(
        JobsInMemoryPersistenceProvider<FakeTimeJob, FakeCronJob> store,
        DateTime lockedUntil
    )
    {
        var cronJob = new FakeCronJob
        {
            Id = Guid.NewGuid(),
            Function = "fn",
            Expression = "* * * * *",
        };
        var occurrence = new CronJobOccurrenceEntity<FakeCronJob>
        {
            Id = Guid.NewGuid(),
            Status = JobStatus.InProgress,
            OwnerId = _Owner,
            LockedUntil = lockedUntil,
            OnNodeDeath = NodeDeathPolicy.MarkFailed,
            ExecutionTime = _Now.UtcDateTime.AddMinutes(-1),
            CronJobId = cronJob.Id,
            CronJob = cronJob,
        };
        await store.InsertCronJobsAsync([cronJob], AbortToken);
        await store.InsertCronJobOccurrencesAsync([occurrence], AbortToken);

        return occurrence;
    }

    private static async Task<CronJobOccurrenceEntity<FakeCronJob>> _OccurrenceAsync(
        JobsInMemoryPersistenceProvider<FakeTimeJob, FakeCronJob> store,
        Guid id
    )
    {
        var all = await store.GetAllCronJobOccurrencesAsync(predicate: null, AbortToken);

        return all.Single(x => x.Id == id);
    }
}
