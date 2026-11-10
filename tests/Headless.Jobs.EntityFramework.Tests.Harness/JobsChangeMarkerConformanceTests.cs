// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.Coordination;
using Headless.Jobs;
using Headless.Testing.Tests;
using Headless.UnitOfWork;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests;

/// <summary>
/// The change marker on a relational store: every write an operator sees raises the row's <c>ChangedAt</c>, stamped by
/// the database in the statement that writes the row and never lower than the row's previous marker; and a change read
/// pages through the rows in passes that reach every write, even one committed after a later stamp or one enlisted in a
/// transaction held open past the overlap window.
/// </summary>
public abstract class JobsChangeMarkerConformanceTests<TFixture>(TFixture fixture) : TestBase
    where TFixture : class, IJobsCoordinationFixture
{
    private const string _Function = "change-marker-job";
    private static readonly TimeSpan _WideOverlap = TimeSpan.FromMinutes(5);

    #region Stamp

    public virtual async Task completion_write_stamps_the_database_clock_and_raises_the_marker()
    {
        await _RunAsync(
            "change-completion",
            async (store, owner, ct) =>
            {
                var id = Guid.NewGuid();
                await fixture.SeedTimeJobAsync(
                    id,
                    _Function,
                    (int)JobStatus.InProgress,
                    owner,
                    ct,
                    lockedUntil: DateTime.UtcNow.AddMinutes(10)
                );
                var seeded = await _TimeJobMarkerAsync(id, ct);
                var before = await _ReadStoreClockAsync(ct);

                (await store.UpdateTimeJobAsync(_State(id, owner).SetProperty(x => x.Status, JobStatus.Succeeded), ct))
                    .Should()
                    .Be(1);

                var after = await _ReadStoreClockAsync(ct);
                var stamped = await _TimeJobMarkerAsync(id, ct);
                stamped.Should().BeAfter(seeded);
                stamped.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
            }
        );
    }

    public virtual async Task back_to_back_writes_to_one_row_produce_strictly_increasing_markers()
    {
        await _RunAsync(
            "change-same-tick",
            async (store, owner, ct) =>
            {
                var id = Guid.NewGuid();
                await fixture.SeedTimeJobAsync(
                    id,
                    _Function,
                    (int)JobStatus.InProgress,
                    owner,
                    ct,
                    lockedUntil: DateTime.UtcNow.AddMinutes(10)
                );
                var markers = new List<DateTime> { await _TimeJobMarkerAsync(id, ct) };

                for (var i = 1; i <= 5; i++)
                {
                    (await store.UpdateTimeJobAsync(_State(id, owner).SetProperty(x => x.RetryCount, i), ct))
                        .Should()
                        .Be(1);
                    markers.Add(await _TimeJobMarkerAsync(id, ct));
                }

                markers.Should().BeInAscendingOrder().And.OnlyHaveUniqueItems();
            }
        );
    }

    public virtual async Task write_after_the_clock_steps_back_still_raises_the_marker()
    {
        await _RunAsync(
            "change-clock-back",
            async (store, owner, ct) =>
            {
                var id = Guid.NewGuid();
                await fixture.SeedTimeJobAsync(
                    id,
                    _Function,
                    (int)JobStatus.InProgress,
                    owner,
                    ct,
                    lockedUntil: DateTime.UtcNow.AddMinutes(10)
                );
                // A marker an hour ahead of the store clock stands for a clock that stepped back after the last write.
                await _WriteMarkerAsync(fixture.QualifiedTimeJobsTable, id, DateTime.UtcNow.AddHours(1), ct);
                var previous = await _TimeJobMarkerAsync(id, ct);

                (await store.UpdateTimeJobAsync(_State(id, owner).SetProperty(x => x.RetryCount, 1), ct))
                    .Should()
                    .Be(1);

                var stamped = await _TimeJobMarkerAsync(id, ct);
                stamped.Should().BeAfter(previous);
                (stamped - previous).Should().BeLessThan(TimeSpan.FromMilliseconds(1));
            }
        );
    }

    public virtual async Task time_job_add_and_claims_stamp_the_marker()
    {
        await _RunAsync(
            "change-claim",
            async (store, _, ct) =>
            {
                var before = await _ReadStoreClockAsync(ct);
                var scheduled = _TimeJob(DateTime.UtcNow);
                var immediate = _TimeJob(executionTime: null);
                (await store.AddTimeJobsAsync([scheduled, immediate], ct)).Should().Be(2);
                var added = await _TimeJobMarkerAsync(scheduled.Id, ct);
                added.Should().BeOnOrAfter(before).And.BeOnOrBefore(await _ReadStoreClockAsync(ct));
                var immediateAdded = await _TimeJobMarkerAsync(immediate.Id, ct);

                var candidates = (await store.GetEarliestTimeJobsAsync(ct)).Jobs;
                (await store.QueueTimeJobsAsync(candidates, ct).ToArrayAsync(ct))
                    .Should()
                    .ContainSingle(x => x.Id == scheduled.Id);
                (await _TimeJobMarkerAsync(scheduled.Id, ct)).Should().BeAfter(added, "the scheduled claim");

                (await store.AcquireImmediateTimeJobsAsync([immediate.Id], ct)).Should().ContainSingle();
                (await _TimeJobMarkerAsync(immediate.Id, ct)).Should().BeAfter(immediateAdded, "the immediate claim");
            }
        );
    }

    public virtual async Task cron_occurrence_claims_stamp_the_marker()
    {
        await _RunAsync(
            "change-cron-claim",
            async (store, _, ct) =>
            {
                var cronId = Guid.NewGuid();
                await fixture.SeedCronJobAsync(cronId, _Function, "* * * * *", NodeDeathPolicy.Retry, ct);
                var before = await _ReadStoreClockAsync(ct);

                var created = await store
                    .QueueCronJobOccurrencesAsync(
                        (
                            DateTime.UtcNow.AddMinutes(1),
                            [
                                new JobManagerDispatchContext(cronId)
                                {
                                    FunctionName = _Function,
                                    Expression = "* * * * *",
                                },
                            ]
                        ),
                        ct
                    )
                    .ToArrayAsync(ct);
                var createdMarker = await _OccurrenceMarkerAsync(created.Should().ContainSingle().Subject.Id, ct);
                createdMarker.Should().BeOnOrAfter(before).And.BeOnOrBefore(await _ReadStoreClockAsync(ct));

                var idle = Guid.NewGuid();
                await fixture.SeedCronOccurrenceAsync(
                    idle,
                    cronId,
                    (int)JobStatus.Idle,
                    ownerId: null,
                    NodeDeathPolicy.Retry,
                    lockedUntil: null,
                    DateTime.UtcNow.AddMinutes(-1),
                    ct
                );
                var seeded = await _OccurrenceMarkerAsync(idle, ct);

                (await store.AcquireImmediateCronOccurrencesAsync([idle], ct)).Should().ContainSingle();
                (await _OccurrenceMarkerAsync(idle, ct)).Should().BeAfter(seeded);
            }
        );
    }

    public virtual async Task progress_writes_stamp_the_marker()
    {
        await _RunAsync(
            "change-progress",
            async (store, owner, ct) =>
            {
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
                var (cronId, occurrence) = await _SeedRunningOccurrenceAsync(owner, ct);
                var timeJobSeeded = await _TimeJobMarkerAsync(timeJob, ct);
                var occurrenceSeeded = await _OccurrenceMarkerAsync(occurrence, ct);

                (await store.UpdateTimeJobProgressAsync(JobsLeaseTables.TimeJob(timeJob, 1), new(40, "halfway"), ct))
                    .Should()
                    .BeTrue();
                (
                    await store.UpdateCronJobOccurrenceProgressAsync(
                        JobsLeaseTables.CronOccurrence(occurrence, 1),
                        new(40, "halfway"),
                        ct
                    )
                )
                    .Should()
                    .BeTrue();

                (await _TimeJobMarkerAsync(timeJob, ct)).Should().BeAfter(timeJobSeeded);
                (await _OccurrenceMarkerAsync(occurrence, ct)).Should().BeAfter(occurrenceSeeded);
                cronId.Should().NotBeEmpty();
            }
        );
    }

    public virtual async Task retry_success_and_failure_writes_stamp_the_marker()
    {
        await _RunAsync(
            "change-outcomes",
            async (store, owner, ct) =>
            {
                var retried = Guid.NewGuid();
                var succeeded = Guid.NewGuid();
                await fixture.SeedTimeJobAsync(
                    retried,
                    _Function,
                    (int)JobStatus.InProgress,
                    owner,
                    ct,
                    lockedUntil: DateTime.UtcNow.AddMinutes(10)
                );
                await fixture.SeedTimeJobAsync(
                    succeeded,
                    _Function,
                    (int)JobStatus.InProgress,
                    owner,
                    ct,
                    lockedUntil: DateTime.UtcNow.AddMinutes(10)
                );
                var (_, failed) = await _SeedRunningOccurrenceAsync(owner, ct);
                var retriedSeeded = await _TimeJobMarkerAsync(retried, ct);
                var succeededSeeded = await _TimeJobMarkerAsync(succeeded, ct);
                var failedSeeded = await _OccurrenceMarkerAsync(failed, ct);

                (await store.UpdateTimeJobAsync(_State(retried, owner).SetProperty(x => x.RetryCount, 1), ct))
                    .Should()
                    .Be(1);
                (
                    await store.UpdateTimeJobAsync(
                        _State(succeeded, owner).SetProperty(x => x.Status, JobStatus.Succeeded),
                        ct
                    )
                )
                    .Should()
                    .Be(1);
                var failure = _State(failed, owner)
                    .SetProperty(x => x.Status, JobStatus.Failed)
                    .SetProperty(x => x.ExceptionDetails, "boom");
                (await store.UpdateCronJobOccurrenceAsync(failure, ct)).Should().Be(1);

                (await _TimeJobMarkerAsync(retried, ct)).Should().BeAfter(retriedSeeded, "the retry write");
                (await _TimeJobMarkerAsync(succeeded, ct)).Should().BeAfter(succeededSeeded, "the success write");
                (await _OccurrenceMarkerAsync(failed, ct)).Should().BeAfter(failedSeeded, "the failure write");
            }
        );
    }

    public virtual async Task cancel_and_requeue_stamp_the_marker()
    {
        await _RunAsync(
            "change-cancel-requeue",
            async (store, owner, ct) =>
            {
                var idle = Guid.NewGuid();
                var running = Guid.NewGuid();
                var failed = Guid.NewGuid();
                await fixture.SeedTimeJobAsync(idle, _Function, (int)JobStatus.Idle, ownerId: null, ct);
                await fixture.SeedTimeJobAsync(
                    running,
                    _Function,
                    (int)JobStatus.InProgress,
                    owner,
                    ct,
                    lockedUntil: DateTime.UtcNow.AddMinutes(10)
                );
                await fixture.SeedTimeJobAsync(failed, _Function, (int)JobStatus.Failed, ownerId: null, ct);
                var cronId = Guid.NewGuid();
                await fixture.SeedCronJobAsync(cronId, _Function, "* * * * *", NodeDeathPolicy.Retry, ct);
                var failedOccurrence = Guid.NewGuid();
                await fixture.SeedCronOccurrenceAsync(
                    failedOccurrence,
                    cronId,
                    (int)JobStatus.Failed,
                    ownerId: null,
                    NodeDeathPolicy.Retry,
                    lockedUntil: null,
                    DateTime.UtcNow.AddMinutes(-1),
                    ct
                );
                var seeded = new Dictionary<Guid, DateTime>
                {
                    [idle] = await _TimeJobMarkerAsync(idle, ct),
                    [running] = await _TimeJobMarkerAsync(running, ct),
                    [failed] = await _TimeJobMarkerAsync(failed, ct),
                    [failedOccurrence] = await _OccurrenceMarkerAsync(failedOccurrence, ct),
                };

                (await store.RequestTimeJobCancellationAsync(idle, ct)).Should().BeTrue();
                (await store.RequestTimeJobCancellationAsync(running, ct)).Should().BeTrue();
                (await store.RequeueTimeJobAsync(failed, traceParent: null, ct))
                    .Should()
                    .Be(JobRequeueOutcome.Requeued);
                (await store.RequeueCronJobOccurrenceAsync(failedOccurrence, traceParent: null, ct))
                    .Should()
                    .Be(JobRequeueOutcome.Requeued);

                (await _TimeJobMarkerAsync(idle, ct)).Should().BeAfter(seeded[idle], "cancelling an idle job");
                (await _TimeJobMarkerAsync(running, ct)).Should().BeAfter(seeded[running], "cancelling a running job");
                (await _TimeJobMarkerAsync(failed, ct)).Should().BeAfter(seeded[failed], "requeuing a time job");
                (await _OccurrenceMarkerAsync(failedOccurrence, ct))
                    .Should()
                    .BeAfter(seeded[failedOccurrence], "requeuing an occurrence");
            }
        );
    }

    public virtual async Task deleting_a_child_stamps_the_surviving_parent()
    {
        await _RunAsync(
            "change-delete-child",
            async (store, _, ct) =>
            {
                var parent = _TimeJob(DateTime.UtcNow.AddHours(1));
                var child = _TimeJob(executionTime: null);
                child.ParentId = parent.Id;
                child.RunCondition = RunCondition.OnSuccess;
                (await store.AddTimeJobsAsync([parent, child], ct)).Should().Be(2);
                var seeded = await _TimeJobMarkerAsync(parent.Id, ct);

                (await store.RemoveTimeJobsAsync([child.Id], ct)).Should().Be(1);

                (await _TimeJobMarkerAsync(parent.Id, ct)).Should().BeAfter(seeded);
            }
        );
    }

    public virtual async Task editing_a_time_job_stamps_the_marker()
    {
        await _RunAsync(
            "change-edit",
            async (store, _, ct) =>
            {
                var job = _TimeJob(DateTime.UtcNow.AddHours(1));
                (await store.AddTimeJobsAsync([job], ct)).Should().Be(1);
                var seeded = await _TimeJobMarkerAsync(job.Id, ct);
                var stored = (await store.GetTimeJobByIdAsync(job.Id, ct))!;
                stored.Description = "edited";
                // A value the caller set on the entity never reaches the column: the store owns the marker.
                stored.ChangedAt = DateTime.UtcNow.AddYears(-1);

                (await store.UpdateTimeJobsAsync([stored], ct)).Should().Be(1);

                (await _TimeJobMarkerAsync(job.Id, ct)).Should().BeAfter(seeded);
            }
        );
    }

    public virtual async Task cron_add_pause_and_resume_stamp_the_definition_marker()
    {
        await _RunAsync(
            "change-cron-definition",
            async (store, _, ct) =>
            {
                var before = await _ReadStoreClockAsync(ct);
                var definition = new CronJobEntity
                {
                    Id = Guid.NewGuid(),
                    Function = _Function,
                    Expression = "0 * * * * *",
                    ScheduleRevision = 1,
                };
                (await store.InsertCronJobsAsync([definition], ct)).Should().Be(1);
                var added = await _CronJobMarkerAsync(definition.Id, ct);
                added.Should().BeOnOrAfter(before).And.BeOnOrBefore(await _ReadStoreClockAsync(ct));

                var paused = await store.PauseCronJobAsync(definition.Id, DateTimeOffset.UtcNow, ct);
                paused.Should().NotBeNull();
                var pausedMarker = await _CronJobMarkerAsync(definition.Id, ct);
                pausedMarker.Should().BeAfter(added, "pausing the definition");

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
                    DateTimeOffset.UtcNow,
                    ct
                );
                resumed.Should().NotBeNull();
                (await _CronJobMarkerAsync(definition.Id, ct))
                    .Should()
                    .BeAfter(pausedMarker, "resuming the definition");
            }
        );
    }

    #endregion

    #region Read

    public virtual async Task change_read_returns_rows_in_marker_order_with_the_next_cursor()
    {
        await _RunAsync(
            "change-read-order",
            async (store, _, ct) =>
            {
                var cursor = await store.GetChangeStartCursorAsync(ct);
                var jobs = new[] { _TimeJob(null), _TimeJob(null), _TimeJob(null) };
                foreach (var job in jobs)
                {
                    (await store.AddTimeJobsAsync([job], ct)).Should().Be(1);
                }

                var page = await store.ReadChangesAsync(cursor, pageSize: 10, TimeSpan.Zero, ct);

                page.Changes.Select(x => x.Id).Should().BeEquivalentTo(jobs.Select(x => x.Id));
                page.Changes.Should().OnlyContain(x => x.Kind == JobChangeKind.TimeJob);
                page.Changes.Select(x => x.ChangedAt).Should().BeInAscendingOrder();
                foreach (var change in page.Changes)
                {
                    change.ChangedAt.Should().Be(await _TimeJobMarkerAsync(change.Id, ct));
                    change.Version.Should().Be(change.ChangedAt.Ticks);
                }

                page.Cursor.TimeJobs.Should()
                    .Be(new JobChangePosition(page.Changes[^1].ChangedAt, page.Changes[^1].Id));
                JobChangeCursor.TryParse(page.Cursor.ToString(), out var parsed).Should().BeTrue();
                parsed.Should().Be(page.Cursor);

                var next = await store.ReadChangesAsync(parsed!, pageSize: 10, TimeSpan.Zero, ct);
                next.Changes.Should()
                    .BeEmpty("nothing changed since the last read and no overlap window was asked for");
            }
        );
    }

    public virtual async Task writes_committed_out_of_stamp_order_are_both_read()
    {
        await _RunAsync(
            "change-read-out-of-order",
            async (store, owner, ct) =>
            {
                var early = _TimeJob(null);
                var late = _TimeJob(null);
                await fixture.SeedTimeJobAsync(
                    early.Id,
                    _Function,
                    (int)JobStatus.InProgress,
                    owner,
                    ct,
                    lockedUntil: DateTime.UtcNow.AddMinutes(10)
                );
                await fixture.SeedTimeJobAsync(
                    late.Id,
                    _Function,
                    (int)JobStatus.InProgress,
                    owner,
                    ct,
                    lockedUntil: DateTime.UtcNow.AddMinutes(10)
                );
                var cursor = await store.GetChangeStartCursorAsync(ct);
                var seen = new HashSet<Guid>();

                // The early write stamps its row inside a transaction that commits only after the late write landed.
                await using var connection = fixture.CreateConnection();
                await connection.OpenAsync(ct);
                await using (var transaction = await connection.BeginTransactionAsync(ct))
                {
                    await using (var command = connection.CreateCommand())
                    {
                        command.Transaction = transaction;
                        command.CommandText = fixture.Sql(
                            $"UPDATE {fixture.QualifiedTimeJobsTable} SET \"ChangedAt\" = {fixture.UtcNowSqlExpression} WHERE \"Id\" = @id;"
                        );
                        JobsCoordinationFixtureExtensions.AddParameter(command, "@id", early.Id);
                        (await command.ExecuteNonQueryAsync(ct)).Should().Be(1);
                    }

                    (await store.UpdateTimeJobAsync(_State(late.Id, owner).SetProperty(x => x.RetryCount, 1), ct))
                        .Should()
                        .Be(1);

                    // On PostgreSQL the read returns the late row at once and passes the early stamp; on a store that
                    // locks rows on read it waits for the commit. Either way the early row must arrive afterwards.
                    var pending = store.ReadChangesAsync(cursor, pageSize: 10, _WideOverlap, ct);
                    _ = await Task.WhenAny(pending, Task.Delay(TimeSpan.FromSeconds(1), ct));
                    await transaction.CommitAsync(ct);
                    var first = await pending;
                    seen.UnionWith(first.Changes.Select(x => x.Id));
                    cursor = first.Cursor;
                }

                for (var read = 0; read < 5 && !seen.IsSupersetOf([early.Id, late.Id]); read++)
                {
                    var page = await store.ReadChangesAsync(cursor, pageSize: 10, _WideOverlap, ct);
                    seen.UnionWith(page.Changes.Select(x => x.Id));
                    cursor = page.Cursor;
                }

                seen.Should().Contain([early.Id, late.Id]);
            }
        );
    }

    public virtual async Task a_burst_larger_than_a_page_inside_the_overlap_does_not_hide_a_later_write()
    {
        await _RunAsync(
            "change-read-burst",
            async (store, _, ct) =>
            {
                const int pageSize = 10;
                var cursor = await store.GetChangeStartCursorAsync(ct);
                var burst = Enumerable.Range(0, 25).Select(_ => _TimeJob(null)).ToArray();
                (await store.AddTimeJobsAsync(burst, ct)).Should().Be(burst.Length);
                var later = _TimeJob(null);
                (await store.AddTimeJobsAsync([later], ct)).Should().Be(1);

                var reads = 0;
                var seenLater = false;
                while (!seenLater && reads < 10)
                {
                    var page = await store.ReadChangesAsync(cursor, pageSize, _WideOverlap, ct);
                    page.Changes.Count.Should().BeLessThanOrEqualTo(pageSize);
                    seenLater = page.Changes.Any(x => x.Id == later.Id);
                    cursor = page.Cursor;
                    reads++;
                }

                seenLater
                    .Should()
                    .BeTrue("successive reads page forward through the pass instead of rereading one page");
                reads.Should().BeLessThanOrEqualTo(3);
            }
        );
    }

    public virtual async Task enlisted_time_job_held_open_past_the_window_reaches_another_nodes_change_read()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var writerHost = fixture.BuildCoordinatedEnqueueHost<JobsDbContext>("change-enlisted-writer");
        using var readerHost = fixture.BuildHost("change-enlisted-reader");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(writerHost, ct);
        await writerHost.StartAsync(ct);
        await readerHost.StartAsync(ct);

        using var readerStop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var overlap = TimeSpan.FromMilliseconds(300);
        var reader = _Store(readerHost);
        var seen = new ConcurrentDictionary<Guid, byte>();
        var cursor = await reader.GetChangeStartCursorAsync(ct);
        var readLoop = Task.Run(
            async () =>
            {
                while (!readerStop.IsCancellationRequested)
                {
                    var page = await reader.ReadChangesAsync(cursor, pageSize: 50, overlap, readerStop.Token);
                    foreach (var change in page.Changes)
                    {
                        seen.TryAdd(change.Id, 0);
                    }

                    cursor = page.Cursor;
                    await Task.Delay(TimeSpan.FromMilliseconds(100), readerStop.Token);
                }
            },
            ct
        );

        try
        {
            var enlisted = new TimeJobEntity
            {
                Id = Guid.NewGuid(),
                Function = JobsCoordinationFixtureExtensions.CoordinatedFunctionName,
                Request = [],
                ExecutionTime = DateTime.UtcNow.AddHours(1),
            };
            var autonomous = _TimeJob(null);

            await fixture.RunCoordinatedTransactionAsync(
                writerHost.Services,
                async (_, unitOfWork, _, _, innerCt) =>
                {
                    (await unitOfWork.TimeJobs<TimeJobEntity>().AddAsync(enlisted, innerCt)).Should().NotBeNull();

                    // Hold the transaction well past the overlap window while another write moves the readers on.
                    await Task.Delay(overlap * 3, innerCt);
                    (await _Store(readerHost).AddTimeJobsAsync([autonomous], innerCt)).Should().Be(1);
                    var deadline = DateTime.UtcNow.AddSeconds(5);
                    while (!seen.ContainsKey(autonomous.Id) && DateTime.UtcNow < deadline)
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(50), innerCt);
                    }

                    await Task.Delay(overlap * 3, innerCt);
                },
                ct
            );

            var afterCommit = DateTime.UtcNow.AddSeconds(15);
            while (!seen.ContainsKey(enlisted.Id) && DateTime.UtcNow < afterCommit)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100), ct);
            }

            seen.Should().ContainKey(enlisted.Id);
        }
        finally
        {
            await readerStop.CancelAsync();
            await readLoop.ContinueWith(static _ => { }, TaskScheduler.Default);
            await readerHost.StopAsync(ct);
            await writerHost.StopAsync(ct);
        }
    }

    #endregion

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
            await scenario(_Store(host), _Owner(host), ct);
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    private async Task<(Guid CronId, Guid OccurrenceId)> _SeedRunningOccurrenceAsync(
        string owner,
        CancellationToken cancellationToken
    )
    {
        var cronId = Guid.NewGuid();
        var occurrence = Guid.NewGuid();
        await fixture.SeedCronJobAsync(cronId, _Function, "* * * * *", NodeDeathPolicy.Retry, cancellationToken);
        await fixture.SeedCronOccurrenceAsync(
            occurrence,
            cronId,
            (int)JobStatus.InProgress,
            owner,
            NodeDeathPolicy.Retry,
            DateTime.UtcNow.AddMinutes(5),
            DateTime.UtcNow.AddMinutes(-1),
            cancellationToken
        );

        return (cronId, occurrence);
    }

    private static TimeJobEntity _TimeJob(DateTime? executionTime)
    {
        return new TimeJobEntity
        {
            Id = Guid.NewGuid(),
            Function = _Function,
            ExecutionTime = executionTime,
        };
    }

    private static JobExecutionState _State(Guid id, string owner)
    {
        return new JobExecutionState
        {
            FunctionName = _Function,
            JobId = id,
            ClaimOwner = owner,
            ClaimGeneration = 1,
        };
    }

    private Task<DateTime> _TimeJobMarkerAsync(Guid id, CancellationToken cancellationToken) =>
        _ReadMarkerAsync(fixture.QualifiedTimeJobsTable, id, cancellationToken);

    private Task<DateTime> _CronJobMarkerAsync(Guid id, CancellationToken cancellationToken) =>
        _ReadMarkerAsync(fixture.QualifiedCronJobsTable, id, cancellationToken);

    private Task<DateTime> _OccurrenceMarkerAsync(Guid id, CancellationToken cancellationToken) =>
        _ReadMarkerAsync(fixture.QualifiedCronJobOccurrencesTable, id, cancellationToken);

    private async Task<DateTime> _ReadMarkerAsync(string table, Guid id, CancellationToken cancellationToken)
    {
        await using var connection = fixture.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = fixture.Sql($"SELECT \"ChangedAt\" FROM {table} WHERE \"Id\" = @id;");
        JobsCoordinationFixtureExtensions.AddParameter(command, "@id", id);

        var value = await command.ExecuteScalarAsync(cancellationToken);

        return value is DateTime changedAt
            ? DateTime.SpecifyKind(changedAt, DateTimeKind.Utc)
            : throw new InvalidOperationException($"Row {id} not found in {table}.");
    }

    private async Task _WriteMarkerAsync(string table, Guid id, DateTime value, CancellationToken cancellationToken)
    {
        await using var connection = fixture.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = fixture.Sql($"UPDATE {table} SET \"ChangedAt\" = @value WHERE \"Id\" = @id;");
        JobsCoordinationFixtureExtensions.AddParameter(command, "@id", id);
        JobsCoordinationFixtureExtensions.AddParameter(command, "@value", value);
        (await command.ExecuteNonQueryAsync(cancellationToken)).Should().Be(1);
    }

    private async Task<DateTime> _ReadStoreClockAsync(CancellationToken cancellationToken)
    {
        await using var connection = fixture.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {fixture.UtcNowSqlExpression};";
        var value = (DateTime)(await command.ExecuteScalarAsync(cancellationToken))!;

        return DateTime.SpecifyKind(value, DateTimeKind.Utc);
    }

    private static string _Owner(IHost host) => host.Services.GetRequiredService<INodeMembership>().GetOwnerTag()!;

    private static IJobPersistenceProvider<TimeJobEntity, CronJobEntity> _Store(IHost host) =>
        host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();

    #endregion
}
