// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Fencing;
using Headless.Jobs;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests;

/// <summary>
/// The two conditional deletes behind the dashboard's Force delete and cron Delete on a relational store: each checks
/// its condition and deletes in one commit, so a time job that finished, or a cron occurrence that started, after the
/// caller read the row keeps the rows in place. The in-memory store runs the same scenarios in its own tests.
/// </summary>
public abstract class JobsDashboardDeletesConformanceTests<TFixture>(TFixture fixture) : TestBase
    where TFixture : class, IJobsCoordinationFixture
{
    private const string _Function = "billing.charge";
    private const string _CronFunction = "report.build";

    private int _hoursBack;

    public virtual async Task a_force_delete_removes_a_running_job_with_cancel_requested()
    {
        await _RunAsync(
            "deletes-force",
            async (store, ct) =>
            {
                var (job, _) = await _StartAsync(store, cancel: true, ct);

                (await store.RemoveCancelRequestedTimeJobAsync(job, ct)).Should().BeTrue();
                (await store.GetTimeJobByIdAsync(job, ct)).Should().BeNull();
            }
        );
    }

    public virtual async Task a_force_delete_keeps_a_running_job_whose_cancel_was_not_requested()
    {
        await _RunAsync(
            "deletes-force-uncancelled",
            async (store, ct) =>
            {
                var (job, _) = await _StartAsync(store, cancel: false, ct);

                (await store.RemoveCancelRequestedTimeJobAsync(job, ct)).Should().BeFalse();
                (await store.GetJobRunAsync(job, ct))!.Status.Should().Be(JobStatus.InProgress);
            }
        );
    }

    public virtual async Task a_force_delete_keeps_a_job_that_finished_after_it_was_read()
    {
        await _RunAsync(
            "deletes-force-finished",
            async (store, ct) =>
            {
                var (job, lease) = await _StartAsync(store, cancel: true, ct);
                var read = await store.GetJobRunAsync(job, ct);
                read!.Status.Should().Be(JobStatus.InProgress);
                read.CancelRequested.Should().BeTrue();

                var completion = new JobExecutionState
                {
                    FunctionName = _Function,
                    JobId = job,
                    ClaimOwner = lease.Holder,
                    ClaimGeneration = lease.Generation,
                }.SetProperty(x => x.Status, JobStatus.Succeeded);
                (await store.UpdateTimeJobAsync(completion, ct)).Should().Be(1);

                (await store.RemoveCancelRequestedTimeJobAsync(job, ct)).Should().BeFalse();
                (await store.GetJobRunAsync(job, ct))!.Status.Should().Be(JobStatus.Succeeded);
            }
        );
    }

    public virtual async Task a_force_delete_keeps_a_running_keyed_job_with_cancel_requested()
    {
        await _RunAsync(
            "deletes-force-keyed",
            async (store, ct) =>
            {
                var scheduled = await store.ScheduleKeyedTimeJobAsync(
                    new JobKey("invoice-42"),
                    _Job(),
                    cancellationToken: ct
                );
                var job = scheduled.RunId!.Value;
                await _ClaimAndCancelAsync(store, job, ct);

                (await store.RemoveCancelRequestedTimeJobAsync(job, ct)).Should().BeFalse();
                (await store.GetTimeJobByIdAsync(job, ct)).Should().NotBeNull();
            }
        );
    }

    public virtual async Task a_force_delete_keeps_a_running_chain_step_with_cancel_requested()
    {
        await _RunAsync(
            "deletes-force-step",
            async (store, ct) =>
            {
                // The root finished, so its step was claimed and is running.
                var root = _Job();
                root.Status = JobStatus.Succeeded;
                root.ExecutedAt = DateTimeOffset.UtcNow.AddMinutes(-1);
                var step = _Job();
                step.RunCondition = RunCondition.OnSuccess;
                root.Children.Add(step);
                await store.AddTimeJobsAsync([root], ct);
                await _ClaimAndCancelAsync(store, step.Id, ct);

                (await store.RemoveCancelRequestedTimeJobAsync(step.Id, ct)).Should().BeFalse();
                (await store.GetTimeJobByIdAsync(step.Id, ct)).Should().NotBeNull();
                (await store.GetTimeJobByIdAsync(root.Id, ct)).Should().NotBeNull();
            }
        );
    }

    public virtual async Task a_force_delete_keeps_a_running_root_with_cancel_requested_while_it_has_a_chain_step()
    {
        await _RunAsync(
            "deletes-force-root",
            async (store, ct) =>
            {
                var root = _Job();
                var step = _Job();
                step.ExecutionTime = null;
                step.RunCondition = RunCondition.OnSuccess;
                root.Children.Add(step);
                await store.AddTimeJobsAsync([root], ct);
                await _ClaimAndCancelAsync(store, root.Id, ct);

                (await store.RemoveCancelRequestedTimeJobAsync(root.Id, ct)).Should().BeFalse();
                (await store.GetTimeJobByIdAsync(root.Id, ct)).Should().NotBeNull();
                (await store.GetTimeJobByIdAsync(step.Id, ct)).Should().NotBeNull();
            }
        );
    }

    public virtual async Task a_cron_delete_removes_the_job_with_its_twelve_stored_occurrences()
    {
        await _RunAsync(
            "deletes-cron",
            async (store, ct) =>
            {
                var cron = await _SeedCronAsync(ct);
                await _AddOccurrencesAsync(store, cron, JobStatus.Succeeded, 9, ct);
                await _AddOccurrencesAsync(store, cron, JobStatus.Failed, 2, ct);
                await _AddOccurrencesAsync(store, cron, JobStatus.Idle, 1, ct);

                (await store.RemoveIdleCronJobAsync(cron, ct)).Should().Be(12);
                (await store.GetCronJobByIdAsync(cron, ct)).Should().BeNull();
                (await store.GetAllCronJobOccurrencesAsync(x => x.CronJobId == cron, ct)).Should().BeEmpty();
            }
        );
    }

    public virtual async Task a_cron_delete_keeps_the_job_and_its_occurrences_when_one_started_after_the_read()
    {
        await _RunAsync(
            "deletes-cron-running",
            async (store, ct) =>
            {
                var cron = await _SeedCronAsync(ct);
                await _AddOccurrencesAsync(store, cron, JobStatus.Succeeded, 3, ct);
                var running = await store.CountJobRunsAsync(
                    new JobRunQuery { CronJobId = cron, Statuses = [JobStatus.InProgress] },
                    cancellationToken: ct
                );
                running.Count.Should().Be(0);
                await _AddOccurrencesAsync(store, cron, JobStatus.InProgress, 1, ct);

                (await store.RemoveIdleCronJobAsync(cron, ct)).Should().BeNull();
                (await store.GetCronJobByIdAsync(cron, ct)).Should().NotBeNull();
                (await store.GetAllCronJobOccurrencesAsync(x => x.CronJobId == cron, ct)).Should().HaveCount(4);
            }
        );
    }

    #region Helpers

    private async Task _RunAsync(
        string nodeId,
        Func<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>, CancellationToken, Task> scenario
    )
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost(nodeId);
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        await host.StartAsync(ct);

        try
        {
            await scenario(
                host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>(),
                ct
            );
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    private static async Task<(Guid Job, RowLeaseSnapshot Lease)> _StartAsync(
        IJobPersistenceProvider<TimeJobEntity, CronJobEntity> store,
        bool cancel,
        CancellationToken cancellationToken
    )
    {
        var job = new TimeJobEntity
        {
            Id = Guid.NewGuid(),
            Function = _Function,
            Status = JobStatus.Idle,
            ExecutionTime = DateTime.UtcNow.AddMinutes(-1),
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-2),
            UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-2),
        };
        (await store.AddTimeJobsAsync([job], cancellationToken)).Should().Be(1);
        var claimed = (await store.AcquireImmediateTimeJobsAsync([job.Id], cancellationToken))
            .Should()
            .ContainSingle()
            .Subject;

        if (cancel)
        {
            (await store.RequestTimeJobCancellationAsync(job.Id, cancellationToken)).Should().BeTrue();
        }

        return (job.Id, claimed.Lease);
    }

    private static TimeJobEntity _Job() =>
        new()
        {
            Id = Guid.NewGuid(),
            Function = _Function,
            Status = JobStatus.Idle,
            ExecutionTime = DateTime.UtcNow.AddMinutes(-1),
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-2),
            UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-2),
        };

    private static async Task _ClaimAndCancelAsync(
        IJobPersistenceProvider<TimeJobEntity, CronJobEntity> store,
        Guid job,
        CancellationToken cancellationToken
    )
    {
        (await store.AcquireImmediateTimeJobsAsync([job], cancellationToken)).Should().ContainSingle();
        (await store.RequestTimeJobCancellationAsync(job, cancellationToken)).Should().BeTrue();
        var running = (await store.GetJobRunAsync(job, cancellationToken))!;
        running.Status.Should().Be(JobStatus.InProgress);
        running.CancelRequested.Should().BeTrue();
    }

    private async Task<Guid> _SeedCronAsync(CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid();
        await fixture.SeedCronJobAsync(
            id,
            _CronFunction,
            "0 0 * * * *",
            NodeDeathPolicy.Retry,
            cancellationToken,
            reconciledThroughOffsetSeconds: -3600,
            nextDueOffsetSeconds: 3600
        );

        return id;
    }

    private async Task _AddOccurrencesAsync(
        IJobPersistenceProvider<TimeJobEntity, CronJobEntity> store,
        Guid cronJobId,
        JobStatus status,
        int count,
        CancellationToken cancellationToken
    )
    {
        // Whole hours, each one earlier than the last, so no two rows share an execution time and both providers keep
        // the instant.
        var now = DateTime.UtcNow;
        var hour = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0, DateTimeKind.Utc);
        var occurrences = Enumerable
            .Range(0, count)
            .Select(_ => new CronJobOccurrenceEntity<CronJobEntity>
            {
                Id = Guid.NewGuid(),
                CronJobId = cronJobId,
                Status = status,
                ExecutionTime = hour.AddHours(-++_hoursBack),
                CreatedAt = DateTimeOffset.UtcNow.AddHours(-2),
                UpdatedAt = DateTimeOffset.UtcNow.AddHours(-2),
            })
            .ToArray();

        (await store.InsertCronJobOccurrencesAsync(occurrences, cancellationToken)).Should().Be(count);
    }

    #endregion
}
