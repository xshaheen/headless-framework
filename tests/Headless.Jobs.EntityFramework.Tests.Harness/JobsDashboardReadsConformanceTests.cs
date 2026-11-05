// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Coordination;
using Headless.Jobs;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests;

/// <summary>
/// The dashboard's reads on a relational store: one keyset run list over time jobs and cron occurrences, capped counts,
/// failure groups that apply the requeue rules and the cron exit rule in the query, Operation scopes frozen per command
/// with their compare-and-set writes, the Overview figures, and per-function stats. The in-memory store runs the same
/// scenarios in its own tests.
/// </summary>
public abstract class JobsDashboardReadsConformanceTests<TFixture>(TFixture fixture) : TestBase
    where TFixture : class, IJobsCoordinationFixture
{
    private const string _Function = "billing.charge";
    private const string _CronFunction = "report.build";

    public virtual async Task the_run_list_pages_both_kinds_in_execution_time_order_with_no_repeat_or_gap()
    {
        await _RunAsync(
            "reads-paging",
            async (store, _, ct) =>
            {
                var at = _Minute(-60);
                var cron = await _SeedCronAsync(ct);
                var t0 = await _AddTimeJobAsync(store, JobStatus.Succeeded, at, ct);
                var o1 = await _AddOccurrenceAsync(store, cron, JobStatus.Succeeded, at.AddMinutes(1), ct);
                var t2 = await _AddTimeJobAsync(store, JobStatus.Succeeded, at.AddMinutes(2), ct);
                var o3 = await _AddOccurrenceAsync(store, cron, JobStatus.Succeeded, at.AddMinutes(3), ct);
                var t4 = await _AddTimeJobAsync(store, JobStatus.Succeeded, at.AddMinutes(4), ct);
                var o4 = await _AddOccurrenceAsync(store, cron, JobStatus.Succeeded, at.AddMinutes(4), ct);
                var o5 = await _AddOccurrenceAsync(store, cron, JobStatus.Succeeded, at.AddMinutes(5), ct);
                var t8 = await _AddTimeJobAsync(store, JobStatus.Succeeded, at.AddMinutes(8), ct);
                var o9 = await _AddOccurrenceAsync(store, cron, JobStatus.Succeeded, at.AddMinutes(9), ct);

                var oldestFirst = await _ReadAllPagesAsync(store, new JobRunQuery(), pageSize: 3, ct);
                var newestFirst = await _ReadAllPagesAsync(store, new JobRunQuery { NewestFirst = true }, 4, ct);
                var occurrencesOnly = await _ReadAllPagesAsync(
                    store,
                    new JobRunQuery { CronJobId = cron },
                    pageSize: 2,
                    ct
                );

                oldestFirst.Should().Equal(t0, o1, t2, o3, t4, o4, o5, t8, o9);
                newestFirst.Should().Equal(o9, t8, o5, t4, o4, o3, t2, o1, t0);
                occurrencesOnly.Should().Equal(o1, o3, o4, o5, o9);
            }
        );
    }

    public virtual async Task a_count_over_ten_thousand_rows_stops_at_ten_thousand_and_reports_capped()
    {
        await _RunAsync(
            "reads-count",
            async (store, _, ct) =>
            {
                var at = _Minute(-60);
                var jobs = Enumerable
                    .Range(0, JobRunCount.DefaultCap + 1)
                    .Select(index => _TimeJob(JobStatus.Succeeded, at.AddSeconds(index)))
                    .ToArray();
                await store.AddTimeJobsAsync(jobs, ct);
                await _AddTimeJobAsync(store, JobStatus.Failed, at, ct, function: "other.job");

                var all = await store.CountJobRunsAsync(new JobRunQuery(), cancellationToken: ct);
                var other = await store.CountJobRunsAsync(
                    new JobRunQuery { Functions = ["other.job"], Statuses = [JobStatus.Failed] },
                    cancellationToken: ct
                );

                all.Should().Be(new JobRunCount(10_000, Capped: true));
                other.Should().Be(new JobRunCount(1, Capped: false));
            }
        );
    }

    public virtual async Task a_failed_occurrence_leaves_its_group_once_a_later_one_succeeds_and_a_failed_time_job_stays_until_requeued()
    {
        await _RunAsync(
            "reads-groups",
            async (store, _, ct) =>
            {
                var at = _Minute(-60);
                var cron = await _SeedCronAsync(ct);
                await _AddOccurrenceAsync(store, cron, JobStatus.Succeeded, at.AddMinutes(-10), ct);
                await _AddOccurrenceAsync(store, cron, JobStatus.Failed, at, ct, fingerprint: "v1:occurrence");
                var job = await _AddTimeJobAsync(store, JobStatus.Failed, at, ct, fingerprint: "v1:time");
                await _AddTimeJobAsync(
                    store,
                    JobStatus.Failed,
                    at,
                    ct,
                    fingerprint: "v1:time",
                    executedAt: DateTimeOffset.UtcNow.AddHours(-3)
                );

                var before = await store.GetJobFailureGroupsAsync(DateTimeOffset.UtcNow.AddHours(-1), null, ct);
                await _AddOccurrenceAsync(store, cron, JobStatus.Succeeded, at.AddMinutes(10), ct);
                var afterLaterSuccess = await store.GetJobFailureGroupsAsync(
                    DateTimeOffset.UtcNow.AddHours(-1),
                    null,
                    ct
                );
                (await store.RequeueTimeJobAsync(job, traceParent: null, ct)).Should().Be(JobRequeueOutcome.Requeued);
                var afterRequeue = await store.GetJobFailureGroupsAsync(DateTimeOffset.UtcNow.AddHours(-1), null, ct);

                before
                    .Select(x => (x.Function, x.FailureFingerprint, x.Count, x.RecentCount))
                    .Should()
                    .BeEquivalentTo([(_CronFunction, "v1:occurrence", 1, 1), (_Function, "v1:time", 2, 1)]);
                var timeGroup = afterLaterSuccess.Should().ContainSingle().Subject;
                timeGroup.Function.Should().Be(_Function);
                timeGroup.LastFailedAt.Should().BeAfter(timeGroup.FirstFailedAt);
                afterRequeue
                    .Select(x => (x.Function, x.FailureFingerprint, x.Count))
                    .Should()
                    .Equal((_Function, "v1:time", 1));
            }
        );
    }

    public virtual async Task a_superseded_keyed_job_and_a_chain_member_are_not_actionable()
    {
        await _RunAsync(
            "reads-actionable",
            async (store, _, ct) =>
            {
                var at = _Minute(-60);
                var plain = await _AddTimeJobAsync(store, JobStatus.Failed, at, ct);
                var child = _TimeJob(JobStatus.Failed, executionTime: null);
                var parent = _TimeJob(JobStatus.Failed, at);
                parent.Children = [child];
                await store.AddTimeJobsAsync([parent], ct);
                var key = new JobKey("reads-key");
                var first = await store.ScheduleKeyedTimeJobAsync(key, _KeyedCandidate(), cancellationToken: ct);
                var second = await store.ScheduleKeyedTimeJobAsync(key, _KeyedCandidate(), 1, ct);
                await _MarkTimeJobFailedAsync(first.RunId!.Value, ct);
                await _MarkTimeJobFailedAsync(second.RunId!.Value, ct);

                var query = new JobRunQuery { ActionableFailuresOnly = true };
                var count = await store.CountJobRunsAsync(query, cancellationToken: ct);
                var listed = await _ReadAllPagesAsync(store, query, pageSize: 10, ct);

                count.Should().Be(new JobRunCount(2, Capped: false));
                listed.Should().BeEquivalentTo([plain, second.RunId.Value]);
                (await store.RequeueTimeJobAsync(first.RunId.Value, traceParent: null, ct))
                    .Should()
                    .Be(JobRequeueOutcome.SupersededGeneration);
            }
        );
    }

    public virtual async Task a_requeue_scope_leaves_out_a_run_changed_after_the_high_water_mark()
    {
        await _RunAsync(
            "reads-requeue-scope",
            async (store, _, ct) =>
            {
                var at = _Minute(-60);
                var cron = await _SeedCronAsync(ct);
                var unchanged = await _AddTimeJobAsync(store, JobStatus.Failed, at, ct);
                var refailed = await _AddTimeJobAsync(store, JobStatus.Failed, at, ct);
                var occurrence = await _AddOccurrenceAsync(store, cron, JobStatus.Failed, at, ct);
                var highWater = (await store.GetLatestChangeMarkerAsync(ct))!.Value;
                var staleMarker = (await store.GetJobRunAsync(refailed, ct))!.ChangedAt;

                // A chunk requeued the run and it failed again after the Operation was confirmed.
                (await store.RequeueTimeJobAsync(refailed, traceParent: null, ct))
                    .Should()
                    .Be(JobRequeueOutcome.Requeued);
                var claimed = (await store.AcquireImmediateTimeJobsAsync([refailed], ct))
                    .Should()
                    .ContainSingle()
                    .Subject;
                var fail = new JobExecutionState
                {
                    FunctionName = _Function,
                    JobId = refailed,
                    ClaimOwner = claimed.OwnerId!,
                }.SetProperty(x => x.Status, JobStatus.Failed);
                (await store.UpdateTimeJobAsync(fail, ct)).Should().Be(1);

                var scope = _Scope(JobOperationKind.Requeue, highWater);
                var chunk = await store.ReadJobOperationScopeAsync(scope, after: null, chunkSize: 10, ct);
                var reach = await store.CountJobOperationScopeAsync(scope, cancellationToken: ct);

                chunk.Targets.Select(x => x.Id).Should().Equal(unchanged, occurrence);
                reach.Should().Be(new JobRunCount(2, Capped: false));
                var occurrenceMarker = chunk.Targets[1].ChangedAt;
                (
                    await store.RequeueCronJobOccurrenceIfUnchangedAsync(
                        occurrence,
                        occurrenceMarker.AddTicks(-10),
                        null,
                        ct
                    )
                )
                    .Should()
                    .Be(JobRequeueOutcome.Conflict);
                (await store.RequeueCronJobOccurrenceIfUnchangedAsync(occurrence, occurrenceMarker, null, ct))
                    .Should()
                    .Be(JobRequeueOutcome.Requeued);
                (await store.RequeueTimeJobIfUnchangedAsync(refailed, staleMarker, traceParent: null, ct))
                    .Should()
                    .Be(JobRequeueOutcome.Conflict);
                (await store.RequeueTimeJobIfUnchangedAsync(unchanged, chunk.Targets[0].ChangedAt, null, ct))
                    .Should()
                    .Be(JobRequeueOutcome.Requeued);
                (await store.RequeueTimeJobIfUnchangedAsync(unchanged, chunk.Targets[0].ChangedAt, null, ct))
                    .Should()
                    .Be(JobRequeueOutcome.NotFailed, "a resent chunk changes nothing");
            }
        );
    }

    public virtual async Task a_cancel_scope_keeps_a_running_run_that_reported_progress_after_confirm()
    {
        await _RunAsync(
            "reads-cancel-scope",
            async (store, _, ct) =>
            {
                var running = await _AddTimeJobAsync(store, JobStatus.Idle, _Minute(-60), ct);
                var claimed = (await store.AcquireImmediateTimeJobsAsync([running], ct))
                    .Should()
                    .ContainSingle()
                    .Subject;
                var highWater = (await store.GetLatestChangeMarkerAsync(ct))!.Value;

                (await store.UpdateTimeJobProgressAsync(running, claimed.OwnerId!, new JobProgress(40), ct))
                    .Should()
                    .BeTrue();
                var cancel = await store.ReadJobOperationScopeAsync(
                    _Scope(JobOperationKind.Cancel, highWater),
                    after: null,
                    chunkSize: 10,
                    ct
                );
                var delete = await store.ReadJobOperationScopeAsync(
                    _Scope(JobOperationKind.Delete, highWater),
                    after: null,
                    chunkSize: 10,
                    ct
                );

                cancel.Targets.Select(x => x.Id).Should().Equal(running);
                cancel.Targets[0].ChangedAt.Should().BeAfter(highWater, "the progress write advanced the marker");
                delete.Targets.Should().BeEmpty("a running run is never deleted");
            }
        );
    }

    public virtual async Task a_delete_applies_only_while_the_marker_is_unchanged()
    {
        await _RunAsync(
            "reads-delete",
            async (store, _, ct) =>
            {
                var at = _Minute(-60);
                var cron = await _SeedCronAsync(ct);
                var job = await _AddTimeJobAsync(store, JobStatus.Failed, at, ct);
                var occurrence = await _AddOccurrenceAsync(store, cron, JobStatus.Failed, at, ct);
                var highWater = (await store.GetLatestChangeMarkerAsync(ct))!.Value;
                var chunk = await store.ReadJobOperationScopeAsync(
                    _Scope(JobOperationKind.Delete, highWater),
                    after: null,
                    chunkSize: 1,
                    ct
                );
                var next = await store.ReadJobOperationScopeAsync(
                    _Scope(JobOperationKind.Delete, highWater),
                    chunk.Next,
                    chunkSize: 10,
                    ct
                );
                var targets = chunk.Targets.Concat(next.Targets).ToArray();
                targets.Select(x => x.Id).Should().Equal(job, occurrence);

                (await store.RequeueTimeJobAsync(job, traceParent: null, ct)).Should().Be(JobRequeueOutcome.Requeued);
                var staleDelete = await store.RemoveTimeJobIfUnchangedAsync(job, targets[0].ChangedAt, ct);
                var delete = await store.RemoveCronJobOccurrenceIfUnchangedAsync(occurrence, targets[1].ChangedAt, ct);

                staleDelete.Should().BeFalse();
                (await store.GetTimeJobByIdAsync(job, ct)).Should().NotBeNull();
                delete.Should().BeTrue();
                (await store.GetCronJobOccurrenceByIdAsync(occurrence, ct)).Should().BeNull();
            }
        );
    }

    public virtual async Task the_overview_figures_count_the_last_24_hours_and_the_24_hours_before()
    {
        await _RunAsync(
            "reads-overview",
            async (store, _, ct) =>
            {
                var now = _Minute(0);
                var at = now.AddHours(-1);
                var cron = await _SeedCronAsync(ct);
                await _AddTimeJobAsync(store, JobStatus.Succeeded, at, ct, executedAt: _Ago(now, hours: 1));
                await _AddTimeJobAsync(store, JobStatus.DueDone, at, ct, executedAt: _Ago(now, hours: 2));
                await _AddTimeJobAsync(store, JobStatus.Failed, at, ct, executedAt: _Ago(now, hours: 3));
                await _AddOccurrenceAsync(store, cron, JobStatus.Succeeded, at, ct, executedAt: _Ago(now, hours: 4));
                await _AddTimeJobAsync(store, JobStatus.Succeeded, at, ct, executedAt: _Ago(now, hours: 30));
                await _AddTimeJobAsync(store, JobStatus.Failed, at, ct, executedAt: _Ago(now, hours: 26));
                await _AddOccurrenceAsync(
                    store,
                    cron,
                    JobStatus.Failed,
                    at.AddHours(-30),
                    ct,
                    executedAt: _Ago(now, hours: 28)
                );
                await _StartAsync(store, due: now.AddMinutes(-10), started: now.AddMinutes(-5), ct);
                await _StartAsync(store, due: now.AddMinutes(-11), started: now.AddMinutes(-11).AddSeconds(30), ct);
                await _StartAsync(store, due: now.AddHours(-31), started: now.AddHours(-30), ct);

                var figures = await store.GetJobOverviewFiguresAsync(now, TimeSpan.FromMinutes(1), ct);

                figures.Current.Should().Be(new JobRunWindowFigures(Succeeded: 3, Failed: 1, Late: 1));
                figures.Previous.Should().Be(new JobRunWindowFigures(Succeeded: 1, Failed: 2, Late: 1));
            }
        );
    }

    public virtual async Task the_trace_list_and_the_id_reads_return_the_runs_they_name()
    {
        await _RunAsync(
            "reads-trace",
            async (store, _, ct) =>
            {
                var at = _Minute(-60);
                var cron = await _SeedCronAsync(ct);
                var first = await _AddTimeJobAsync(store, JobStatus.Succeeded, at, ct, correlationId: "order-7");
                var second = await _AddTimeJobAsync(
                    store,
                    JobStatus.Failed,
                    at.AddMinutes(1),
                    ct,
                    correlationId: "order-7"
                );
                await _AddTimeJobAsync(store, JobStatus.Succeeded, at, ct, correlationId: "order-8");
                var occurrence = await _AddOccurrenceAsync(store, cron, JobStatus.Succeeded, at, ct);

                var traced = await _ReadAllPagesAsync(store, new JobRunQuery { CorrelationId = "order-7" }, 10, ct);
                var resolved = await store.GetJobRunAsync(occurrence, ct);
                var stored = await store.GetCronJobOccurrenceByIdAsync(occurrence, ct);

                traced.Should().Equal(first, second);
                resolved!.Kind.Should().Be(JobRunKind.CronJobOccurrence);
                resolved.CronJobId.Should().Be(cron);
                stored!.CronJobId.Should().Be(cron);
                (await store.GetJobRunAsync(second, ct))!.Kind.Should().Be(JobRunKind.TimeJob);
                (await store.GetJobRunAsync(Guid.NewGuid(), ct)).Should().BeNull();
            }
        );
    }

    public virtual async Task per_function_stats_count_both_kinds()
    {
        await _RunAsync(
            "reads-stats",
            async (store, _, ct) =>
            {
                var now = _Minute(0);
                var at = now.AddHours(-1);
                var cron = await _SeedCronAsync(ct, function: _Function);
                await _AddTimeJobAsync(store, JobStatus.Succeeded, at, ct, executedAt: _Ago(now, hours: 1));
                await _AddTimeJobAsync(store, JobStatus.Failed, at, ct, executedAt: _Ago(now, hours: 2));
                await _AddTimeJobAsync(store, JobStatus.Idle, now.AddHours(2), ct);
                var latest = new DateTimeOffset(now.AddMinutes(-5));
                await _AddOccurrenceAsync(store, cron, JobStatus.Succeeded, at, ct, executedAt: latest);
                await _AddTimeJobAsync(store, JobStatus.Succeeded, at, ct, executedAt: _Ago(now, hours: 72));

                var stats = await store.GetJobFunctionStatsAsync(new DateTimeOffset(now.AddDays(-1)), ct);

                var charge = stats.Should().ContainSingle().Subject;
                charge.Function.Should().Be(_Function);
                charge.Succeeded.Should().Be(2);
                charge.Failed.Should().Be(1);
                charge.Waiting.Should().Be(1);
                charge.LastExecutedAt.Should().Be(latest);
            }
        );
    }

    #region Helpers

    private async Task _RunAsync(
        string nodeId,
        Func<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>, string, CancellationToken, Task> scenario
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
                host.Services.GetRequiredService<INodeMembership>().GetOwnerTag()!,
                ct
            );
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    private static async Task<List<Guid>> _ReadAllPagesAsync(
        IJobPersistenceProvider<TimeJobEntity, CronJobEntity> store,
        JobRunQuery query,
        int pageSize,
        CancellationToken cancellationToken
    )
    {
        var ids = new List<Guid>();
        JobRunCursor? cursor = null;

        do
        {
            var page = await store.ListJobRunsAsync(query, cursor, pageSize, cancellationToken);
            ids.AddRange(page.Runs.Select(x => x.Id));
            // The cursor crosses HTTP as text between pages.
            cursor = page.Next is { } next && JobRunCursor.TryParse(next.ToString(), out var parsed) ? parsed : null;
        } while (cursor is not null);

        return ids;
    }

    private static JobOperationScope _Scope(JobOperationKind kind, DateTime highWater)
    {
        return new JobOperationScope
        {
            Kind = kind,
            Query = new JobRunQuery(),
            HighWaterMark = highWater,
            ConfirmedAt = DateTimeOffset.UtcNow.AddMinutes(1),
        };
    }

    private static async Task _StartAsync(
        IJobPersistenceProvider<TimeJobEntity, CronJobEntity> store,
        DateTime due,
        DateTime started,
        CancellationToken cancellationToken
    )
    {
        var id = await _AddTimeJobAsync(store, JobStatus.Idle, due, cancellationToken);
        var claimed = (await store.AcquireImmediateTimeJobsAsync([id], cancellationToken))
            .Should()
            .ContainSingle()
            .Subject;
        (await store.OpenTimeJobAttemptAsync(id, claimed.OwnerId!, started, cancellationToken)).Should().BeTrue();
    }

    private static async Task<Guid> _AddTimeJobAsync(
        IJobPersistenceProvider<TimeJobEntity, CronJobEntity> store,
        JobStatus status,
        DateTime? executionTime,
        CancellationToken cancellationToken,
        string function = _Function,
        string? fingerprint = null,
        DateTimeOffset? executedAt = null,
        string? correlationId = null
    )
    {
        var job = _TimeJob(status, executionTime, function, fingerprint, executedAt);
        job.CorrelationId = correlationId;
        (await store.AddTimeJobsAsync([job], cancellationToken)).Should().Be(1);

        return job.Id;
    }

    private static TimeJobEntity _TimeJob(
        JobStatus status,
        DateTime? executionTime,
        string function = _Function,
        string? fingerprint = null,
        DateTimeOffset? executedAt = null
    )
    {
        var finished = status is JobStatus.Succeeded or JobStatus.DueDone or JobStatus.Failed;

        return new TimeJobEntity
        {
            Id = Guid.NewGuid(),
            Function = function,
            Status = status,
            ExecutionTime = executionTime,
            ExecutedAt = finished ? executedAt ?? DateTimeOffset.UtcNow.AddMinutes(-30) : null,
            ExceptionMessage = status == JobStatus.Failed ? "boom" : null,
            FailureFingerprint = status == JobStatus.Failed ? fingerprint ?? "v1:default" : null,
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-2),
            UpdatedAt = DateTimeOffset.UtcNow.AddHours(-2),
        };
    }

    private async Task<Guid> _SeedCronAsync(CancellationToken cancellationToken, string function = _CronFunction)
    {
        var id = Guid.NewGuid();
        await fixture.SeedCronJobAsync(
            id,
            function,
            "0 0 * * * *",
            NodeDeathPolicy.Retry,
            cancellationToken,
            reconciledThroughOffsetSeconds: -3600,
            nextDueOffsetSeconds: 3600
        );

        return id;
    }

    private static async Task<Guid> _AddOccurrenceAsync(
        IJobPersistenceProvider<TimeJobEntity, CronJobEntity> store,
        Guid cronJobId,
        JobStatus status,
        DateTime executionTime,
        CancellationToken cancellationToken,
        string? fingerprint = null,
        DateTimeOffset? executedAt = null
    )
    {
        var finished = status is JobStatus.Succeeded or JobStatus.Failed;
        var occurrence = new CronJobOccurrenceEntity<CronJobEntity>
        {
            Id = Guid.NewGuid(),
            CronJobId = cronJobId,
            Status = status,
            ExecutionTime = executionTime,
            ExecutedAt = finished ? executedAt ?? DateTimeOffset.UtcNow.AddMinutes(-30) : null,
            ExceptionMessage = status == JobStatus.Failed ? "boom" : null,
            FailureFingerprint = status == JobStatus.Failed ? fingerprint ?? "v1:default" : null,
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-2),
            UpdatedAt = DateTimeOffset.UtcNow.AddHours(-2),
        };
        (await store.InsertCronJobOccurrencesAsync([occurrence], cancellationToken)).Should().Be(1);

        return occurrence.Id;
    }

    private static TimeJobEntity _KeyedCandidate() =>
        new()
        {
            Id = Guid.NewGuid(),
            Function = "reads-keyed",
            ContractVersion = "1",
            ExecutionTime = DateTime.UtcNow.AddHours(1),
            Request = [1, 2, 3],
        };

    // A keyed row reaches Failed only by executing, which a storage test does not drive; set it directly instead.
    private async Task _MarkTimeJobFailedAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = fixture.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = fixture.Sql(
            $"UPDATE {fixture.QualifiedTimeJobsTable} SET \"Status\" = @status, \"ExceptionMessage\" = 'boom' "
                + "WHERE \"Id\" = @id;"
        );
        JobsCoordinationFixtureExtensions.AddParameter(command, "@status", nameof(JobStatus.Failed));
        JobsCoordinationFixtureExtensions.AddParameter(command, "@id", id);
        (await command.ExecuteNonQueryAsync(cancellationToken)).Should().Be(1);
    }

    // Whole minutes, so an instant survives both providers' timestamp precision.
    private static DateTime _Minute(int minutesFromNow)
    {
        var now = DateTime.UtcNow;

        return new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, DateTimeKind.Utc).AddMinutes(
            minutesFromNow
        );
    }

    private static DateTimeOffset _Ago(DateTime now, int hours) => new(now.AddHours(-hours));

    #endregion
}
