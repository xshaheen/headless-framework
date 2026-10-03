// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Models;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

/// <summary>
/// Cross-provider conformance for requeuing a failed time job or cron occurrence: the transition is one conditional
/// write from <c>Failed</c>, refusals leave the row unchanged, a time job is re-stamped to the database clock so the
/// main peek claims it, and an occurrence keeps its instant and is checked for overlap under the definition row lock.
/// </summary>
public abstract class JobsRequeueConformanceTests<TFixture>(TFixture fixture) : TestBase
    where TFixture : class, IJobsCoordinationFixture
{
    private static readonly TimeSpan _ClockTolerance = TimeSpan.FromMinutes(1);

    /// <summary>A failed standalone time job returns to Idle due now and the main peek claims it.</summary>
    public virtual async Task failed_time_job_returns_to_idle_due_now_and_is_claimed_by_the_main_peek()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost("requeue-time");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        await host.StartAsync(ct);

        try
        {
            var persistence = _Persistence(host);
            var job = _FailedJob();
            await persistence.AddTimeJobsAsync([job], ct);
            (await persistence.GetEarliestTimeJobsAsync(ct)).Jobs.Should().BeEmpty("a failed row is not claimable");

            (await persistence.RequeueTimeJobAsync(job.Id, ct)).Should().Be(JobRequeueOutcome.Requeued);

            var requeued = await persistence.GetTimeJobByIdAsync(job.Id, ct);
            requeued!.Status.Should().Be(JobStatus.Idle);
            requeued.RetryCount.Should().Be(0);
            requeued.ExceptionMessage.Should().BeNull();
            requeued.OwnerId.Should().BeNull();
            requeued.LockedUntil.Should().BeNull();
            requeued.ExecutedAt.Should().BeNull();
            requeued.ElapsedTime.Should().Be(0);
            requeued.ExecutionTime.Should().BeCloseTo(DateTime.UtcNow, _ClockTolerance);
            requeued.Retries.Should().Be(3, "the stored retry budget is kept");
            requeued.RetryIntervals.Should().Equal(5, 10, 20);

            var peek = await persistence.GetEarliestTimeJobsAsync(ct);
            peek.Jobs.Select(x => x.Id).Should().Equal(job.Id);
            var claimed = await persistence.QueueTimeJobsAsync(peek.Jobs, ct).ToArrayAsync(ct);
            claimed.Select(x => x.Id).Should().Equal(job.Id);
        }
        finally
        {
            await host.StopAsync(CancellationToken.None);
        }
    }

    /// <summary>The due time comes from the database clock, so a node whose clock runs ahead does not delay the run.</summary>
    public virtual async Task time_job_requeue_stamps_the_database_clock_not_the_node_clock()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        var skewed = new FakeTimeProvider(DateTimeOffset.UtcNow.AddHours(2));
        using var host = fixture.BuildHost("requeue-skew", timeProvider: skewed);
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        var persistence = _Persistence(host);
        var job = _FailedJob();
        await persistence.AddTimeJobsAsync([job], ct);

        (await persistence.RequeueTimeJobAsync(job.Id, ct)).Should().Be(JobRequeueOutcome.Requeued);

        (await persistence.GetTimeJobByIdAsync(job.Id, ct))!
            .ExecutionTime.Should()
            .BeCloseTo(DateTime.UtcNow, _ClockTolerance, "the store's clock, not the skewed node clock");
    }

    /// <summary>A row that is not Failed is refused and left unchanged; an unknown id is not found.</summary>
    public virtual async Task time_job_that_is_not_failed_is_refused_and_unchanged()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost("requeue-not-failed");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        var persistence = _Persistence(host);

        foreach (var status in new[] { JobStatus.Succeeded, JobStatus.InProgress, JobStatus.Idle })
        {
            var job = _FailedJob();
            job.Status = status;
            await persistence.AddTimeJobsAsync([job], ct);
            var before = await persistence.GetTimeJobByIdAsync(job.Id, ct);

            (await persistence.RequeueTimeJobAsync(job.Id, ct)).Should().Be(JobRequeueOutcome.NotFailed);

            var after = await persistence.GetTimeJobByIdAsync(job.Id, ct);
            after!.Status.Should().Be(status);
            after.UpdatedAt.Should().Be(before!.UpdatedAt);
            after.ExecutionTime.Should().Be(before.ExecutionTime);
        }

        (await persistence.RequeueTimeJobAsync(Guid.NewGuid(), ct)).Should().Be(JobRequeueOutcome.NotFound);
    }

    /// <summary>A chain parent and a chain child are refused.</summary>
    public virtual async Task chain_parent_and_chain_child_are_refused()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost("requeue-chain");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        var persistence = _Persistence(host);
        var child = _FailedJob();
        child.ExecutionTime = null;
        var parent = _FailedJob();
        parent.Children = [child];
        await persistence.AddTimeJobsAsync([parent], ct);

        (await persistence.RequeueTimeJobAsync(parent.Id, ct)).Should().Be(JobRequeueOutcome.ChainMember);
        (await persistence.RequeueTimeJobAsync(child.Id, ct)).Should().Be(JobRequeueOutcome.ChainMember);

        (await persistence.GetTimeJobByIdAsync(parent.Id, ct))!.Status.Should().Be(JobStatus.Failed);
        (await persistence.GetTimeJobByIdAsync(child.Id, ct))!.Status.Should().Be(JobStatus.Failed);
    }

    /// <summary>The current keyed generation is requeued; a superseded one is refused.</summary>
    public virtual async Task current_keyed_generation_is_requeued_and_a_superseded_one_is_refused()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost("requeue-keyed");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        var persistence = _Persistence(host);
        var key = new JobKey("requeue-key");

        var first = await persistence.ScheduleKeyedTimeJobAsync(key, _KeyedCandidate(), cancellationToken: ct);
        var second = await persistence.ScheduleKeyedTimeJobAsync(key, _KeyedCandidate(), 1, ct);
        second.Disposition.Should().Be(JobScheduleDisposition.Replaced);
        await _MarkTimeJobFailedAsync(first.RunId!.Value, ct);
        await _MarkTimeJobFailedAsync(second.RunId!.Value, ct);

        (await persistence.RequeueTimeJobAsync(first.RunId.Value, ct))
            .Should()
            .Be(JobRequeueOutcome.SupersededGeneration);
        (await persistence.GetTimeJobByIdAsync(first.RunId.Value, ct))!.Status.Should().Be(JobStatus.Failed);

        (await persistence.RequeueTimeJobAsync(second.RunId.Value, ct)).Should().Be(JobRequeueOutcome.Requeued);
        var requeued = await persistence.GetTimeJobByIdAsync(second.RunId.Value, ct);
        requeued!.Status.Should().Be(JobStatus.Idle);
        requeued.IsCurrentGeneration.Should().BeTrue();
    }

    /// <summary>Concurrent requeues of one time job move it exactly once.</summary>
    public virtual async Task concurrent_time_job_requeues_move_the_row_once()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost("requeue-race");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        var persistence = _Persistence(host);
        var job = _FailedJob();
        await persistence.AddTimeJobsAsync([job], ct);

        var outcomes = await Task.WhenAll(
            Enumerable.Range(0, 8).Select(_ => Task.Run(() => persistence.RequeueTimeJobAsync(job.Id, ct), ct))
        );

        outcomes.Count(x => x == JobRequeueOutcome.Requeued).Should().Be(1);
        outcomes
            .Where(x => x != JobRequeueOutcome.Requeued)
            .Should()
            .OnlyContain(x => x == JobRequeueOutcome.NotFailed || x == JobRequeueOutcome.Conflict);
    }

    /// <summary>A failed occurrence returns to Idle, keeps its instant, and the fallback claims it.</summary>
    public virtual async Task failed_occurrence_returns_to_idle_keeps_its_instant_and_is_claimed_by_the_fallback()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost("requeue-occurrence");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        await host.StartAsync(ct);

        try
        {
            var persistence = _Persistence(host);
            var cronId = await _SeedDefinitionAsync("requeue-occurrence", CronOverlapPolicy.Allow, ct);
            var instant = _Instant(-2);
            var failedId = await _SeedOccurrenceAsync(cronId, JobStatus.Failed, instant, ct);
            (await persistence.QueueTimedOutCronJobOccurrencesAsync(ct).ToArrayAsync(ct))
                .Should()
                .BeEmpty("a failed occurrence is not claimable");

            (await persistence.RequeueCronJobOccurrenceAsync(failedId, ct)).Should().Be(JobRequeueOutcome.Requeued);

            var requeued = (await persistence.GetAllCronJobOccurrencesAsync(x => x.Id == failedId, ct)).Single();
            requeued.Status.Should().Be(JobStatus.Idle);
            requeued.RetryCount.Should().Be(0);
            requeued.ExceptionMessage.Should().BeNull();
            requeued.OwnerId.Should().BeNull();
            requeued.LockedUntil.Should().BeNull();
            requeued.ExecutedAt.Should().BeNull();
            requeued.ExecutionTime.Should().BeCloseTo(instant, TimeSpan.FromMilliseconds(1), "the instant is kept");

            var claimed = await persistence.QueueTimedOutCronJobOccurrencesAsync(ct).ToArrayAsync(ct);
            claimed.Select(x => x.Id).Should().Equal(failedId);
        }
        finally
        {
            await host.StopAsync(CancellationToken.None);
        }
    }

    /// <summary>An occurrence that is not Failed is refused; an unknown id is not found.</summary>
    public virtual async Task occurrence_that_is_not_failed_is_refused()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost("requeue-occurrence-not-failed");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        var persistence = _Persistence(host);
        var cronId = await _SeedDefinitionAsync("requeue-occurrence-not-failed", CronOverlapPolicy.Allow, ct);
        var succeededId = await _SeedOccurrenceAsync(cronId, JobStatus.Succeeded, _Instant(-3), ct);
        var runningId = await _SeedOccurrenceAsync(cronId, JobStatus.InProgress, _Instant(-2), ct);

        (await persistence.RequeueCronJobOccurrenceAsync(succeededId, ct)).Should().Be(JobRequeueOutcome.NotFailed);
        (await persistence.RequeueCronJobOccurrenceAsync(runningId, ct)).Should().Be(JobRequeueOutcome.NotFailed);
        (await persistence.RequeueCronJobOccurrenceAsync(Guid.NewGuid(), ct)).Should().Be(JobRequeueOutcome.NotFound);
    }

    /// <summary>Skip overlap refuses while another occurrence runs; Allow requeues beside it.</summary>
    public virtual async Task overlap_policy_decides_a_requeue_next_to_a_running_occurrence()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost("requeue-overlap");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        var persistence = _Persistence(host);

        var skipId = await _SeedDefinitionAsync("requeue-overlap-skip", CronOverlapPolicy.Skip, ct);
        var skipFailed = await _SeedOccurrenceAsync(skipId, JobStatus.Failed, _Instant(-3), ct);
        await _SeedOccurrenceAsync(skipId, JobStatus.InProgress, _Instant(-2), ct);
        var allowId = await _SeedDefinitionAsync("requeue-overlap-allow", CronOverlapPolicy.Allow, ct);
        var allowFailed = await _SeedOccurrenceAsync(allowId, JobStatus.Failed, _Instant(-3), ct);
        await _SeedOccurrenceAsync(allowId, JobStatus.InProgress, _Instant(-2), ct);

        (await persistence.RequeueCronJobOccurrenceAsync(skipFailed, ct)).Should().Be(JobRequeueOutcome.Overlap);
        (await persistence.GetAllCronJobOccurrencesAsync(x => x.Id == skipFailed, ct))
            .Single()
            .Status.Should()
            .Be(JobStatus.Failed);
        (await persistence.RequeueCronJobOccurrenceAsync(allowFailed, ct)).Should().Be(JobRequeueOutcome.Requeued);
    }

    /// <summary>Skip overlap accepts the requeue once no other occurrence is unfinished.</summary>
    public virtual async Task skip_overlap_requeues_when_no_other_occurrence_is_unfinished()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost("requeue-overlap-finished");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        var persistence = _Persistence(host);
        var cronId = await _SeedDefinitionAsync("requeue-overlap-finished", CronOverlapPolicy.Skip, ct);
        var failed = await _SeedOccurrenceAsync(cronId, JobStatus.Failed, _Instant(-3), ct);
        await _SeedOccurrenceAsync(cronId, JobStatus.Succeeded, _Instant(-2), ct);

        (await persistence.RequeueCronJobOccurrenceAsync(failed, ct)).Should().Be(JobRequeueOutcome.Requeued);
    }

    /// <summary>A live row already holding the occurrence's instant refuses the requeue instead of violating the index.</summary>
    public virtual async Task occurrence_whose_instant_is_held_by_a_live_row_is_refused()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost("requeue-instant");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        var persistence = _Persistence(host);
        var cronId = await _SeedDefinitionAsync("requeue-instant", CronOverlapPolicy.Allow, ct);
        var instant = _Instant(-2);
        var failed = await _SeedOccurrenceAsync(cronId, JobStatus.Failed, instant, ct);
        await _SeedOccurrenceAsync(cronId, JobStatus.Idle, instant, ct);

        (await persistence.RequeueCronJobOccurrenceAsync(failed, ct)).Should().Be(JobRequeueOutcome.Conflict);
        (await persistence.GetAllCronJobOccurrencesAsync(x => x.Id == failed, ct))
            .Single()
            .Status.Should()
            .Be(JobStatus.Failed);
    }

    /// <summary>Concurrent requeues of one occurrence move it exactly once.</summary>
    public virtual async Task concurrent_occurrence_requeues_move_the_row_once()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost("requeue-occurrence-race");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        var persistence = _Persistence(host);
        var cronId = await _SeedDefinitionAsync("requeue-occurrence-race", CronOverlapPolicy.Skip, ct);
        var failed = await _SeedOccurrenceAsync(cronId, JobStatus.Failed, _Instant(-2), ct);

        var outcomes = await Task.WhenAll(
            Enumerable
                .Range(0, 8)
                .Select(_ => Task.Run(() => persistence.RequeueCronJobOccurrenceAsync(failed, ct), ct))
        );

        outcomes.Count(x => x == JobRequeueOutcome.Requeued).Should().Be(1);
        outcomes
            .Where(x => x != JobRequeueOutcome.Requeued)
            .Should()
            .OnlyContain(x => x == JobRequeueOutcome.NotFailed || x == JobRequeueOutcome.Conflict);
    }

    private static TimeJobEntity _FailedJob() =>
        new()
        {
            Id = Guid.NewGuid(),
            Function = "requeue",
            Status = JobStatus.Failed,
            OwnerId = "node-b@1",
            LockedUntil = DateTime.UtcNow.AddMinutes(-30),
            ExecutionTime = DateTime.UtcNow.AddHours(-1),
            ExecutedAt = DateTimeOffset.UtcNow.AddMinutes(-59),
            ElapsedTime = 3000,
            ExceptionMessage = "boom",
            Retries = 3,
            RetryCount = 3,
            RetryIntervals = [5, 10, 20],
        };

    private static TimeJobEntity _KeyedCandidate() =>
        new()
        {
            Id = Guid.NewGuid(),
            Function = "requeue-keyed",
            ContractVersion = "1",
            ExecutionTime = DateTime.UtcNow.AddHours(1),
            Request = [1, 2, 3],
        };

    // A keyed row reaches Failed only by executing, which a storage test does not drive; set it directly instead.
    private async Task _MarkTimeJobFailedAsync(Guid id, CancellationToken ct)
    {
        await using var connection = fixture.CreateConnection();
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = fixture.Sql(
            $"UPDATE {fixture.QualifiedTimeJobsTable} SET \"Status\" = @status, \"ExceptionMessage\" = 'boom' "
                + "WHERE \"Id\" = @id;"
        );
        JobsCoordinationFixtureExtensions.AddParameter(command, "@status", nameof(JobStatus.Failed));
        JobsCoordinationFixtureExtensions.AddParameter(command, "@id", id);
        (await command.ExecuteNonQueryAsync(ct)).Should().Be(1);
    }

    private async Task<Guid> _SeedDefinitionAsync(string function, CronOverlapPolicy policy, CancellationToken ct)
    {
        var id = Guid.NewGuid();
        await fixture.SeedCronJobAsync(
            id,
            function,
            "0 0 * * * *",
            NodeDeathPolicy.Retry,
            ct,
            reconciledThroughOffsetSeconds: -3600,
            nextDueOffsetSeconds: 3600,
            onOverlap: policy
        );
        return id;
    }

    private async Task<Guid> _SeedOccurrenceAsync(Guid cronId, JobStatus status, DateTime instant, CancellationToken ct)
    {
        var id = Guid.NewGuid();
        var claimed = status is JobStatus.Queued or JobStatus.InProgress or JobStatus.Failed;
        await fixture.SeedCronOccurrenceAsync(
            id,
            cronId,
            (int)status,
            claimed ? "node-b@1" : null,
            NodeDeathPolicy.Retry,
            claimed ? DateTime.UtcNow.AddMinutes(5) : null,
            instant,
            ct
        );

        if (status == JobStatus.Failed)
        {
            await using var connection = fixture.CreateConnection();
            await connection.OpenAsync(ct);
            await using var command = connection.CreateCommand();
            command.CommandText = fixture.Sql(
                $"UPDATE {fixture.QualifiedCronJobOccurrencesTable} SET \"ExceptionMessage\" = 'boom', "
                    + "\"RetryCount\" = 2, \"ElapsedTime\" = 3000 WHERE \"Id\" = @id;"
            );
            JobsCoordinationFixtureExtensions.AddParameter(command, "@id", id);
            await command.ExecuteNonQueryAsync(ct);
        }

        return id;
    }

    // Whole seconds, so the instant survives the round trip through both providers' timestamp precision.
    private static DateTime _Instant(int hoursFromNow)
    {
        var now = DateTime.UtcNow;
        return new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0, DateTimeKind.Utc).AddHours(hoursFromNow);
    }

    private static IJobPersistenceProvider<TimeJobEntity, CronJobEntity> _Persistence(IHost host) =>
        host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
}
