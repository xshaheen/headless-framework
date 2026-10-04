// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Reflection;
using Headless.Hosting;
using Headless.Jobs;
using Headless.Jobs.Provider;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

/// <summary>
/// Requeue of a failed time job or cron occurrence: the scheduler contract, and the transition and refusals as the
/// in-memory store applies them.
/// </summary>
public sealed class JobRequeueTests : TestBase
{
    private const string _Owner = "node-a@incarnation";
    private static readonly DateTimeOffset _Now = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTime _FailedAt = _Now.UtcDateTime.AddHours(-1);

    #region Scheduler

    [Fact]
    public void default_requeue_outcome_is_a_refusal_not_a_success()
    {
        // An uninitialized outcome (a default struct field, an unconfigured test double) must never read as a move.
        default(JobRequeueOutcome).Should().NotBe(JobRequeueOutcome.Requeued);
    }

    [Theory]
    [InlineData(JobRequeueOutcome.Requeued, 1)]
    [InlineData(JobRequeueOutcome.NotFailed, 0)]
    [InlineData(JobRequeueOutcome.ChainMember, 0)]
    public async Task scheduler_requeue_forwards_and_wakes_the_host_only_when_the_row_moved(
        JobRequeueOutcome outcome,
        int expectedRestarts
    )
    {
        var internalManager = Substitute.For<IInternalJobManager>();
        var hostScheduler = Substitute.For<IJobsHostScheduler>();
        var jobId = Guid.NewGuid();
        internalManager.RequeueTimeJobAsync(jobId, AbortToken).Returns(outcome);
        var scheduler = _Scheduler(internalManager, hostScheduler);

        (await scheduler.RequeueAsync(jobId, AbortToken)).Should().Be(outcome);

        await internalManager.Received(1).RequeueTimeJobAsync(jobId, AbortToken);
        hostScheduler.Received(expectedRestarts).Restart();
    }

    [Theory]
    [InlineData(JobRequeueOutcome.Requeued, 1)]
    [InlineData(JobRequeueOutcome.Overlap, 0)]
    public async Task scheduler_occurrence_requeue_forwards_and_wakes_the_host_only_when_the_row_moved(
        JobRequeueOutcome outcome,
        int expectedRestarts
    )
    {
        var internalManager = Substitute.For<IInternalJobManager>();
        var hostScheduler = Substitute.For<IJobsHostScheduler>();
        var occurrenceId = Guid.NewGuid();
        internalManager.RequeueCronJobOccurrenceAsync(occurrenceId, AbortToken).Returns(outcome);
        var scheduler = _Scheduler(internalManager, hostScheduler);

        (await scheduler.RequeueOccurrenceAsync(occurrenceId, AbortToken)).Should().Be(outcome);

        await internalManager.Received(1).RequeueCronJobOccurrenceAsync(occurrenceId, AbortToken);
        await internalManager.DidNotReceive().RequeueTimeJobAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        hostScheduler.Received(expectedRestarts).Restart();
    }

    #endregion

    #region Time jobs

    [Fact]
    public async Task failed_time_job_returns_to_idle_due_now_and_is_claimed_by_the_main_peek()
    {
        var (store, _) = _Create();
        var job = _FailedJob();
        await store.AddTimeJobsAsync([job], AbortToken);
        (await store.GetEarliestTimeJobsAsync(AbortToken)).Jobs.Should().BeEmpty("a failed row is not claimable");

        var outcome = await store.RequeueTimeJobAsync(job.Id, AbortToken);

        outcome.Should().Be(JobRequeueOutcome.Requeued);
        var requeued = await store.GetTimeJobByIdAsync(job.Id, AbortToken);
        requeued!.Status.Should().Be(JobStatus.Idle);
        requeued.RetryCount.Should().Be(0);
        requeued.ExceptionMessage.Should().BeNull();
        requeued.OwnerId.Should().BeNull();
        requeued.LockedUntil.Should().BeNull();
        requeued.ExecutedAt.Should().BeNull();
        requeued.ElapsedTime.Should().Be(0);
        requeued.CancelRequested.Should().BeFalse();
        requeued.ExecutionTime.Should().Be(_Now.UtcDateTime, "the store's current instant, not the original due time");
        requeued.UpdatedAt.Should().Be(_Now);
        requeued.Retries.Should().Be(3, "the stored retry budget is kept");
        requeued.RetryIntervals.Should().Equal(5, 10, 20);

        var peek = await store.GetEarliestTimeJobsAsync(AbortToken);
        peek.Jobs.Select(x => x.Id).Should().Equal(job.Id);
        var claimed = await store.QueueTimeJobsAsync(peek.Jobs, AbortToken).ToArrayAsync(AbortToken);
        claimed.Select(x => x.Id).Should().Equal(job.Id);
    }

    [Theory]
    [InlineData(JobStatus.Succeeded)]
    [InlineData(JobStatus.InProgress)]
    [InlineData(JobStatus.Idle)]
    [InlineData(JobStatus.Cancelled)]
    public async Task time_job_that_is_not_failed_is_refused_and_unchanged(JobStatus status)
    {
        var (store, _) = _Create();
        var job = _FailedJob();
        job.Status = status;
        await store.AddTimeJobsAsync([job], AbortToken);
        var before = await store.GetTimeJobByIdAsync(job.Id, AbortToken);

        var outcome = await store.RequeueTimeJobAsync(job.Id, AbortToken);

        outcome.Should().Be(JobRequeueOutcome.NotFailed);
        var after = await store.GetTimeJobByIdAsync(job.Id, AbortToken);
        after.Should().BeEquivalentTo(before, options => options.Excluding(x => x!.Children));
    }

    [Fact]
    public async Task unknown_time_job_is_not_found()
    {
        var (store, _) = _Create();

        (await store.RequeueTimeJobAsync(Guid.NewGuid(), AbortToken)).Should().Be(JobRequeueOutcome.NotFound);
    }

    [Fact]
    public async Task chain_parent_and_chain_child_are_refused()
    {
        var (store, _) = _Create();
        var child = _FailedJob();
        child.ExecutionTime = null;
        var parent = _FailedJob();
        parent.Children = [child];
        await store.AddTimeJobsAsync([parent], AbortToken);

        (await store.RequeueTimeJobAsync(parent.Id, AbortToken)).Should().Be(JobRequeueOutcome.ChainMember);
        (await store.RequeueTimeJobAsync(child.Id, AbortToken)).Should().Be(JobRequeueOutcome.ChainMember);

        (await store.GetTimeJobByIdAsync(parent.Id, AbortToken))!.Status.Should().Be(JobStatus.Failed);
        (await store.GetTimeJobByIdAsync(child.Id, AbortToken))!.Status.Should().Be(JobStatus.Failed);
    }

    [Fact]
    public async Task current_keyed_generation_is_requeued_and_a_superseded_one_is_refused()
    {
        var (store, _) = _Create();
        var current = await _FailedKeyedAsync(store, "current", isCurrent: true);
        var superseded = await _FailedKeyedAsync(store, "superseded", isCurrent: false);

        (await store.RequeueTimeJobAsync(superseded, AbortToken)).Should().Be(JobRequeueOutcome.SupersededGeneration);
        (await store.GetTimeJobByIdAsync(superseded, AbortToken))!.Status.Should().Be(JobStatus.Failed);

        (await store.RequeueTimeJobAsync(current, AbortToken)).Should().Be(JobRequeueOutcome.Requeued);
        var requeued = await store.GetTimeJobByIdAsync(current, AbortToken);
        requeued!.Status.Should().Be(JobStatus.Idle);
        requeued.IsCurrentGeneration.Should().BeTrue();
    }

    [Fact]
    public async Task concurrent_time_job_requeues_move_the_row_once()
    {
        var (store, _) = _Create();
        var job = _FailedJob();
        await store.AddTimeJobsAsync([job], AbortToken);

        var outcomes = await Task.WhenAll(
            Enumerable
                .Range(0, 16)
                .Select(_ => Task.Run(() => store.RequeueTimeJobAsync(job.Id, AbortToken), AbortToken))
        );

        outcomes.Count(x => x == JobRequeueOutcome.Requeued).Should().Be(1);
        outcomes
            .Where(x => x != JobRequeueOutcome.Requeued)
            .Should()
            .OnlyContain(x => x == JobRequeueOutcome.NotFailed || x == JobRequeueOutcome.Conflict);
        (await store.RequeueTimeJobAsync(job.Id, AbortToken)).Should().Be(JobRequeueOutcome.NotFailed);
    }

    #endregion

    #region Cron occurrences

    [Fact]
    public async Task failed_occurrence_returns_to_idle_keeps_its_instant_and_is_claimed_by_the_fallback()
    {
        var (store, _) = _Create();
        var definition = _Definition(CronOverlapPolicy.Allow);
        await store.InsertCronJobsAsync([definition], AbortToken);
        var occurrence = _Occurrence(definition, _FailedAt, JobStatus.Failed);
        await store.InsertCronJobOccurrencesAsync([occurrence], AbortToken);
        (await store.QueueTimedOutCronJobOccurrencesAsync(AbortToken).ToArrayAsync(AbortToken))
            .Should()
            .BeEmpty("a failed occurrence is not claimable");

        var outcome = await store.RequeueCronJobOccurrenceAsync(occurrence.Id, AbortToken);

        outcome.Should().Be(JobRequeueOutcome.Requeued);
        var requeued = (await store.GetAllCronJobOccurrencesAsync(x => x.Id == occurrence.Id, AbortToken)).Single();
        requeued.Status.Should().Be(JobStatus.Idle);
        requeued.RetryCount.Should().Be(0);
        requeued.ExceptionMessage.Should().BeNull();
        requeued.OwnerId.Should().BeNull();
        requeued.LockedUntil.Should().BeNull();
        requeued.ExecutedAt.Should().BeNull();
        requeued.ElapsedTime.Should().Be(0);
        requeued.ExecutionTime.Should().Be(_FailedAt, "the instant identifies the occurrence");
        requeued.UpdatedAt.Should().Be(_Now);

        var claimed = await store.QueueTimedOutCronJobOccurrencesAsync(AbortToken).ToArrayAsync(AbortToken);
        claimed.Select(x => x.Id).Should().Equal(occurrence.Id);
    }

    [Theory]
    [InlineData(JobStatus.Succeeded)]
    [InlineData(JobStatus.InProgress)]
    [InlineData(JobStatus.Idle)]
    public async Task occurrence_that_is_not_failed_is_refused_and_unchanged(JobStatus status)
    {
        var (store, _) = _Create();
        var definition = _Definition(CronOverlapPolicy.Allow);
        await store.InsertCronJobsAsync([definition], AbortToken);
        var occurrence = _Occurrence(definition, _FailedAt, status);
        await store.InsertCronJobOccurrencesAsync([occurrence], AbortToken);

        (await store.RequeueCronJobOccurrenceAsync(occurrence.Id, AbortToken)).Should().Be(JobRequeueOutcome.NotFailed);

        var after = (await store.GetAllCronJobOccurrencesAsync(x => x.Id == occurrence.Id, AbortToken)).Single();
        after.Status.Should().Be(status);
        after.UpdatedAt.Should().Be(occurrence.UpdatedAt);
    }

    [Fact]
    public async Task unknown_occurrence_is_not_found()
    {
        var (store, _) = _Create();

        (await store.RequeueCronJobOccurrenceAsync(Guid.NewGuid(), AbortToken)).Should().Be(JobRequeueOutcome.NotFound);
    }

    [Fact]
    public async Task occurrence_whose_definition_is_missing_is_not_found_and_unchanged()
    {
        var (store, _) = _Create();
        var definition = _Definition(CronOverlapPolicy.Skip);
        await store.InsertCronJobsAsync([definition], AbortToken);
        var occurrence = _Occurrence(definition, _FailedAt, JobStatus.Failed);
        await store.InsertCronJobOccurrencesAsync([occurrence], AbortToken);
        await store.RemoveCronJobsAsync([definition.Id], AbortToken);

        (await store.RequeueCronJobOccurrenceAsync(occurrence.Id, AbortToken)).Should().Be(JobRequeueOutcome.NotFound);

        (await store.GetAllCronJobOccurrencesAsync(x => x.Id == occurrence.Id, AbortToken))
            .Single()
            .Status.Should()
            .Be(JobStatus.Failed);
    }

    [Fact]
    public async Task skip_overlap_refuses_while_another_occurrence_runs_and_accepts_once_it_finished()
    {
        var (store, _) = _Create();
        var definition = _Definition(CronOverlapPolicy.Skip);
        await store.InsertCronJobsAsync([definition], AbortToken);
        var failed = _Occurrence(definition, _FailedAt, JobStatus.Failed);
        var running = _Occurrence(definition, _FailedAt.AddMinutes(30), JobStatus.InProgress);
        await store.InsertCronJobOccurrencesAsync([failed, running], AbortToken);

        (await store.RequeueCronJobOccurrenceAsync(failed.Id, AbortToken)).Should().Be(JobRequeueOutcome.Overlap);
        (await store.GetAllCronJobOccurrencesAsync(x => x.Id == failed.Id, AbortToken))
            .Single()
            .Status.Should()
            .Be(JobStatus.Failed);

        await store.RemoveCronJobOccurrencesAsync([running.Id], AbortToken);
        var finished = _Occurrence(definition, _FailedAt.AddMinutes(30), JobStatus.Succeeded);
        await store.InsertCronJobOccurrencesAsync([finished], AbortToken);

        (await store.RequeueCronJobOccurrenceAsync(failed.Id, AbortToken)).Should().Be(JobRequeueOutcome.Requeued);
    }

    [Fact]
    public async Task allow_overlap_requeues_next_to_a_running_occurrence()
    {
        var (store, _) = _Create();
        var definition = _Definition(CronOverlapPolicy.Allow);
        await store.InsertCronJobsAsync([definition], AbortToken);
        var failed = _Occurrence(definition, _FailedAt, JobStatus.Failed);
        var running = _Occurrence(definition, _FailedAt.AddMinutes(30), JobStatus.InProgress);
        await store.InsertCronJobOccurrencesAsync([failed, running], AbortToken);

        (await store.RequeueCronJobOccurrenceAsync(failed.Id, AbortToken)).Should().Be(JobRequeueOutcome.Requeued);
    }

    [Fact]
    public async Task occurrence_whose_instant_is_held_by_a_live_row_is_refused()
    {
        var (store, _) = _Create();
        var definition = _Definition(CronOverlapPolicy.Allow);
        await store.InsertCronJobsAsync([definition], AbortToken);
        var failed = _Occurrence(definition, _FailedAt, JobStatus.Failed);
        var live = _Occurrence(definition, _FailedAt, JobStatus.Idle);
        await store.InsertCronJobOccurrencesAsync([failed, live], AbortToken);

        (await store.RequeueCronJobOccurrenceAsync(failed.Id, AbortToken)).Should().Be(JobRequeueOutcome.Conflict);
        (await store.GetAllCronJobOccurrencesAsync(x => x.Id == failed.Id, AbortToken))
            .Single()
            .Status.Should()
            .Be(JobStatus.Failed);
    }

    [Fact]
    public async Task concurrent_occurrence_requeues_move_the_row_once()
    {
        var (store, _) = _Create();
        var definition = _Definition(CronOverlapPolicy.Skip);
        await store.InsertCronJobsAsync([definition], AbortToken);
        var failed = _Occurrence(definition, _FailedAt, JobStatus.Failed);
        await store.InsertCronJobOccurrencesAsync([failed], AbortToken);

        var outcomes = await Task.WhenAll(
            Enumerable
                .Range(0, 16)
                .Select(_ => Task.Run(() => store.RequeueCronJobOccurrenceAsync(failed.Id, AbortToken), AbortToken))
        );

        outcomes.Count(x => x == JobRequeueOutcome.Requeued).Should().Be(1);
        outcomes
            .Where(x => x != JobRequeueOutcome.Requeued)
            .Should()
            .OnlyContain(x => x == JobRequeueOutcome.NotFailed || x == JobRequeueOutcome.Conflict);
    }

    #endregion

    private static JobScheduler<TimeJobEntity, CronJobEntity> _Scheduler(
        IInternalJobManager internalManager,
        IJobsHostScheduler hostScheduler
    ) =>
        new(
            Substitute.For<ITimeJobManager<TimeJobEntity>>(),
            Substitute.For<ICronJobManager<CronJobEntity>>(),
            JobFunctionRegistryBuilder.Build([], [], []),
            internalManager,
            hostScheduler,
            JobsRequestSerializationOptions.Default,
            new FakeTimeProvider(),
            JobSchedulingPolicies.Empty
        );

    private static (
        JobsInMemoryPersistenceProvider<TimeJobEntity, CronJobEntity> Store,
        FakeTimeProvider Time
    ) _Create()
    {
        var time = new FakeTimeProvider(_Now);
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(time);
        services.AddHeadlessGuidGenerator();
        services.AddSingleton(new SchedulerOptionsBuilder { NodeId = _Owner });
        return (
            new JobsInMemoryPersistenceProvider<TimeJobEntity, CronJobEntity>(services.BuildServiceProvider()),
            time
        );
    }

    private static TimeJobEntity _FailedJob() =>
        new()
        {
            Id = Guid.NewGuid(),
            Function = "requeue",
            Status = JobStatus.Failed,
            OwnerId = "node-b@1",
            LockedUntil = _FailedAt.AddMinutes(5),
            ExecutionTime = _FailedAt,
            ExecutedAt = _Now.AddHours(-1).AddSeconds(3),
            ElapsedTime = 3000,
            ExceptionMessage = "boom",
            Retries = 3,
            RetryCount = 3,
            RetryIntervals = [5, 10, 20],
            CreatedAt = _Now.AddHours(-2),
            UpdatedAt = _Now.AddHours(-1),
        };

    // A keyed row reaches Failed only by executing, which this store test does not drive; the stored row is edited in
    // place instead, the same way the keyed storage tests reach the store's private state.
    private async Task<Guid> _FailedKeyedAsync(
        JobsInMemoryPersistenceProvider<TimeJobEntity, CronJobEntity> store,
        string key,
        bool isCurrent
    )
    {
        var scheduled = await store.ScheduleKeyedTimeJobAsync(
            new JobKey(key),
            JobsKeyedSchedulingScenarios.Candidate(),
            cancellationToken: AbortToken
        );
        var id = scheduled.RunId!.Value;
        var rows =
            (ConcurrentDictionary<Guid, TimeJobEntity>)
                typeof(JobsInMemoryPersistenceProvider<TimeJobEntity, CronJobEntity>)
                    .GetField("_timeJobs", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)!
                    .GetValue(store)!;
        var failed = rows[id].Clone();
        failed.Status = JobStatus.Failed;
        failed.ExceptionMessage = "boom";
        failed.IsCurrentGeneration = isCurrent;
        rows[id] = failed;
        return id;
    }

    private static CronJobEntity _Definition(CronOverlapPolicy policy) =>
        new()
        {
            Id = Guid.NewGuid(),
            Function = "requeue-cron",
            Expression = "0 0 * * * *",
            ReconciledThroughUtc = _FailedAt,
            NextDueUtc = _FailedAt.AddHours(2),
            OnOverlap = policy,
            CreatedAt = _Now.AddDays(-1),
            UpdatedAt = _Now.AddDays(-1),
        };

    private static CronJobOccurrenceEntity<CronJobEntity> _Occurrence(
        CronJobEntity definition,
        DateTime executionTime,
        JobStatus status
    )
    {
        var claimed = status is JobStatus.Queued or JobStatus.InProgress or JobStatus.Failed;
        return new CronJobOccurrenceEntity<CronJobEntity>
        {
            Id = Guid.NewGuid(),
            CronJobId = definition.Id,
            Function = definition.Function,
            ExecutionTime = executionTime,
            Status = status,
            OwnerId = claimed ? "node-b@1" : null,
            LockedUntil = claimed ? _Now.UtcDateTime.AddMinutes(5) : null,
            ExecutedAt = status is JobStatus.Failed or JobStatus.Succeeded
                ? new DateTimeOffset(executionTime.AddSeconds(3), TimeSpan.Zero)
                : null,
            ElapsedTime = status is JobStatus.Failed ? 3000 : 0,
            ExceptionMessage = status is JobStatus.Failed ? "boom" : null,
            RetryCount = status is JobStatus.Failed ? 2 : 0,
            CreatedAt = _Now.AddHours(-2),
            UpdatedAt = _Now.AddHours(-2),
        };
    }
}
