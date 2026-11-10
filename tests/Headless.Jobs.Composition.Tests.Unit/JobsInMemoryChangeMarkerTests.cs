// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;
using Headless.Jobs;
using Headless.Jobs.Provider;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>
/// The change marker on the in-memory store: every write an operator sees raises the row's <c>ChangedAt</c> from one
/// counter, never lower than the previous marker however the clock moves, and a change read pages through the rows in
/// write order.
/// </summary>
public sealed class JobsInMemoryChangeMarkerTests : TestBase
{
    private const string _Owner = "node-a@incarnation";
    private static readonly DateTimeOffset _Now = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task every_reported_time_job_write_advances_the_marker()
    {
        var (store, _) = _Create();
        var claimed = _TimeJob();
        var failed = _TimeJob();
        failed.Status = JobStatus.Failed;
        var idle = _TimeJob();
        var parent = _TimeJob();
        var child = _UntimedJob();
        child.ParentId = parent.Id;
        child.RunCondition = RunCondition.OnSuccess;
        await store.AddTimeJobsAsync([claimed, failed, idle, parent, child], AbortToken);
        var markers = await _MarkersAsync(store, JobChangeKind.TimeJob);
        markers.Should().ContainKeys(claimed.Id, failed.Id, idle.Id, parent.Id, child.Id);

        async Task advanced(Guid id, string because)
        {
            var current = await _MarkersAsync(store, JobChangeKind.TimeJob);
            current[id].Should().BeAfter(markers[id], because);
            markers[id] = current[id];
        }

        (await store.AcquireImmediateTimeJobsAsync([claimed.Id], AbortToken)).Should().ContainSingle();
        await advanced(claimed.Id, "the claim");

        (await store.UpdateTimeJobProgressAsync(JobsLeaseTables.TimeJob(claimed.Id, 1), new(40, "halfway"), AbortToken))
            .Should()
            .BeTrue();
        await advanced(claimed.Id, "the progress write");

        (await store.UpdateTimeJobAsync(_State(claimed.Id).SetProperty(x => x.RetryCount, 1), AbortToken))
            .Should()
            .Be(1);
        await advanced(claimed.Id, "the retry write");

        (await store.UpdateTimeJobAsync(_State(claimed.Id).SetProperty(x => x.Status, JobStatus.Succeeded), AbortToken))
            .Should()
            .Be(1);
        await advanced(claimed.Id, "the success write");

        (await store.RequestTimeJobCancellationAsync(idle.Id, AbortToken)).Should().BeTrue();
        await advanced(idle.Id, "the cancel");

        (await store.RequeueTimeJobAsync(failed.Id, traceParent: null, AbortToken))
            .Should()
            .Be(JobRequeueOutcome.Requeued);
        await advanced(failed.Id, "the requeue");

        (await store.RemoveTimeJobsAsync([child.Id], AbortToken)).Should().Be(1);
        await advanced(parent.Id, "deleting a child changes its parent's chain");
    }

    [Fact]
    public async Task cron_add_pause_resume_and_occurrence_writes_advance_the_marker()
    {
        var (store, _) = _Create();
        var definition = new CronJobEntity
        {
            Id = Guid.NewGuid(),
            Function = "cron-marker",
            Expression = "0 * * * * *",
            ScheduleRevision = 1,
        };
        (await store.InsertCronJobsAsync([definition], AbortToken)).Should().Be(1);
        var added = (await _MarkersAsync(store, JobChangeKind.CronJob))[definition.Id];

        var paused = await store.PauseCronJobAsync(definition.Id, _Now, AbortToken);
        paused.Should().NotBeNull();
        var pausedMarker = (await _MarkersAsync(store, JobChangeKind.CronJob))[definition.Id];
        pausedMarker.Should().BeAfter(added, "the pause");

        var resumed = await store.ResumeCronJobAsync(
            definition.Id,
            paused!.ScheduleRevision,
            anchor => new CronJobOccurrenceEntity<CronJobEntity>
            {
                Id = Guid.NewGuid(),
                CronJobId = definition.Id,
                Status = JobStatus.Idle,
                ExecutionTime = anchor.AddMinutes(5),
            },
            _Now,
            AbortToken
        );
        resumed.Should().NotBeNull();
        (await _MarkersAsync(store, JobChangeKind.CronJob))[definition.Id].Should().BeAfter(pausedMarker, "the resume");

        var occurrence = new CronJobOccurrenceEntity<CronJobEntity>
        {
            Id = Guid.NewGuid(),
            CronJobId = definition.Id,
            Status = JobStatus.Idle,
            ExecutionTime = _Now.UtcDateTime.AddMinutes(-1),
        };
        (await store.InsertCronJobOccurrencesAsync([occurrence], AbortToken)).Should().Be(1);
        var inserted = (await _MarkersAsync(store, JobChangeKind.CronJobOccurrence))[occurrence.Id];

        (await store.AcquireImmediateCronOccurrencesAsync([occurrence.Id], AbortToken)).Should().ContainSingle();
        var claimedMarker = (await _MarkersAsync(store, JobChangeKind.CronJobOccurrence))[occurrence.Id];
        claimedMarker.Should().BeAfter(inserted, "the claim");

        var failure = _State(occurrence.Id)
            .SetProperty(x => x.Status, JobStatus.Failed)
            .SetProperty(x => x.ExceptionDetails, "boom");
        (await store.UpdateCronJobOccurrenceAsync(failure, AbortToken)).Should().Be(1);
        (await _MarkersAsync(store, JobChangeKind.CronJobOccurrence))[occurrence.Id]
            .Should()
            .BeAfter(claimedMarker, "the failure");
    }

    [Fact]
    public async Task writes_in_one_clock_tick_and_after_the_clock_steps_back_still_raise_the_marker()
    {
        var (store, clock) = _Create();
        var job = _TimeJob();
        await store.AddTimeJobsAsync([job], AbortToken);
        (await store.AcquireImmediateTimeJobsAsync([job.Id], AbortToken)).Should().ContainSingle();
        var markers = new List<DateTime> { (await _MarkersAsync(store, JobChangeKind.TimeJob))[job.Id] };

        // The clock never moves between these writes.
        for (var i = 1; i <= 3; i++)
        {
            await store.UpdateTimeJobAsync(_State(job.Id).SetProperty(x => x.RetryCount, i), AbortToken);
            markers.Add((await _MarkersAsync(store, JobChangeKind.TimeJob))[job.Id]);
        }

        clock.Now = _Now.AddHours(-1);
        await store.UpdateTimeJobAsync(_State(job.Id).SetProperty(x => x.RetryCount, 4), AbortToken);
        markers.Add((await _MarkersAsync(store, JobChangeKind.TimeJob))[job.Id]);

        markers.Should().BeInAscendingOrder().And.OnlyHaveUniqueItems();
        markers[^1].Should().Be(markers[^2].AddTicks(1));
    }

    [Fact]
    public async Task change_read_returns_rows_in_write_order_with_the_next_cursor()
    {
        var (store, _) = _Create();
        var cursor = await store.GetChangeStartCursorAsync(AbortToken);
        var jobs = new[] { _TimeJob(), _TimeJob(), _TimeJob() };
        foreach (var job in jobs)
        {
            await store.AddTimeJobsAsync([job], AbortToken);
        }

        await store.UpdateTimeJobsAsync([_Edited(await store.GetTimeJobByIdAsync(jobs[0].Id, AbortToken))], AbortToken);

        var page = await store.ReadChangesAsync(cursor, pageSize: 10, TimeSpan.Zero, AbortToken);

        page.Changes.Select(x => x.Id).Should().Equal(jobs[1].Id, jobs[2].Id, jobs[0].Id);
        page.Cursor.TimeJobs.Should().Be(new JobChangePosition(page.Changes[^1].ChangedAt, jobs[0].Id));
        (await store.ReadChangesAsync(page.Cursor, pageSize: 10, TimeSpan.Zero, AbortToken)).Changes.Should().BeEmpty();
    }

    [Fact]
    public async Task a_burst_larger_than_a_page_inside_the_overlap_does_not_hide_a_later_write()
    {
        var (store, _) = _Create();
        var cursor = await store.GetChangeStartCursorAsync(AbortToken);
        var burst = Enumerable.Range(0, 25).Select(_ => _TimeJob()).ToArray();
        await store.AddTimeJobsAsync(burst, AbortToken);
        var later = _TimeJob();
        await store.AddTimeJobsAsync([later], AbortToken);

        var reads = 0;
        var seenLater = false;
        while (!seenLater && reads < 10)
        {
            var page = await store.ReadChangesAsync(cursor, pageSize: 10, TimeSpan.FromHours(1), AbortToken);
            page.Changes.Count.Should().BeLessThanOrEqualTo(10);
            seenLater = page.Changes.Any(x => x.Id == later.Id);
            cursor = page.Cursor;
            reads++;
        }

        seenLater.Should().BeTrue();
        reads.Should().Be(3, "the pass pages 10, 10, then the last 6 rows");
    }

    [Fact]
    public void cursor_round_trips_through_its_string_form()
    {
        var cursor = new JobChangeCursor
        {
            PassAfter = _Now.UtcDateTime.AddMinutes(-1),
            PassThrough = _Now.UtcDateTime,
            TimeJobs = new JobChangePosition(_Now.UtcDateTime.AddSeconds(-5), Guid.NewGuid()),
            CronJobOccurrences = new JobChangePosition(_Now.UtcDateTime.AddSeconds(-2), Guid.NewGuid()),
        };

        JobChangeCursor.TryParse(cursor.ToString(), out var parsed).Should().BeTrue();

        parsed.Should().Be(cursor);
        JobChangeCursor.TryParse("v1:not-a-cursor", out _).Should().BeFalse();
        JobChangeCursor.TryParse(null, out _).Should().BeFalse();
    }

    private static (IJobPersistenceProvider<TimeJobEntity, CronJobEntity> Store, SettableTimeProvider Clock) _Create()
    {
        var clock = new SettableTimeProvider { Now = _Now };
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(clock);
        services.AddHeadlessGuidGenerator();
        services.AddSingleton(new SchedulerOptionsBuilder { NodeId = _Owner });

        return (
            new JobsInMemoryPersistenceProvider<TimeJobEntity, CronJobEntity>(services.BuildServiceProvider()),
            clock
        );
    }

    private static async Task<Dictionary<Guid, DateTime>> _MarkersAsync(
        IJobPersistenceProvider<TimeJobEntity, CronJobEntity> store,
        JobChangeKind kind
    )
    {
        var rows = await store.ReadChangedRowsAsync(
            kind,
            DateTime.MinValue,
            DateTime.MaxValue,
            resumeAfter: null,
            maxCount: 10_000,
            AbortToken
        );

        return rows.ToDictionary(static x => x.Id, static x => x.ChangedAt);
    }

    private static TimeJobEntity _TimeJob()
    {
        var job = _UntimedJob();
        // Due now, so the immediate claim the tests take is allowed: the store only claims a job that is due.
        job.ExecutionTime = _Now.UtcDateTime;

        return job;
    }

    private static TimeJobEntity _UntimedJob()
    {
        return new TimeJobEntity { Id = Guid.NewGuid(), Function = "marker" };
    }

    private static TimeJobEntity _Edited(TimeJobEntity? job)
    {
        job!.Description = "edited";

        return job;
    }

    private static JobExecutionState _State(Guid id)
    {
        return new JobExecutionState
        {
            FunctionName = "marker",
            JobId = id,
            ClaimOwner = _Owner,
            ClaimGeneration = 1,
        };
    }

    private sealed class SettableTimeProvider : TimeProvider
    {
        public DateTimeOffset Now { get; set; }

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
