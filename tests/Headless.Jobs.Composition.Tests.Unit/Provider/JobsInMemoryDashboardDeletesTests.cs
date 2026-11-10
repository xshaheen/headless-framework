// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Headless.Fencing;
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
        var (job, lease) = await _StartAsync(store, cancel: true);
        var read = await store.GetJobRunAsync(job, AbortToken);
        read!.CancelRequested.Should().BeTrue();
        await _CompleteAsync(store, job, lease);

        // when
        var deleted = await store.RemoveCancelRequestedTimeJobAsync(job, AbortToken);

        // then
        deleted.Should().BeFalse();
        (await store.GetJobRunAsync(job, AbortToken))!.Status.Should().Be(JobStatus.Succeeded);
    }

    [Fact]
    public async Task should_keep_a_running_keyed_job_with_cancel_requested()
    {
        // given
        IJobPersistenceProvider<TimeJobEntity, CronJobEntity> store = _Create();
        var candidate = _Job();
        var scheduled = await store.ScheduleKeyedTimeJobAsync(
            new JobKey("invoice-42"),
            candidate,
            cancellationToken: AbortToken
        );
        var job = scheduled.RunId!.Value;
        await _ClaimAndCancelAsync(store, job);

        // when
        var deleted = await store.RemoveCancelRequestedTimeJobAsync(job, AbortToken);

        // then
        deleted.Should().BeFalse();
        (await store.GetTimeJobByIdAsync(job, AbortToken)).Should().NotBeNull();
    }

    [Fact]
    public async Task should_keep_a_running_chain_step_with_cancel_requested()
    {
        // given: the root finished, so its step was claimed and is running
        IJobPersistenceProvider<TimeJobEntity, CronJobEntity> store = _Create();
        var root = _Job();
        root.Status = JobStatus.Succeeded;
        root.ExecutedAt = _Now.AddMinutes(-1);
        var step = _Job();
        step.RunCondition = RunCondition.OnSuccess;
        root.Children.Add(step);
        await store.AddTimeJobsAsync([root], AbortToken);
        await _ClaimAndCancelAsync(store, step.Id);

        // when
        var deleted = await store.RemoveCancelRequestedTimeJobAsync(step.Id, AbortToken);

        // then
        deleted.Should().BeFalse();
        (await store.GetTimeJobByIdAsync(step.Id, AbortToken)).Should().NotBeNull();
        (await store.GetTimeJobByIdAsync(root.Id, AbortToken)).Should().NotBeNull();
    }

    [Fact]
    public async Task should_keep_a_running_root_with_cancel_requested_while_it_has_a_chain_step()
    {
        // given
        IJobPersistenceProvider<TimeJobEntity, CronJobEntity> store = _Create();
        var root = _Job();
        var step = _Job();
        step.ExecutionTime = null;
        step.RunCondition = RunCondition.OnSuccess;
        root.Children.Add(step);
        await store.AddTimeJobsAsync([root], AbortToken);
        await _ClaimAndCancelAsync(store, root.Id);

        // when
        var deleted = await store.RemoveCancelRequestedTimeJobAsync(root.Id, AbortToken);

        // then
        deleted.Should().BeFalse();
        (await store.GetTimeJobByIdAsync(root.Id, AbortToken)).Should().NotBeNull();
        (await store.GetTimeJobByIdAsync(step.Id, AbortToken)).Should().NotBeNull();
    }

    [Fact]
    public async Task should_not_start_an_occurrence_that_a_cron_delete_is_removing()
    {
        // given: the delete has checked that no occurrence runs and is held before it removes anything
        var provider = _Create();
        IJobPersistenceProvider<TimeJobEntity, CronJobEntity> store = provider;
        var cron = await _AddCronJobAsync(store);
        await _AddOccurrencesAsync(store, cron, JobStatus.Idle, 1);
        var occurrence = (await store.GetAllCronJobOccurrencesAsync(x => x.CronJobId == cron, AbortToken)).Single().Id;
        var type = typeof(JobsInMemoryPersistenceProvider<TimeJobEntity, CronJobEntity>);
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;
        var removalGate = (Lock)type.GetField("_cronJobIdIndexLock", flags)!.GetValue(provider)!;
        var definitionLock = type.GetMethod("_GetCronDefinitionLock", flags, [typeof(Guid)])!.Invoke(provider, [cron])!;
        int? removed = null;
        CronJobOccurrenceEntity<CronJobEntity>[] started = [];
        var delete = new Thread(() =>
            removed = store.RemoveIdleCronJobAsync(cron, AbortToken).GetAwaiter().GetResult()
        );
        var acquire = new Thread(() =>
            started = store.AcquireImmediateCronOccurrencesAsync([occurrence], AbortToken).GetAwaiter().GetResult()
        );

        removalGate.Enter();

        try
        {
            delete.Start();
            // The delete reads the occurrences under the definition lock and takes the removal gate next, so a delete
            // that holds the definition lock and is blocked has read them and removed nothing.
            SpinWait
                .SpinUntil(() => _IsBlocked(delete) && _IsHeldElsewhere(definitionLock), TimeSpan.FromSeconds(10))
                .Should()
                .BeTrue("the delete reaches the removal gate");

            // when: a run-now acquire lands between the delete's check and its removal
            acquire.Start();
            SpinWait
                .SpinUntil(() => !acquire.IsAlive || _IsBlocked(acquire), TimeSpan.FromSeconds(10))
                .Should()
                .BeTrue("the acquire either finishes or waits for the delete");
        }
        finally
        {
            removalGate.Exit();
        }

        delete.Join(TimeSpan.FromSeconds(10)).Should().BeTrue();
        acquire.Join(TimeSpan.FromSeconds(10)).Should().BeTrue();

        // then: the delete removed an occurrence nothing started
        removed.Should().Be(1);
        started.Should().BeEmpty();
        (await store.GetAllCronJobOccurrencesAsync(x => x.CronJobId == cron, AbortToken)).Should().BeEmpty();
    }

    [Fact]
    public async Task should_not_start_an_occurrence_of_a_deleted_cron_job()
    {
        // given
        IJobPersistenceProvider<TimeJobEntity, CronJobEntity> store = _Create();
        var cron = await _AddCronJobAsync(store);
        await _AddOccurrencesAsync(store, cron, JobStatus.Idle, 1);
        var occurrence = (await store.GetAllCronJobOccurrencesAsync(x => x.CronJobId == cron, AbortToken)).Single().Id;
        (await store.RemoveIdleCronJobAsync(cron, AbortToken)).Should().Be(1);

        // when
        var started = await store.AcquireImmediateCronOccurrencesAsync([occurrence], AbortToken);

        // then
        started.Should().BeEmpty();
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

    private static async Task<(Guid Job, RowLeaseSnapshot Lease)> _StartAsync(
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

        return (job.Id, claimed.Lease);
    }

    private static bool _IsBlocked(Thread thread) => thread.ThreadState.HasFlag(ThreadState.WaitSleepJoin);

    private static bool _IsHeldElsewhere(object gate)
    {
        var taken = false;

        try
        {
            Monitor.TryEnter(gate, ref taken);

            return !taken;
        }
        finally
        {
            if (taken)
            {
                Monitor.Exit(gate);
            }
        }
    }

    private static TimeJobEntity _Job() =>
        new()
        {
            Id = Guid.NewGuid(),
            Function = _Function,
            Status = JobStatus.Idle,
            ExecutionTime = _Now.UtcDateTime.AddMinutes(-1),
            CreatedAt = _Now.AddMinutes(-2),
            UpdatedAt = _Now.AddMinutes(-2),
        };

    private static async Task _ClaimAndCancelAsync(
        IJobPersistenceProvider<TimeJobEntity, CronJobEntity> store,
        Guid job
    )
    {
        (await store.AcquireImmediateTimeJobsAsync([job], AbortToken)).Should().ContainSingle();
        (await store.RequestTimeJobCancellationAsync(job, AbortToken)).Should().BeTrue();
        var running = (await store.GetJobRunAsync(job, AbortToken))!;
        running.Status.Should().Be(JobStatus.InProgress);
        running.CancelRequested.Should().BeTrue();
    }

    private static async Task _CompleteAsync(
        IJobPersistenceProvider<TimeJobEntity, CronJobEntity> store,
        Guid job,
        RowLeaseSnapshot lease
    )
    {
        var completion = new JobExecutionState
        {
            FunctionName = _Function,
            JobId = job,
            ClaimOwner = lease.Holder,
            ClaimGeneration = lease.Generation,
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
