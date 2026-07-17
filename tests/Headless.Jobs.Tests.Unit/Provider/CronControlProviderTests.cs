// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs;
using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Models;
using Headless.Jobs.Provider;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Tests.Provider;

public sealed class CronControlProviderTests : TestBase
{
    private sealed class FakeTimeJob : TimeJobEntity<FakeTimeJob>;

    private sealed class FakeCronJob : CronJobEntity;

    private const string _Owner = "node-a@incarnation";
    private static readonly DateTime _Now = new(2026, 7, 17, 10, 30, 0, DateTimeKind.Utc);

    [Fact]
    public async Task pause_is_one_transition_that_skips_pending_but_preserves_in_progress_work()
    {
        var provider = _Create();
        var definition = _Definition(isPaused: false, revision: 4);
        var lockedUntil = _Now.AddMinutes(5);
        var idle = _Occurrence(definition.Id, JobStatus.Idle, _Owner, lockedUntil);
        var queued = _Occurrence(definition.Id, JobStatus.Queued, _Owner, lockedUntil);
        var inProgress = _Occurrence(definition.Id, JobStatus.InProgress, _Owner, lockedUntil);
        await provider.InsertCronJobsAsync([definition], AbortToken);
        await provider.InsertCronJobOccurrencesAsync([idle, queued, inProgress], AbortToken);

        var accepted = await provider.PauseCronJobAsync(definition.Id, _Now, AbortToken);

        accepted.Should().NotBeNull();
        accepted!.IsPaused.Should().BeTrue();
        accepted.ScheduleRevision.Should().Be(5);
        (await provider.PauseCronJobAsync(definition.Id, _Now, AbortToken)).Should().BeNull();
        (await provider.PauseCronJobAsync(Guid.NewGuid(), _Now, AbortToken)).Should().BeNull();

        var occurrences = await provider.GetAllCronJobOccurrencesAsync(x => x.CronJobId == definition.Id, AbortToken);
        foreach (var pending in occurrences.Where(x => x.Id == idle.Id || x.Id == queued.Id))
        {
            pending.Status.Should().Be(JobStatus.Skipped);
            pending.ExecutedAt.Should().Be(_Now);
            pending.UpdatedAt.Should().Be(_Now);
            pending.SkippedReason.Should().Be("Cron definition paused");
            pending.OwnerId.Should().BeNull();
            pending.LockedUntil.Should().BeNull();
        }

        var running = occurrences.Single(x => x.Id == inProgress.Id);
        running.Status.Should().Be(JobStatus.InProgress);
        running.ExecutedAt.Should().BeNull();
        running.OwnerId.Should().Be(_Owner);
        running.LockedUntil.Should().Be(lockedUntil);
    }

    [Fact]
    public async Task concurrent_resume_has_one_winner_and_one_replacement_occurrence()
    {
        var provider = _Create();
        var definition = _Definition(isPaused: true, revision: 7);
        await provider.InsertCronJobsAsync([definition], AbortToken);
        var executionTime = _Now.AddMinutes(15);

        var attempts = await Task.WhenAll(
            Enumerable
                .Range(0, 8)
                .Select(_ =>
                    provider.ResumeCronJobAsync(
                        definition.Id,
                        expectedScheduleRevision: 7,
                        _Occurrence(definition.Id, JobStatus.Idle, owner: null, lockedUntil: null, executionTime),
                        _Now,
                        AbortToken
                    )
                )
        );

        attempts.Count(x => x is not null).Should().Be(1);
        attempts.Single(x => x is not null)!.ScheduleRevision.Should().Be(8);
        attempts.Single(x => x is not null)!.IsPaused.Should().BeFalse();
        var occurrences = await provider.GetAllCronJobOccurrencesAsync(x => x.CronJobId == definition.Id, AbortToken);
        occurrences.Should().ContainSingle(x => x.ExecutionTime == executionTime && x.Status == JobStatus.Idle);
    }

    [Fact]
    public async Task stale_or_paused_dispatch_context_cannot_materialize_an_occurrence()
    {
        var provider = _Create();
        var definition = _Definition(isPaused: false, revision: 3);
        await provider.InsertCronJobsAsync([definition], AbortToken);
        var stale = _Dispatch(definition, revision: 2);

        var staleResult = await provider
            .QueueCronJobOccurrencesAsync((_Now.AddMinutes(1), [stale]), AbortToken)
            .ToArrayAsync(AbortToken);
        await provider.PauseCronJobAsync(definition.Id, _Now, AbortToken);
        var pausedResult = await provider
            .QueueCronJobOccurrencesAsync((_Now.AddMinutes(2), [_Dispatch(definition, revision: 4)]), AbortToken)
            .ToArrayAsync(AbortToken);

        staleResult.Should().BeEmpty();
        pausedResult.Should().BeEmpty();
        (await provider.GetAllCronJobOccurrencesAsync(null, AbortToken)).Should().BeEmpty();
    }

    [Fact]
    public async Task schedule_edit_retires_pending_work_but_metadata_edit_preserves_it()
    {
        var provider = _Create();
        var definition = _Definition(isPaused: false, revision: 2);
        var existing = _Occurrence(definition.Id, JobStatus.Queued, _Owner, _Now.AddMinutes(5));
        await provider.InsertCronJobsAsync([definition], AbortToken);
        await provider.InsertCronJobOccurrencesAsync([existing], AbortToken);

        var metadataEdit = _Definition(isPaused: true, revision: 999);
        metadataEdit.Id = definition.Id;
        metadataEdit.Description = "renamed";
        var metadataResult = await provider.UpdateCronJobsAtomicallyAsync(
            [new CronJobAtomicUpdate<FakeCronJob>(metadataEdit, ExpectedScheduleRevision: 2, NextOccurrence: null)],
            _Now,
            AbortToken
        );

        metadataResult.Should().NotBeNull();
        metadataResult![0].IsPaused.Should().BeFalse();
        metadataResult[0].ScheduleRevision.Should().Be(2);
        var afterMetadata = await provider.GetAllCronJobOccurrencesAsync(x => x.CronJobId == definition.Id, AbortToken);
        afterMetadata.Single().Status.Should().Be(JobStatus.Queued);

        var scheduleEdit = _Definition(isPaused: false, revision: 999, expression: "0 */5 * * * *");
        scheduleEdit.Id = definition.Id;
        var replacement = _Occurrence(
            definition.Id,
            JobStatus.Idle,
            owner: null,
            lockedUntil: null,
            _Now.AddMinutes(10)
        );
        var scheduleResult = await provider.UpdateCronJobsAtomicallyAsync(
            [new CronJobAtomicUpdate<FakeCronJob>(scheduleEdit, ExpectedScheduleRevision: 2, replacement)],
            _Now,
            AbortToken
        );

        scheduleResult.Should().NotBeNull();
        scheduleResult![0].ScheduleRevision.Should().Be(3);
        var afterSchedule = await provider.GetAllCronJobOccurrencesAsync(x => x.CronJobId == definition.Id, AbortToken);
        afterSchedule.Single(x => x.Id == existing.Id).Status.Should().Be(JobStatus.Skipped);
        afterSchedule.Single(x => x.Id == existing.Id).SkippedReason.Should().Be("Cron definition updated");
        afterSchedule.Single(x => x.Id == replacement.Id).Status.Should().Be(JobStatus.Idle);
    }

    private static JobsInMemoryPersistenceProvider<FakeTimeJob, FakeCronJob> _Create()
    {
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(new FakeTimeProvider(new DateTimeOffset(_Now, TimeSpan.Zero)));
        services.AddSingleton(new SchedulerOptionsBuilder { NodeId = _Owner });
        return new JobsInMemoryPersistenceProvider<FakeTimeJob, FakeCronJob>(services.BuildServiceProvider());
    }

    private static FakeCronJob _Definition(bool isPaused, long revision, string expression = "0 * * * * *") =>
        new()
        {
            Id = Guid.NewGuid(),
            Function = "cron-control",
            Expression = expression,
            IsPaused = isPaused,
            ScheduleRevision = revision,
            CreatedAt = _Now.AddHours(-1),
            UpdatedAt = _Now.AddMinutes(-1),
        };

    private static CronJobOccurrenceEntity<FakeCronJob> _Occurrence(
        Guid definitionId,
        JobStatus status,
        string? owner,
        DateTime? lockedUntil,
        DateTime? executionTime = null
    ) =>
        new()
        {
            Id = Guid.NewGuid(),
            CronJobId = definitionId,
            Status = status,
            OwnerId = owner,
            LockedUntil = lockedUntil,
            ExecutionTime = executionTime ?? _Now.AddMinutes(1),
            CreatedAt = _Now.AddMinutes(-5),
            UpdatedAt = _Now.AddMinutes(-2),
        };

    private static JobManagerDispatchContext _Dispatch(FakeCronJob definition, long revision) =>
        new(definition.Id)
        {
            FunctionName = definition.Function,
            Expression = definition.Expression,
            IsPaused = definition.IsPaused,
            ScheduleRevision = revision,
        };
}
