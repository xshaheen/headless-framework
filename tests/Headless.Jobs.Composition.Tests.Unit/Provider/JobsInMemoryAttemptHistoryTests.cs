// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;
using Headless.Jobs;
using Headless.Jobs.Provider;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Tests.Provider;

/// <summary>
/// The in-memory store keeps each attempt of a run: the executor opens one per handler invocation, and every write that
/// moves the run out of InProgress closes it with its outcome, or inserts it closed when the open was lost. The
/// relational providers run the same scenarios in the attempt-history conformance suite.
/// </summary>
public sealed class JobsInMemoryAttemptHistoryTests : TestBase
{
    private sealed class FakeTimeJob : TimeJobEntity<FakeTimeJob>;

    private sealed class FakeCronJob : CronJobEntity;

    private const string _Owner = "node-a@1";
    private static readonly DateTimeOffset _Now = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task should_close_the_attempt_as_failed_with_the_node_not_alive_fingerprint_when_the_store_fails_a_run_of_a_dead_node()
    {
        var store = _Create();
        var job = await _AddRunningTimeJobAsync(store, lapsed: true, NodeDeathPolicy.MarkFailed);
        var occurrence = await _AddRunningOccurrenceAsync(store, lapsed: true, NodeDeathPolicy.MarkFailed);
        (await store.OpenTimeJobAttemptAsync(job.Id, _Owner, _Now.UtcDateTime, AbortToken)).Should().BeTrue();
        (await store.OpenCronJobOccurrenceAttemptAsync(occurrence.Id, _Owner, _Now.UtcDateTime, AbortToken))
            .Should()
            .BeTrue();

        (await store.ReleaseDeadNodeTimeJobResourcesAsync(_Owner, AbortToken)).Should().Be(1);
        (await store.ReleaseDeadNodeOccurrenceResourcesAsync(_Owner, AbortToken)).Should().Be(1);

        var attempt = (await store.GetTimeJobAttemptsAsync(job.Id, AbortToken)).Should().ContainSingle().Subject;
        attempt.Number.Should().Be(1);
        attempt.OwnerId.Should().Be(_Owner);
        attempt.StartedAt.Should().Be(_Now.UtcDateTime);
        attempt.EndedAt.Should().NotBeNull();
        attempt.Outcome.Should().Be(JobAttemptOutcome.Failed);
        attempt.FailureFingerprint.Should().Be(JobFailureFingerprints.NodeNotAlive);
        attempt.ExceptionMessage.Should().Be("Node is not alive!");

        var occurrenceAttempt = (await store.GetCronJobOccurrenceAttemptsAsync(occurrence.Id, AbortToken))
            .Should()
            .ContainSingle()
            .Subject;
        occurrenceAttempt.Outcome.Should().Be(JobAttemptOutcome.Failed);
        occurrenceAttempt.FailureFingerprint.Should().Be(JobFailureFingerprints.NodeNotAlive);
    }

    [Fact]
    public async Task should_close_the_attempt_as_lost_when_a_dead_node_reclaim_hands_the_run_back_and_open_a_new_one_on_the_next_claim()
    {
        var store = _Create();
        var job = await _AddRunningTimeJobAsync(store, lapsed: true, NodeDeathPolicy.Retry);
        (await store.OpenTimeJobAttemptAsync(job.Id, _Owner, _Now.UtcDateTime, AbortToken)).Should().BeTrue();

        (await store.ReleaseDeadNodeTimeJobResourcesAsync(_Owner, AbortToken)).Should().Be(1);

        var claimed = (await store.AcquireImmediateTimeJobsAsync([job.Id], AbortToken))
            .Should()
            .ContainSingle()
            .Subject;
        (await store.OpenTimeJobAttemptAsync(job.Id, claimed.OwnerId!, _Now.UtcDateTime, AbortToken)).Should().BeTrue();

        var attempts = await store.GetTimeJobAttemptsAsync(job.Id, AbortToken);
        attempts.Select(x => x.Number).Should().Equal(1, 2);
        attempts[0].Outcome.Should().Be(JobAttemptOutcome.Lost);
        attempts[0].OwnerId.Should().Be(_Owner);
        attempts[0].ExceptionMessage.Should().Be("Node is not alive!");
        attempts[1].Outcome.Should().BeNull("the next claim's attempt is still running");
        attempts[1].EndedAt.Should().BeNull();
        attempts[1].OwnerId.Should().Be(claimed.OwnerId);
    }

    [Fact]
    public async Task should_close_the_attempt_with_each_stores_outcome_for_a_skip_reclaim_a_stalled_lease_and_a_shutdown_release()
    {
        var store = _Create();
        var skipped = await _AddRunningTimeJobAsync(store, lapsed: true, NodeDeathPolicy.Skip);
        var stalled = await _AddRunningTimeJobAsync(store, lapsed: true, NodeDeathPolicy.Retry);
        var released = await _AddRunningTimeJobAsync(store, lapsed: false, NodeDeathPolicy.Retry);
        foreach (var id in new[] { skipped.Id, stalled.Id, released.Id })
        {
            (await store.OpenTimeJobAttemptAsync(id, _Owner, _Now.UtcDateTime, AbortToken)).Should().BeTrue();
        }

        (await store.ReclaimStalledTimeJobsAsync(AbortToken)).Should().Be(2);
        (await store.SettleInterruptedTimeJobAsync(released.Id, _Owner, "host stopped", null, AbortToken))
            .Should()
            .Be(1);

        (await _OnlyAttemptAsync(store, skipped.Id)).Outcome.Should().Be(JobAttemptOutcome.Skipped);
        var lost = await _OnlyAttemptAsync(store, stalled.Id);
        lost.Outcome.Should().Be(JobAttemptOutcome.Lost);
        lost.ExceptionMessage.Should().Be("Lease lapsed while running!");
        var handedBack = await _OnlyAttemptAsync(store, released.Id);
        handedBack.Outcome.Should().Be(JobAttemptOutcome.Released);
        handedBack.ExceptionMessage.Should().Be("host stopped");
    }

    [Fact]
    public async Task should_insert_one_closed_attempt_with_the_node_and_times_it_knows_when_the_open_was_lost()
    {
        var store = _Create();
        var job = await _AddRunningTimeJobAsync(store, lapsed: false, NodeDeathPolicy.Retry);
        var started = _Now.UtcDateTime.AddSeconds(-3);
        var success = _State(job.Id)
            .SetProperty(x => x.Status, JobStatus.Succeeded)
            .SetProperty(
                x => x.AttemptEnd,
                new JobAttemptEnd
                {
                    Outcome = JobAttemptOutcome.Succeeded,
                    StartedAt = started,
                    EndedAt = _Now.UtcDateTime,
                }
            );

        (await store.UpdateTimeJobAsync(success, AbortToken)).Should().Be(1);

        var attempt = await _OnlyAttemptAsync(store, job.Id);
        attempt.Number.Should().Be(1);
        attempt.OwnerId.Should().Be(_Owner);
        attempt.StartedAt.Should().Be(started);
        attempt.EndedAt.Should().Be(_Now.UtcDateTime);
        attempt.Outcome.Should().Be(JobAttemptOutcome.Succeeded);
    }

    [Fact]
    public async Task should_not_insert_an_attempt_when_a_graceful_stop_hands_back_a_run_that_never_started()
    {
        var store = _Create();
        var job = await _AddRunningTimeJobAsync(store, lapsed: false, NodeDeathPolicy.Retry);

        (await store.ReleaseInProgressTimeJobAsync(job.Id, _Owner, AbortToken)).Should().Be(1);

        (await store.GetTimeJobAttemptsAsync(job.Id, AbortToken)).Should().BeEmpty();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task should_close_the_attempt_once_when_the_executor_and_a_sweep_race_to_end_a_run(bool executorFirst)
    {
        var store = _Create();
        var job = await _AddRunningTimeJobAsync(store, lapsed: true, NodeDeathPolicy.MarkFailed);
        (await store.OpenTimeJobAttemptAsync(job.Id, _Owner, _Now.UtcDateTime, AbortToken)).Should().BeTrue();
        var success = _State(job.Id)
            .SetProperty(x => x.Status, JobStatus.Succeeded)
            .SetProperty(
                x => x.AttemptEnd,
                new JobAttemptEnd { Outcome = JobAttemptOutcome.Succeeded, EndedAt = _Now.UtcDateTime }
            );

        if (executorFirst)
        {
            (await store.UpdateTimeJobAsync(success, AbortToken)).Should().Be(1);
            (await store.ReclaimStalledTimeJobsAsync(AbortToken)).Should().Be(0);
        }
        else
        {
            (await store.ReclaimStalledTimeJobsAsync(AbortToken)).Should().Be(1);
            (await store.UpdateTimeJobAsync(success, AbortToken)).Should().Be(0);
        }

        (await _OnlyAttemptAsync(store, job.Id))
            .Outcome.Should()
            .Be(executorFirst ? JobAttemptOutcome.Succeeded : JobAttemptOutcome.Failed);
    }

    [Fact]
    public async Task should_delete_a_runs_attempts_with_the_run()
    {
        var store = _Create();
        var job = await _AddRunningTimeJobAsync(store, lapsed: true, NodeDeathPolicy.MarkFailed);
        var occurrence = await _AddRunningOccurrenceAsync(store, lapsed: true, NodeDeathPolicy.MarkFailed);
        await store.OpenTimeJobAttemptAsync(job.Id, _Owner, _Now.UtcDateTime, AbortToken);
        await store.OpenCronJobOccurrenceAttemptAsync(occurrence.Id, _Owner, _Now.UtcDateTime, AbortToken);
        (await store.ReclaimStalledTimeJobsAsync(AbortToken)).Should().Be(1);
        (await store.ReclaimStalledCronJobOccurrencesAsync(AbortToken)).Should().Be(1);

        (await store.RemoveTimeJobsAsync([job.Id], AbortToken)).Should().Be(1);
        (await store.RemoveCronJobOccurrencesAsync([occurrence.Id], AbortToken)).Should().Be(1);

        (await store.GetTimeJobAttemptsAsync(job.Id, AbortToken)).Should().BeEmpty();
        (await store.GetCronJobOccurrenceAttemptsAsync(occurrence.Id, AbortToken)).Should().BeEmpty();
    }

    [Fact]
    public async Task should_not_open_an_attempt_for_a_run_another_owner_holds_or_that_left_in_progress()
    {
        var store = _Create();
        var job = await _AddRunningTimeJobAsync(store, lapsed: false, NodeDeathPolicy.Retry);

        (await store.OpenTimeJobAttemptAsync(job.Id, "node-b@1", _Now.UtcDateTime, AbortToken)).Should().BeFalse();
        (await store.ReleaseInProgressTimeJobAsync(job.Id, _Owner, AbortToken)).Should().Be(1);
        (await store.OpenTimeJobAttemptAsync(job.Id, _Owner, _Now.UtcDateTime, AbortToken)).Should().BeFalse();

        (await store.GetTimeJobAttemptsAsync(job.Id, AbortToken)).Should().BeEmpty();
    }

    private async Task<TimeJobAttemptEntity> _OnlyAttemptAsync(
        JobsInMemoryPersistenceProvider<FakeTimeJob, FakeCronJob> store,
        Guid id
    )
    {
        var attempts = await store.GetTimeJobAttemptsAsync(id, AbortToken);
        var attempt = attempts.Should().ContainSingle().Subject;
        attempt.EndedAt.Should().NotBeNull("every write that left InProgress closed the attempt");

        return attempt;
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

    private async Task<FakeTimeJob> _AddRunningTimeJobAsync(
        JobsInMemoryPersistenceProvider<FakeTimeJob, FakeCronJob> store,
        bool lapsed,
        NodeDeathPolicy policy
    )
    {
        var job = new FakeTimeJob
        {
            Id = Guid.NewGuid(),
            Function = "fn",
            Status = JobStatus.InProgress,
            OwnerId = _Owner,
            LockedUntil = lapsed ? _Now.UtcDateTime.AddMinutes(-1) : _Now.UtcDateTime.AddMinutes(5),
            OnNodeDeath = policy,
            Retries = 3,
            ExecutionTime = _Now.UtcDateTime.AddMinutes(-2),
        };
        await store.AddTimeJobsAsync([job], AbortToken);

        return job;
    }

    private async Task<CronJobOccurrenceEntity<FakeCronJob>> _AddRunningOccurrenceAsync(
        JobsInMemoryPersistenceProvider<FakeTimeJob, FakeCronJob> store,
        bool lapsed,
        NodeDeathPolicy policy
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
            LockedUntil = lapsed ? _Now.UtcDateTime.AddMinutes(-1) : _Now.UtcDateTime.AddMinutes(5),
            OnNodeDeath = policy,
            ExecutionTime = _Now.UtcDateTime.AddMinutes(-1),
            CronJobId = cronJob.Id,
            CronJob = cronJob,
        };
        await store.InsertCronJobsAsync([cronJob], AbortToken);
        await store.InsertCronJobOccurrencesAsync([occurrence], AbortToken);

        return occurrence;
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
}
