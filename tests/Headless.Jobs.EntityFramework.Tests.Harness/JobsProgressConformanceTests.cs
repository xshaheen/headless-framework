// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Coordination;
using Headless.Jobs;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

/// <summary>
/// Stored job progress on a relational store: a progress write lands only on the running row its node owns, is stamped
/// with the database clock, and is read back by any node, as a dashboard refresh does. The terminal write carries the
/// report the throttle had not written yet, crash recovery keeps progress for the next attempt, and a requeue clears it.
/// A job the real scheduler runs proves the throttle against the store: hundreds of reports cost at most two writes.
/// </summary>
public abstract class JobsProgressConformanceTests<TFixture>(TFixture fixture) : TestBase
    where TFixture : class, IJobsCoordinationFixture
{
    private const string _Function = "progress-job";
    private const string _ForeignOwner = "other-node@9";
    private static readonly TimeSpan _ClockTolerance = TimeSpan.FromSeconds(30);

    public virtual async Task time_job_progress_is_fenced_to_the_owning_node_and_read_back_by_any_node()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var writer = fixture.BuildHost("progress-writer");
        using var reader = fixture.BuildHost("progress-reader");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(writer, ct);
        await writer.StartAsync(ct);

        try
        {
            var owner = _Owner(writer);
            var lease = DateTime.UtcNow.AddMinutes(5);
            var owned = Guid.NewGuid();
            var foreign = Guid.NewGuid();
            var queued = Guid.NewGuid();
            await fixture.SeedTimeJobAsync(owned, _Function, (int)JobStatus.InProgress, owner, ct, lockedUntil: lease);
            await fixture.SeedTimeJobAsync(
                foreign,
                _Function,
                (int)JobStatus.InProgress,
                _ForeignOwner,
                ct,
                lockedUntil: lease
            );
            await fixture.SeedTimeJobAsync(queued, _Function, (int)JobStatus.Queued, owner, ct, lockedUntil: lease);
            var store = _Store(writer);

            (await store.UpdateTimeJobProgressAsync(owned, new(10, "warming up"), ct)).Should().BeTrue();
            (await store.UpdateTimeJobProgressAsync(owned, new(42.5, "halfway"), ct)).Should().BeTrue();
            (await store.UpdateTimeJobProgressAsync(foreign, new(99, "not mine"), ct)).Should().BeFalse();
            (await store.UpdateTimeJobProgressAsync(queued, new(99, "not running"), ct)).Should().BeFalse();

            // A dashboard refresh is a fresh read through any node's store, here one that never wrote anything.
            var page = await _Store(reader).GetTimeJobsPaginatedAsync(x => x.Function == _Function, 1, 10, ct);
            var ownedRow = page.Items.Single(x => x.Id == owned);
            ownedRow.ProgressPercent.Should().Be(42.5);
            ownedRow.ProgressMessage.Should().Be("halfway");
            ownedRow.ProgressUpdatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, _ClockTolerance);
            page.Items.Single(x => x.Id == foreign).ProgressPercent.Should().BeNull();
            page.Items.Single(x => x.Id == queued).ProgressPercent.Should().BeNull();

            // Each report replaces the stored one whole, so a report without a message clears it.
            (await store.UpdateTimeJobProgressAsync(owned, new(50), ct))
                .Should()
                .BeTrue();
            var cleared = (await _Store(reader).GetTimeJobByIdAsync(owned, ct))!;
            cleared.ProgressPercent.Should().Be(50);
            cleared.ProgressMessage.Should().BeNull();
        }
        finally
        {
            await writer.StopAsync(ct);
        }
    }

    public virtual async Task cron_occurrence_progress_is_fenced_to_the_owning_node_and_read_back_by_any_node()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var writer = fixture.BuildHost("progress-writer");
        using var reader = fixture.BuildHost("progress-reader");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(writer, ct);
        await writer.StartAsync(ct);

        try
        {
            var owner = _Owner(writer);
            var lease = DateTime.UtcNow.AddMinutes(5);
            var cronId = Guid.NewGuid();
            await fixture.SeedCronJobAsync(cronId, _Function, "* * * * *", NodeDeathPolicy.Retry, ct);
            var owned = Guid.NewGuid();
            var foreign = Guid.NewGuid();
            // Distinct instants: (CronJobId, ExecutionTime) is unique.
            var instant = DateTime.UtcNow.AddHours(-1);
            await fixture.SeedCronOccurrenceAsync(
                owned,
                cronId,
                (int)JobStatus.InProgress,
                owner,
                NodeDeathPolicy.Retry,
                lease,
                instant,
                ct
            );
            await fixture.SeedCronOccurrenceAsync(
                foreign,
                cronId,
                (int)JobStatus.InProgress,
                _ForeignOwner,
                NodeDeathPolicy.Retry,
                lease,
                instant.AddMinutes(1),
                ct
            );
            var store = _Store(writer);

            (await store.UpdateCronJobOccurrenceProgressAsync(owned, new(75, "nearly"), ct)).Should().BeTrue();
            (await store.UpdateCronJobOccurrenceProgressAsync(foreign, new(75), ct)).Should().BeFalse();

            var rows = await _Store(reader).GetAllCronJobOccurrencesAsync(x => x.CronJobId == cronId, ct);
            var ownedRow = rows.Single(x => x.Id == owned);
            ownedRow.ProgressPercent.Should().Be(75);
            ownedRow.ProgressMessage.Should().Be("nearly");
            ownedRow.ProgressUpdatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, _ClockTolerance);
            rows.Single(x => x.Id == foreign).ProgressPercent.Should().BeNull();
        }
        finally
        {
            await writer.StopAsync(ct);
        }
    }

    public virtual async Task progress_writes_stamp_the_database_clock_not_the_node_clock()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        // A node whose clock runs two hours ahead must still stamp the store's time, or the dashboard's "updated ago"
        // would disagree across nodes.
        var skewed = new FakeTimeProvider(DateTimeOffset.UtcNow.AddHours(2));
        using var host = fixture.BuildHost("progress-skewed", timeProvider: skewed);
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        await host.StartAsync(ct);

        try
        {
            var id = Guid.NewGuid();
            await fixture.SeedTimeJobAsync(
                id,
                _Function,
                (int)JobStatus.InProgress,
                _Owner(host),
                ct,
                lockedUntil: DateTime.UtcNow.AddMinutes(5)
            );
            var store = _Store(host);

            (await store.UpdateTimeJobProgressAsync(id, new(5), ct)).Should().BeTrue();

            (await store.GetTimeJobByIdAsync(id, ct))!
                .ProgressUpdatedAt.Should()
                .BeCloseTo(DateTimeOffset.UtcNow, _ClockTolerance);
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    public virtual async Task progress_writes_nothing_while_membership_is_not_established()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        // Not started: there is no owner identity to fence on.
        using var host = fixture.BuildHost("progress-unstarted");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        var id = Guid.NewGuid();
        await fixture.SeedTimeJobAsync(
            id,
            _Function,
            (int)JobStatus.InProgress,
            "progress-unstarted@1",
            ct,
            lockedUntil: DateTime.UtcNow.AddMinutes(5)
        );
        var store = _Store(host);

        (await store.UpdateTimeJobProgressAsync(id, new(5), ct)).Should().BeFalse();
        (await store.GetTimeJobByIdAsync(id, ct))!.ProgressPercent.Should().BeNull();
    }

    public virtual async Task terminal_writes_store_the_report_the_throttle_had_not_written()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost("progress-terminal");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        await host.StartAsync(ct);

        try
        {
            var owner = _Owner(host);
            var lease = DateTime.UtcNow.AddMinutes(5);
            var timeJob = Guid.NewGuid();
            await fixture.SeedTimeJobAsync(
                timeJob,
                _Function,
                (int)JobStatus.InProgress,
                owner,
                ct,
                lockedUntil: lease
            );
            var cronId = Guid.NewGuid();
            await fixture.SeedCronJobAsync(cronId, _Function, "* * * * *", NodeDeathPolicy.Retry, ct);
            var occurrence = Guid.NewGuid();
            await fixture.SeedCronOccurrenceAsync(
                occurrence,
                cronId,
                (int)JobStatus.InProgress,
                owner,
                NodeDeathPolicy.Retry,
                lease,
                DateTime.UtcNow.AddHours(-1),
                ct
            );
            var store = _Store(host);
            await store.UpdateTimeJobProgressAsync(timeJob, new(60, "last written"), ct);

            var timeJobAffected = await store.UpdateTimeJobAsync(
                _Terminal(timeJob, JobType.TimeJob, JobStatus.Succeeded, new JobProgress(100, "done")),
                ct
            );
            var occurrenceAffected = await store.UpdateCronJobOccurrenceAsync(
                _Terminal(occurrence, JobType.CronJobOccurrence, JobStatus.Failed, new JobProgress(30, "failed here")),
                ct
            );

            timeJobAffected.Should().Be(1);
            occurrenceAffected.Should().Be(1);
            var storedJob = (await store.GetTimeJobByIdAsync(timeJob, ct))!;
            storedJob.Status.Should().Be(JobStatus.Succeeded);
            storedJob.ProgressPercent.Should().Be(100);
            storedJob.ProgressMessage.Should().Be("done");
            storedJob.ProgressUpdatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, _ClockTolerance);
            var storedOccurrence = (await store.GetAllCronJobOccurrencesAsync(x => x.Id == occurrence, ct)).Single();
            storedOccurrence.Status.Should().Be(JobStatus.Failed);
            storedOccurrence.ProgressPercent.Should().Be(30);
            storedOccurrence.ProgressMessage.Should().Be("failed here");
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    public virtual async Task crash_recovery_keeps_progress_and_a_requeue_clears_it()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost("progress-recovery");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        await host.StartAsync(ct);

        try
        {
            var owner = _Owner(host);
            var crashed = Guid.NewGuid();
            var failed = Guid.NewGuid();
            var lease = DateTime.UtcNow.AddMinutes(5);
            await fixture.SeedTimeJobAsync(
                crashed,
                _Function,
                (int)JobStatus.InProgress,
                owner,
                ct,
                lockedUntil: lease
            );
            await fixture.SeedTimeJobAsync(failed, _Function, (int)JobStatus.InProgress, owner, ct, lockedUntil: lease);
            var store = _Store(host);
            await store.UpdateTimeJobProgressAsync(crashed, new(80, "before the crash"), ct);
            await store.UpdateTimeJobProgressAsync(failed, new(40, "failed here"), ct);

            // The crashed run's lease lapses; the stalled sweep releases it for another attempt.
            await _ExpireLeaseAsync(crashed, ct);
            (await store.ReclaimStalledTimeJobsAsync(ct)).Should().Be(1);
            var released = (await store.GetTimeJobByIdAsync(crashed, ct))!;
            released.Status.Should().Be(JobStatus.Idle);
            released.RetryCount.Should().Be(1);
            released.ProgressPercent.Should().Be(80);
            released.ProgressMessage.Should().Be("before the crash");

            // A requeue starts a new run, so the failed run's progress goes.
            (await store.UpdateTimeJobAsync(_Terminal(failed, JobType.TimeJob, JobStatus.Failed, progress: null), ct))
                .Should()
                .Be(1);
            (await store.GetTimeJobByIdAsync(failed, ct))!.ProgressPercent.Should().Be(40);
            (await store.RequeueTimeJobAsync(failed, ct)).Should().Be(JobRequeueOutcome.Requeued);
            var requeued = (await store.GetTimeJobByIdAsync(failed, ct))!;
            requeued.ProgressPercent.Should().BeNull();
            requeued.ProgressMessage.Should().BeNull();
            requeued.ProgressUpdatedAt.Should().BeNull();
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    public virtual async Task frequent_reports_from_a_running_job_cost_at_most_two_progress_writes()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        var writes = new ProgressWriteCounter();
        // A one-minute interval makes the bound exact: the run finishes long before a second interval write is due,
        // so only the leading write and the terminal write may touch the progress columns.
        using var host = fixture.BuildHost(
            "progress-runner",
            interceptor: writes,
            configureJobs: jobs => jobs.ConfigureScheduler(s => s.ProgressReportInterval = TimeSpan.FromMinutes(1)),
            runBackgroundServices: true
        );
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        await host.StartAsync(ct);

        try
        {
            var store = _Store(host);
            var id = Guid.NewGuid();
            await store.AddTimeJobsAsync(
                [
                    new TimeJobEntity
                    {
                        Id = id,
                        Function = JobsCoordinationFixtureExtensions.CoordinatedProgressFunctionName,
                        ExecutionTime = DateTime.UtcNow,
                    },
                ],
                ct
            );

            var stored = await _WaitForTerminalAsync(store, id, ct);

            // DueDone is the success status of a run picked up after its due time, as this one may be.
            stored.Status.Should().BeOneOf(JobStatus.Succeeded, JobStatus.DueDone);
            stored.ProgressPercent.Should().Be(100);
            stored
                .ProgressMessage.Should()
                .Be(
                    $"report {JobsCoordinationFixtureExtensions.ProgressReports.ToString(System.Globalization.CultureInfo.InvariantCulture)}"
                );
            writes
                .Count.Should()
                .BeInRange(1, 2, "the throttle coalesces the reports into the leading write and the terminal write");
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    public virtual async Task a_requeued_cron_occurrence_clears_its_progress()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost("progress-requeue");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        await host.StartAsync(ct);

        try
        {
            var cronId = Guid.NewGuid();
            await fixture.SeedCronJobAsync(cronId, _Function, "* * * * *", NodeDeathPolicy.Retry, ct);
            var occurrence = Guid.NewGuid();
            await fixture.SeedCronOccurrenceAsync(
                occurrence,
                cronId,
                (int)JobStatus.InProgress,
                _Owner(host),
                NodeDeathPolicy.Retry,
                DateTime.UtcNow.AddMinutes(5),
                DateTime.UtcNow.AddHours(-1),
                ct
            );
            var store = _Store(host);
            await store.UpdateCronJobOccurrenceProgressAsync(occurrence, new(40, "failed here"), ct);
            await store.UpdateCronJobOccurrenceAsync(
                _Terminal(occurrence, JobType.CronJobOccurrence, JobStatus.Failed, progress: null),
                ct
            );

            (await store.RequeueCronJobOccurrenceAsync(occurrence, ct)).Should().Be(JobRequeueOutcome.Requeued);

            var requeued = (await store.GetAllCronJobOccurrencesAsync(x => x.Id == occurrence, ct)).Single();
            requeued.ProgressPercent.Should().BeNull();
            requeued.ProgressMessage.Should().BeNull();
            requeued.ProgressUpdatedAt.Should().BeNull();
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    private async Task _ExpireLeaseAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = fixture.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = fixture.Sql(
            $"UPDATE {fixture.QualifiedTimeJobsTable} SET \"LockedUntil\" = @past WHERE \"Id\" = @id;"
        );
        var past = command.CreateParameter();
        past.ParameterName = "@past";
        past.Value = DateTime.UtcNow.AddMinutes(-10);
        command.Parameters.Add(past);
        var idParameter = command.CreateParameter();
        idParameter.ParameterName = "@id";
        idParameter.Value = id;
        command.Parameters.Add(idParameter);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static JobExecutionState _Terminal(Guid id, JobType type, JobStatus status, JobProgress? progress)
    {
        var state = new JobExecutionState
        {
            JobId = id,
            FunctionName = _Function,
            Type = type,
        }
            .SetProperty(x => x.Status, status)
            .SetProperty(x => x.ExecutedAt, DateTimeOffset.UtcNow);

        return progress is null ? state : state.SetProperty(x => x.Progress, progress);
    }

    private static string _Owner(IHost host) =>
        host.Services.GetRequiredService<INodeMembership>().Identity!.Value.ToString();

    private static IJobPersistenceProvider<TimeJobEntity, CronJobEntity> _Store(IHost host) =>
        host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();

    private static async Task<TimeJobEntity> _WaitForTerminalAsync(
        IJobPersistenceProvider<TimeJobEntity, CronJobEntity> store,
        Guid id,
        CancellationToken cancellationToken
    )
    {
        var deadline = DateTime.UtcNow.AddSeconds(60);
        TimeJobEntity? job = null;
        while (DateTime.UtcNow < deadline)
        {
            job = await store.GetTimeJobByIdAsync(id, cancellationToken);
            if (
                job?.Status
                is JobStatus.Succeeded
                    or JobStatus.DueDone
                    or JobStatus.Failed
                    or JobStatus.Cancelled
                    or JobStatus.Skipped
            )
            {
                return job;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
        }

        throw new TimeoutException(
            $"Job {id} did not finish within 60 s; last state: status={job?.Status}, owner={job?.OwnerId}, "
                + $"lockedUntil={job?.LockedUntil:O}, executionTime={job?.ExecutionTime:O}, progress={job?.ProgressPercent}, "
                + $"exception={job?.ExceptionMessage}"
        );
    }

    /// <summary>Counts the statements that write the progress columns, whatever the provider's column naming.</summary>
    private sealed class ProgressWriteCounter : DbCommandInterceptor
    {
        private int _count;

        public int Count => Volatile.Read(ref _count);

        public override InterceptionResult<int> NonQueryExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result
        )
        {
            _Count(command);
            return base.NonQueryExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        )
        {
            _Count(command);
            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }

        private void _Count(DbCommand command)
        {
            var text = command.CommandText.Replace("_", "", StringComparison.Ordinal);
            if (
                text.TrimStart().StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase)
                && text.Contains("progresspercent", StringComparison.OrdinalIgnoreCase)
            )
            {
                Interlocked.Increment(ref _count);
            }
        }
    }
}
