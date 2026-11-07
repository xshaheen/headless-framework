// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Reflection;
using Headless.Hosting;
using Headless.Jobs;
using Headless.Jobs.Provider;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Tests.Provider;

/// <summary>
/// The in-memory store answers the dashboard's reads: one keyset run list over time jobs and cron occurrences, capped
/// counts, failure groups that apply the requeue rules and the cron exit rule, Operation scopes frozen per command, and
/// the Overview figures. The relational providers run the same scenarios in the dashboard-reads conformance suite.
/// </summary>
public sealed class JobsInMemoryDashboardReadsTests : TestBase
{
    private const string _Owner = "node-a@1";
    private static readonly DateTimeOffset _Now = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTime _Base = _Now.UtcDateTime.AddHours(-1);

    [Fact]
    public async Task should_page_both_kinds_in_execution_time_order_with_no_repeat_or_gap_at_a_page_boundary()
    {
        // given
        var store = _Create();
        var cron = await _AddCronJobAsync(store, "report.build");
        var t0 = await _AddTimeJobAsync(store, JobStatus.Succeeded, _Base);
        var o1 = await _AddOccurrenceAsync(store, cron, JobStatus.Succeeded, _Base.AddMinutes(1));
        var t2 = await _AddTimeJobAsync(store, JobStatus.Succeeded, _Base.AddMinutes(2));
        var o3 = await _AddOccurrenceAsync(store, cron, JobStatus.Succeeded, _Base.AddMinutes(3));
        var t4 = await _AddTimeJobAsync(store, JobStatus.Succeeded, _Base.AddMinutes(4));
        var o4 = await _AddOccurrenceAsync(store, cron, JobStatus.Succeeded, _Base.AddMinutes(4));
        var o5 = await _AddOccurrenceAsync(store, cron, JobStatus.Succeeded, _Base.AddMinutes(5));
        var t8 = await _AddTimeJobAsync(store, JobStatus.Succeeded, _Base.AddMinutes(8));
        var o9 = await _AddOccurrenceAsync(store, cron, JobStatus.Succeeded, _Base.AddMinutes(9));

        // when
        var oldestFirst = await _ReadAllPagesAsync(store, new JobRunQuery(), pageSize: 3);
        var newestFirst = await _ReadAllPagesAsync(store, new JobRunQuery { NewestFirst = true }, pageSize: 4);

        // then
        oldestFirst.Should().Equal(t0, o1, t2, o3, t4, o4, o5, t8, o9);
        newestFirst.Should().Equal(o9, t8, o5, t4, o4, o3, t2, o1, t0);
    }

    [Fact]
    public async Task should_stop_a_count_at_ten_thousand_and_report_it_capped()
    {
        // given
        var store = _Create();
        var jobs = Enumerable
            .Range(0, JobRunCount.DefaultCap + 1)
            .Select(index => _TimeJob(JobStatus.Succeeded, _Base.AddSeconds(index)))
            .ToArray();
        await store.AddTimeJobsAsync(jobs, AbortToken);
        await _AddTimeJobAsync(store, JobStatus.Failed, _Base, function: "other.job");

        // when
        var all = await _Store(store).CountJobRunsAsync(new JobRunQuery(), cancellationToken: AbortToken);
        var other = await _Store(store)
            .CountJobRunsAsync(new JobRunQuery { Functions = ["other.job"] }, cancellationToken: AbortToken);

        // then
        all.Should().Be(new JobRunCount(10_000, Capped: true));
        other.Should().Be(new JobRunCount(1, Capped: false));
    }

    [Fact]
    public async Task should_drop_a_failed_occurrence_once_a_later_one_succeeds_and_keep_a_failed_time_job_until_requeued()
    {
        // given
        var store = _Create();
        var cron = await _AddCronJobAsync(store, "report.build");
        await _AddOccurrenceAsync(store, cron, JobStatus.Succeeded, _Base.AddMinutes(-10));
        await _AddOccurrenceAsync(store, cron, JobStatus.Failed, _Base, fingerprint: "v1:occurrence");
        var job = await _AddTimeJobAsync(store, JobStatus.Failed, _Base, fingerprint: "v1:time");

        var before = await _GroupsAsync(store);

        // when
        await _AddOccurrenceAsync(store, cron, JobStatus.Succeeded, _Base.AddMinutes(10));
        var afterLaterSuccess = await _GroupsAsync(store);
        (await store.RequeueTimeJobAsync(job, traceParent: null, AbortToken)).Should().Be(JobRequeueOutcome.Requeued);
        var afterRequeue = await _GroupsAsync(store);

        // then
        before
            .Select(x => (x.Function, x.FailureFingerprint, x.Count))
            .Should()
            .BeEquivalentTo([("report.build", "v1:occurrence", 1), ("billing.charge", "v1:time", 1)]);
        afterLaterSuccess
            .Select(x => (x.Function, x.FailureFingerprint, x.Count))
            .Should()
            .Equal(("billing.charge", "v1:time", 1));
        afterRequeue.Should().BeEmpty();
    }

    [Fact]
    public async Task should_report_a_group_s_count_first_and_last_failure_and_recent_count()
    {
        // given
        var store = _Create();
        await _AddTimeJobAsync(store, JobStatus.Failed, _Base, fingerprint: "v1:a", executedAt: _Now.AddHours(-3));
        await _AddTimeJobAsync(store, JobStatus.Failed, _Base, fingerprint: "v1:a", executedAt: _Now.AddMinutes(-30));

        // when
        var group = (await _GroupsAsync(store)).Should().ContainSingle().Subject;

        // then
        group.Count.Should().Be(2);
        group.FirstFailedAt.Should().Be(_Now.AddHours(-3));
        group.LastFailedAt.Should().Be(_Now.AddMinutes(-30));
        group.RecentCount.Should().Be(1, "only one failed within the last hour");
    }

    [Fact]
    public async Task should_not_count_a_superseded_keyed_job_or_a_chain_member_as_actionable()
    {
        // given
        var store = _Create();
        var actionable = await _AddTimeJobAsync(store, JobStatus.Failed, _Base, fingerprint: "v1:x");
        var child = _TimeJob(JobStatus.Failed, executionTime: null, fingerprint: "v1:x");
        var parent = _TimeJob(JobStatus.Failed, _Base, fingerprint: "v1:x");
        parent.Children = [child];
        await store.AddTimeJobsAsync([parent], AbortToken);
        await _FailedKeyedAsync(store, "current", isCurrent: true);
        var superseded = await _FailedKeyedAsync(store, "superseded", isCurrent: false);

        // when
        var count = await _Store(store)
            .CountJobRunsAsync(new JobRunQuery { ActionableFailuresOnly = true }, cancellationToken: AbortToken);
        var listed = await _ReadAllPagesAsync(store, new JobRunQuery { ActionableFailuresOnly = true }, 10);

        // then
        count.Should().Be(new JobRunCount(2, Capped: false));
        listed.Should().Contain(actionable).And.NotContain([parent.Id, child.Id, superseded]);
        (await store.RequeueTimeJobAsync(superseded, traceParent: null, AbortToken))
            .Should()
            .Be(JobRequeueOutcome.SupersededGeneration, "the count leaves out exactly what a requeue refuses");
    }

    [Fact]
    public async Task should_leave_a_run_changed_after_the_high_water_mark_out_of_a_requeue_scope()
    {
        // given
        var store = _Create();
        var cron = await _AddCronJobAsync(store, "report.build");
        var unchanged = await _AddTimeJobAsync(store, JobStatus.Failed, _Base);
        var refailed = await _AddTimeJobAsync(store, JobStatus.Failed, _Base);
        var occurrence = await _AddOccurrenceAsync(store, cron, JobStatus.Failed, _Base);
        var highWater = (await store.GetLatestChangeMarkerAsync(AbortToken))!.Value;
        var staleMarker = (await _Store(store).GetJobRunAsync(refailed, AbortToken))!.ChangedAt;

        // A chunk requeued the run and it failed again after the Operation was confirmed.
        (await store.RequeueTimeJobAsync(refailed, traceParent: null, AbortToken))
            .Should()
            .Be(JobRequeueOutcome.Requeued);
        var claimed = (await store.AcquireImmediateTimeJobsAsync([refailed], AbortToken))
            .Should()
            .ContainSingle()
            .Subject;
        var fail = new JobExecutionState
        {
            FunctionName = "billing.charge",
            JobId = refailed,
            ClaimOwner = claimed.OwnerId!,
        }.SetProperty(x => x.Status, JobStatus.Failed);
        (await store.UpdateTimeJobAsync(fail, AbortToken)).Should().Be(1);

        var scope = _Scope(JobOperationKind.Requeue, highWater);

        // when
        var chunk = await _Store(store).ReadJobOperationScopeAsync(scope, after: null, chunkSize: 10, AbortToken);
        var reach = await _Store(store).CountJobOperationScopeAsync(scope, cancellationToken: AbortToken);

        // then
        chunk.Targets.Select(x => x.Id).Should().Equal(unchanged, occurrence);
        reach.Should().Be(new JobRunCount(2, Capped: false));
        var occurrenceMarker = chunk.Targets[1].ChangedAt;
        (
            await store.RequeueCronJobOccurrenceIfUnchangedAsync(
                occurrence,
                occurrenceMarker.AddTicks(-1),
                null,
                AbortToken
            )
        )
            .Should()
            .Be(JobRequeueOutcome.Conflict);
        (await store.RequeueCronJobOccurrenceIfUnchangedAsync(occurrence, occurrenceMarker, null, AbortToken))
            .Should()
            .Be(JobRequeueOutcome.Requeued);
        (await store.RequeueTimeJobIfUnchangedAsync(refailed, staleMarker, traceParent: null, AbortToken))
            .Should()
            .Be(JobRequeueOutcome.Conflict);
        (await store.RequeueTimeJobIfUnchangedAsync(unchanged, chunk.Targets[0].ChangedAt, null, AbortToken))
            .Should()
            .Be(JobRequeueOutcome.Requeued);
        (await store.RequeueTimeJobIfUnchangedAsync(unchanged, chunk.Targets[0].ChangedAt, null, AbortToken))
            .Should()
            .Be(JobRequeueOutcome.NotFailed, "a resent chunk changes nothing");
    }

    [Fact]
    public async Task should_put_cancelled_and_skipped_runs_in_a_requeue_scope_but_keep_them_out_of_failure_groups()
    {
        // given
        var store = _Create();
        var cron = await _AddCronJobAsync(store, "report.build");
        var failed = await _AddTimeJobAsync(store, JobStatus.Failed, _Base);
        var cancelled = await _AddTimeJobAsync(store, JobStatus.Cancelled, _Base.AddMinutes(1));
        var skipped = await _AddOccurrenceAsync(store, cron, JobStatus.Skipped, _Base.AddMinutes(2));
        await _AddTimeJobAsync(store, JobStatus.Succeeded, _Base.AddMinutes(3));
        var highWater = (await store.GetLatestChangeMarkerAsync(AbortToken))!.Value;

        // when
        var chunk = await _Store(store)
            .ReadJobOperationScopeAsync(_Scope(JobOperationKind.Requeue, highWater), after: null, 10, AbortToken);
        var groups = await _GroupsAsync(store);

        // then
        chunk.Targets.Select(x => x.Id).Should().BeEquivalentTo([failed, cancelled, skipped]);
        groups.Should().ContainSingle().Which.Count.Should().Be(1, "only a failure asks for an operator");
        var skippedMarker = chunk.Targets.Single(x => x.Id == skipped).ChangedAt;
        (await store.RequeueCronJobOccurrenceIfUnchangedAsync(skipped, skippedMarker, null, AbortToken))
            .Should()
            .Be(JobRequeueOutcome.Requeued);
    }

    [Fact]
    public async Task should_keep_a_running_run_that_reported_progress_after_confirm_in_a_cancel_scope()
    {
        // given
        var store = _Create();
        var running = await _AddTimeJobAsync(store, JobStatus.Idle, _Base);
        var claimed = (await store.AcquireImmediateTimeJobsAsync([running], AbortToken))
            .Should()
            .ContainSingle()
            .Subject;
        await _AddTimeJobAsync(store, JobStatus.Idle, _Base, createdAt: _Now.AddMinutes(1));
        var highWater = (await store.GetLatestChangeMarkerAsync(AbortToken))!.Value;
        var scope = _Scope(JobOperationKind.Cancel, highWater);

        // when
        (await store.UpdateTimeJobProgressAsync(running, claimed.OwnerId!, new JobProgress(40), AbortToken))
            .Should()
            .BeTrue();
        var chunk = await _Store(store).ReadJobOperationScopeAsync(scope, after: null, chunkSize: 10, AbortToken);
        var deletable = await _Store(store)
            .ReadJobOperationScopeAsync(_Scope(JobOperationKind.Delete, highWater), null, 10, AbortToken);

        // then
        chunk.Targets.Select(x => x.Id).Should().Equal(running);
        chunk.Targets[0].ChangedAt.Should().BeAfter(highWater, "the progress write advanced the marker");
        deletable.Targets.Select(x => x.Id).Should().NotContain(running, "a running run is never deleted");
    }

    [Fact]
    public async Task should_delete_a_run_only_while_its_marker_is_unchanged()
    {
        // given
        var store = _Create();
        var cron = await _AddCronJobAsync(store, "report.build");
        var job = await _AddTimeJobAsync(store, JobStatus.Failed, _Base);
        var occurrence = await _AddOccurrenceAsync(store, cron, JobStatus.Failed, _Base);
        var highWater = (await store.GetLatestChangeMarkerAsync(AbortToken))!.Value;
        var chunk = await _Store(store)
            .ReadJobOperationScopeAsync(_Scope(JobOperationKind.Delete, highWater), null, 10, AbortToken);
        var jobMarker = chunk.Targets.Single(x => x.Id == job).ChangedAt;
        var occurrenceMarker = chunk.Targets.Single(x => x.Id == occurrence).ChangedAt;

        // when
        (await store.RequeueTimeJobAsync(job, traceParent: null, AbortToken))
            .Should()
            .Be(JobRequeueOutcome.Requeued);
        var staleDelete = await store.RemoveTimeJobIfUnchangedAsync(job, jobMarker, AbortToken);
        var delete = await store.RemoveCronJobOccurrenceIfUnchangedAsync(occurrence, occurrenceMarker, AbortToken);

        // then
        staleDelete.Should().BeFalse();
        (await store.GetTimeJobByIdAsync(job, AbortToken)).Should().NotBeNull();
        delete.Should().BeTrue();
        (await store.GetCronJobOccurrenceByIdAsync(occurrence, AbortToken)).Should().BeNull();
    }

    [Fact]
    public async Task should_count_the_overview_figures_for_the_last_24_hours_and_the_24_hours_before()
    {
        // given
        var store = _Create();
        var cron = await _AddCronJobAsync(store, "report.build");
        await _AddTimeJobAsync(store, JobStatus.Succeeded, _Base, executedAt: _Now.AddHours(-1));
        await _AddTimeJobAsync(store, JobStatus.DueDone, _Base, executedAt: _Now.AddHours(-2));
        await _AddTimeJobAsync(store, JobStatus.Failed, _Base, executedAt: _Now.AddHours(-3));
        await _AddOccurrenceAsync(store, cron, JobStatus.Succeeded, _Base, executedAt: _Now.AddHours(-4));
        await _AddTimeJobAsync(store, JobStatus.Succeeded, _Base, executedAt: _Now.AddHours(-30));
        await _AddTimeJobAsync(store, JobStatus.Failed, _Base, executedAt: _Now.AddHours(-26));
        await _AddOccurrenceAsync(store, cron, JobStatus.Failed, _Base.AddHours(-30), executedAt: _Now.AddHours(-28));
        await _StartAsync(store, due: _Now.AddMinutes(-10), started: _Now.AddMinutes(-5));
        await _StartAsync(store, due: _Now.AddMinutes(-10), started: _Now.AddMinutes(-10).AddSeconds(30));
        await _StartAsync(store, due: _Now.AddHours(-31), started: _Now.AddHours(-30));

        // when
        var figures = await _Store(store)
            .GetJobOverviewFiguresAsync(_Now.UtcDateTime, TimeSpan.FromMinutes(1), AbortToken);

        // then
        figures.Current.Should().Be(new JobRunWindowFigures(Succeeded: 3, Failed: 1, Late: 1));
        figures.Previous.Should().Be(new JobRunWindowFigures(Succeeded: 1, Failed: 2, Late: 1));
        figures.Current.SucceededShare.Should().Be(0.75);
        figures.Previous.SucceededShare.Should().BeApproximately(1d / 3, 1e-9);
    }

    [Fact]
    public async Task should_list_every_run_of_one_correlation_id_and_resolve_a_run_by_id()
    {
        // given
        var store = _Create();
        var cron = await _AddCronJobAsync(store, "report.build");
        var first = await _AddTimeJobAsync(store, JobStatus.Succeeded, _Base, correlationId: "order-7");
        var second = await _AddTimeJobAsync(store, JobStatus.Failed, _Base.AddMinutes(1), correlationId: "order-7");
        await _AddTimeJobAsync(store, JobStatus.Succeeded, _Base, correlationId: "order-8");
        var occurrence = await _AddOccurrenceAsync(store, cron, JobStatus.Succeeded, _Base);

        // when
        var traced = await _ReadAllPagesAsync(store, new JobRunQuery { CorrelationId = "order-7" }, 10);
        var resolved = await _Store(store).GetJobRunAsync(occurrence, AbortToken);

        // then
        traced.Should().Equal(first, second);
        resolved!.Kind.Should().Be(JobRunKind.CronJobOccurrence);
        resolved.CronJobId.Should().Be(cron.Id);
        (await _Store(store).GetJobRunAsync(Guid.NewGuid(), AbortToken)).Should().BeNull();
    }

    [Fact]
    public async Task should_report_per_function_stats_across_both_kinds()
    {
        // given
        var store = _Create();
        var cron = await _AddCronJobAsync(store, "billing.charge");
        await _AddTimeJobAsync(store, JobStatus.Succeeded, _Base, executedAt: _Now.AddHours(-1));
        await _AddTimeJobAsync(store, JobStatus.Failed, _Base, executedAt: _Now.AddHours(-2));
        await _AddTimeJobAsync(store, JobStatus.Idle, _Base.AddHours(2));
        await _AddOccurrenceAsync(store, cron, JobStatus.Succeeded, _Base, executedAt: _Now.AddMinutes(-5));
        await _AddTimeJobAsync(store, JobStatus.Succeeded, _Base, executedAt: _Now.AddDays(-3));

        // when
        var stats = await _Store(store).GetJobFunctionStatsAsync(_Now.AddDays(-1), AbortToken);

        // then
        var charge = stats.Should().ContainSingle().Subject;
        charge.Function.Should().Be("billing.charge");
        charge.Succeeded.Should().Be(2);
        charge.Failed.Should().Be(1);
        charge.Waiting.Should().Be(1);
        charge.LastExecutedAt.Should().Be(_Now.AddMinutes(-5));
    }

    private static JobOperationScope _Scope(JobOperationKind kind, DateTime highWater)
    {
        return new JobOperationScope
        {
            Kind = kind,
            Query = new JobRunQuery(),
            HighWaterMark = highWater,
            ConfirmedAt = _Now,
        };
    }

    private async Task<List<Guid>> _ReadAllPagesAsync(
        JobsInMemoryPersistenceProvider<TimeJobEntity, CronJobEntity> store,
        JobRunQuery query,
        int pageSize
    )
    {
        var ids = new List<Guid>();
        JobRunCursor? cursor = null;

        do
        {
            var page = await _Store(store).ListJobRunsAsync(query, cursor, pageSize, AbortToken);
            ids.AddRange(page.Runs.Select(x => x.Id));
            // The cursor crosses HTTP as text between pages.
            cursor = page.Next is { } next && JobRunCursor.TryParse(next.ToString(), out var parsed) ? parsed : null;
        } while (cursor is not null);

        return ids;
    }

    private async Task<IReadOnlyList<JobFailureGroup>> _GroupsAsync(
        JobsInMemoryPersistenceProvider<TimeJobEntity, CronJobEntity> store
    )
    {
        return await _Store(store).GetJobFailureGroupsAsync(_Now.AddHours(-1), cancellationToken: AbortToken);
    }

    private async Task _StartAsync(
        JobsInMemoryPersistenceProvider<TimeJobEntity, CronJobEntity> store,
        DateTimeOffset due,
        DateTimeOffset started
    )
    {
        var id = await _AddTimeJobAsync(store, JobStatus.Idle, due.UtcDateTime);
        var claimed = (await store.AcquireImmediateTimeJobsAsync([id], AbortToken)).Should().ContainSingle().Subject;
        (await store.OpenTimeJobAttemptAsync(id, claimed.OwnerId!, started.UtcDateTime, AbortToken)).Should().BeTrue();
    }

    private async Task<Guid> _AddTimeJobAsync(
        JobsInMemoryPersistenceProvider<TimeJobEntity, CronJobEntity> store,
        JobStatus status,
        DateTime? executionTime,
        string function = "billing.charge",
        string? fingerprint = null,
        DateTimeOffset? executedAt = null,
        DateTimeOffset? createdAt = null,
        string? correlationId = null
    )
    {
        var job = _TimeJob(status, executionTime, function, fingerprint, executedAt, createdAt);
        job.CorrelationId = correlationId;
        await store.AddTimeJobsAsync([job], AbortToken);

        return job.Id;
    }

    private static TimeJobEntity _TimeJob(
        JobStatus status,
        DateTime? executionTime,
        string function = "billing.charge",
        string? fingerprint = null,
        DateTimeOffset? executedAt = null,
        DateTimeOffset? createdAt = null
    )
    {
        var finished = status is JobStatus.Succeeded or JobStatus.DueDone or JobStatus.Failed;

        return new TimeJobEntity
        {
            Id = Guid.NewGuid(),
            Function = function,
            Status = status,
            ExecutionTime = executionTime,
            ExecutedAt = finished ? executedAt ?? _Now.AddMinutes(-30) : null,
            ExceptionMessage = status == JobStatus.Failed ? "boom" : null,
            FailureFingerprint = status == JobStatus.Failed ? fingerprint ?? "v1:default" : null,
            CreatedAt = createdAt ?? _Now.AddHours(-2),
            UpdatedAt = _Now.AddHours(-2),
        };
    }

    private async Task<CronJobEntity> _AddCronJobAsync(
        JobsInMemoryPersistenceProvider<TimeJobEntity, CronJobEntity> store,
        string function
    )
    {
        var cron = new CronJobEntity
        {
            Id = Guid.NewGuid(),
            Function = function,
            Expression = "0 0 * * * *",
            ReconciledThroughUtc = _Base,
            NextDueUtc = _Base.AddHours(2),
            CreatedAt = _Now.AddDays(-1),
            UpdatedAt = _Now.AddDays(-1),
        };
        await store.InsertCronJobsAsync([cron], AbortToken);

        return cron;
    }

    private async Task<Guid> _AddOccurrenceAsync(
        JobsInMemoryPersistenceProvider<TimeJobEntity, CronJobEntity> store,
        CronJobEntity cron,
        JobStatus status,
        DateTime executionTime,
        string? fingerprint = null,
        DateTimeOffset? executedAt = null
    )
    {
        var finished = status is JobStatus.Succeeded or JobStatus.Failed;
        var occurrence = new CronJobOccurrenceEntity<CronJobEntity>
        {
            Id = Guid.NewGuid(),
            CronJobId = cron.Id,
            CronJob = cron,
            Function = cron.Function,
            Status = status,
            ExecutionTime = executionTime,
            ExecutedAt = finished ? executedAt ?? _Now.AddMinutes(-30) : null,
            ExceptionMessage = status == JobStatus.Failed ? "boom" : null,
            FailureFingerprint = status == JobStatus.Failed ? fingerprint ?? "v1:default" : null,
            CreatedAt = _Now.AddHours(-2),
            UpdatedAt = _Now.AddHours(-2),
        };
        await store.InsertCronJobOccurrencesAsync([occurrence], AbortToken);

        return occurrence.Id;
    }

    // A keyed row reaches Failed only by executing, which this store test does not drive; the stored row is edited in
    // place instead, as the requeue tests do.
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
        failed.FailureFingerprint = "v1:x";
        failed.IsCurrentGeneration = isCurrent;
        rows[id] = failed;

        return id;
    }

    private static IJobPersistenceProvider<TimeJobEntity, CronJobEntity> _Store(
        JobsInMemoryPersistenceProvider<TimeJobEntity, CronJobEntity> store
    ) => store;

    private static JobsInMemoryPersistenceProvider<TimeJobEntity, CronJobEntity> _Create()
    {
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(new FakeTimeProvider(_Now));
        services.AddHeadlessGuidGenerator();
        services.AddSingleton(new SchedulerOptionsBuilder { NodeId = _Owner, LeaseDuration = TimeSpan.FromMinutes(5) });

        return new JobsInMemoryPersistenceProvider<TimeJobEntity, CronJobEntity>(services.BuildServiceProvider());
    }
}
