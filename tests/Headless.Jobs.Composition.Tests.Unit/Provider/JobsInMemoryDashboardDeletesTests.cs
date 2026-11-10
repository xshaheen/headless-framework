// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;
using Headless.Jobs;
using Headless.Jobs.Provider;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Tests.Provider;

/// <summary>
/// The in-memory store's two conditional deletes behind the dashboard's Force delete and cron Delete: each checks and
/// removes in one step, so a run that moved after the caller read it is kept. The relational providers run the same
/// scenarios in the dashboard-deletes conformance suite.
/// </summary>
public sealed class JobsInMemoryDashboardDeletesTests : TestBase
{
    private const string _Owner = "node-a@1";
    private const string _Function = "billing.charge";
    private static readonly DateTimeOffset _Now = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task should_force_delete_a_running_job_with_cancel_requested()
    {
        // given
        IJobPersistenceProvider<TimeJobEntity, CronJobEntity> store = _Create();
        var (job, _) = await _StartAsync(store, cancel: true);

        // when
        var deleted = await store.RemoveCancelRequestedTimeJobAsync(job, AbortToken);

        // then
        deleted.Should().BeTrue();
        (await store.GetTimeJobByIdAsync(job, AbortToken)).Should().BeNull();
    }

    [Fact]
    public async Task should_keep_a_running_job_whose_cancel_was_not_requested()
    {
        // given
        IJobPersistenceProvider<TimeJobEntity, CronJobEntity> store = _Create();
        var (job, _) = await _StartAsync(store, cancel: false);

        // when
        var deleted = await store.RemoveCancelRequestedTimeJobAsync(job, AbortToken);

        // then
        deleted.Should().BeFalse();
        (await store.GetJobRunAsync(job, AbortToken))!.Status.Should().Be(JobStatus.InProgress);
    }

    [Fact]
    public async Task should_keep_a_job_that_finished_after_it_was_read_as_running_with_cancel_requested()
    {
        // given
        IJobPersistenceProvider<TimeJobEntity, CronJobEntity> store = _Create();
        var (job, owner) = await _StartAsync(store, cancel: true);
        var read = await store.GetJobRunAsync(job, AbortToken);
        read!.CancelRequested.Should().BeTrue();
        await _CompleteAsync(store, job, owner);

        // when
        var deleted = await store.RemoveCancelRequestedTimeJobAsync(job, AbortToken);

        // then
        deleted.Should().BeFalse();
        (await store.GetJobRunAsync(job, AbortToken))!.Status.Should().Be(JobStatus.Succeeded);
    }

    [Fact]
    public async Task should_delete_a_cron_job_with_its_stored_occurrences_and_report_how_many()
    {
        // given
        IJobPersistenceProvider<TimeJobEntity, CronJobEntity> store = _Create();
        var cron = await _AddCronJobAsync(store);
        await _AddOccurrencesAsync(store, cron, JobStatus.Succeeded, 9);
        await _AddOccurrencesAsync(store, cron, JobStatus.Failed, 2);
        await _AddOccurrencesAsync(store, cron, JobStatus.Idle, 1);

        // when
        var removed = await store.RemoveIdleCronJobAsync(cron, AbortToken);

        // then
        removed.Should().Be(12);
        (await store.GetCronJobByIdAsync(cron, AbortToken)).Should().BeNull();
        (await store.GetAllCronJobOccurrencesAsync(x => x.CronJobId == cron, AbortToken)).Should().BeEmpty();
    }

    [Fact]
    public async Task should_keep_a_cron_job_and_its_occurrences_when_an_occurrence_started_after_the_read()
    {
        // given
        IJobPersistenceProvider<TimeJobEntity, CronJobEntity> store = _Create();
        var cron = await _AddCronJobAsync(store);
        await _AddOccurrencesAsync(store, cron, JobStatus.Succeeded, 3);
        var running = await store.CountJobRunsAsync(
            new JobRunQuery { CronJobId = cron, Statuses = [JobStatus.InProgress] },
            cancellationToken: AbortToken
        );
        running.Count.Should().Be(0);
        await _AddOccurrencesAsync(store, cron, JobStatus.InProgress, 1);

        // when
        var removed = await store.RemoveIdleCronJobAsync(cron, AbortToken);

        // then
        removed.Should().BeNull();
        (await store.GetCronJobByIdAsync(cron, AbortToken)).Should().NotBeNull();
        (await store.GetAllCronJobOccurrencesAsync(x => x.CronJobId == cron, AbortToken)).Should().HaveCount(4);
    }

    [Fact]
    public async Task should_answer_null_for_a_cron_job_that_does_not_exist()
    {
        IJobPersistenceProvider<TimeJobEntity, CronJobEntity> store = _Create();

        (await store.RemoveIdleCronJobAsync(Guid.NewGuid(), AbortToken)).Should().BeNull();
    }

    private static async Task<(Guid Job, string Owner)> _StartAsync(
        IJobPersistenceProvider<TimeJobEntity, CronJobEntity> store,
        bool cancel
    )
    {
        var job = new TimeJobEntity
        {
            Id = Guid.NewGuid(),
            Function = _Function,
            Status = JobStatus.Idle,
            ExecutionTime = _Now.UtcDateTime.AddMinutes(-1),
            CreatedAt = _Now.AddMinutes(-2),
            UpdatedAt = _Now.AddMinutes(-2),
        };
        await store.AddTimeJobsAsync([job], AbortToken);
        var claimed = (await store.AcquireImmediateTimeJobsAsync([job.Id], AbortToken))
            .Should()
            .ContainSingle()
            .Subject;

        if (cancel)
        {
            (await store.RequestTimeJobCancellationAsync(job.Id, AbortToken)).Should().BeTrue();
        }

        return (job.Id, claimed.OwnerId!);
    }

    private static async Task _CompleteAsync(
        IJobPersistenceProvider<TimeJobEntity, CronJobEntity> store,
        Guid job,
        string owner
    )
    {
        var completion = new JobExecutionState
        {
            FunctionName = _Function,
            JobId = job,
            ClaimOwner = owner,
        }.SetProperty(x => x.Status, JobStatus.Succeeded);

        (await store.UpdateTimeJobAsync(completion, AbortToken)).Should().Be(1);
    }

    private static async Task<Guid> _AddCronJobAsync(IJobPersistenceProvider<TimeJobEntity, CronJobEntity> store)
    {
        var cron = new CronJobEntity
        {
            Id = Guid.NewGuid(),
            Function = "report.build",
            Expression = "0 0 * * * *",
            ReconciledThroughUtc = _Now.UtcDateTime.AddHours(-1),
            NextDueUtc = _Now.UtcDateTime.AddHours(1),
            CreatedAt = _Now.AddDays(-1),
            UpdatedAt = _Now.AddDays(-1),
        };
        await store.InsertCronJobsAsync([cron], AbortToken);

        return cron.Id;
    }

    private static async Task _AddOccurrencesAsync(
        IJobPersistenceProvider<TimeJobEntity, CronJobEntity> store,
        Guid cronJobId,
        JobStatus status,
        int count
    )
    {
        var occurrences = Enumerable
            .Range(0, count)
            .Select(index => new CronJobOccurrenceEntity<CronJobEntity>
            {
                Id = Guid.NewGuid(),
                CronJobId = cronJobId,
                Status = status,
                ExecutionTime = _Now.UtcDateTime.AddHours(-index - 1),
                CreatedAt = _Now.AddDays(-1),
                UpdatedAt = _Now.AddDays(-1),
            })
            .ToArray();

        (await store.InsertCronJobOccurrencesAsync(occurrences, AbortToken)).Should().Be(count);
    }

    private static JobsInMemoryPersistenceProvider<TimeJobEntity, CronJobEntity> _Create()
    {
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(new FakeTimeProvider(_Now));
        services.AddHeadlessGuidGenerator();
        services.AddSingleton(new SchedulerOptionsBuilder { NodeId = _Owner, LeaseDuration = TimeSpan.FromMinutes(5) });

        return new JobsInMemoryPersistenceProvider<TimeJobEntity, CronJobEntity>(services.BuildServiceProvider());
    }
}
