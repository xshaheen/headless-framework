// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Jobs;
using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Models;
using Headless.Jobs.Provider;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Tests.Provider;

/// <summary>
/// The overlap policy as the in-memory provider applies it: at materialization and to a recovery's coalesced run,
/// always against the definition's persisted value.
/// </summary>
public sealed class CronOverlapPolicyProviderTests : TestBase
{
    private sealed class FakeTimeJob : TimeJobEntity<FakeTimeJob>;

    private sealed class FakeCronJob : CronJobEntity;

    private const string _Owner = "node-a@incarnation";
    private static readonly DateTimeOffset _Now = new(2026, 7, 26, 17, 30, 0, TimeSpan.Zero);

    // An hourly definition reconciled through 16:00 whose 17:00 occurrence is due.
    private static readonly DateTime _Previous = new(2026, 7, 26, 16, 00, 0, DateTimeKind.Utc);
    private static readonly DateTime _Due = new(2026, 7, 26, 17, 00, 0, DateTimeKind.Utc);
    private static readonly DateTime _Next = new(2026, 7, 26, 18, 00, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(JobStatus.Idle)]
    [InlineData(JobStatus.Queued)]
    [InlineData(JobStatus.InProgress)]
    public async Task should_record_the_due_occurrence_as_skipped_while_an_earlier_one_is_unfinished(
        JobStatus unfinished
    )
    {
        var provider = _Create();
        var definition = _Definition(CronOverlapPolicy.Skip);
        await provider.InsertCronJobsAsync([definition], AbortToken);
        await provider.InsertCronJobOccurrencesAsync([_Occurrence(definition.Id, _Previous, unfinished)], AbortToken);

        var result = await provider.MaterializeCronScheduleOccurrenceAsync(_Materialization(definition), AbortToken);

        result.Outcome.Should().Be(CronScheduleMaterializationOutcome.OccurrenceSkippedForOverlap);
        var skipped = (
            await provider.GetAllCronJobOccurrencesAsync(x => x.CronJobId == definition.Id, AbortToken)
        ).Single(x => x.ExecutionTime == _Due);
        skipped.Id.Should().Be(result.OccurrenceId!.Value);
        skipped.Status.Should().Be(JobStatus.Skipped);
        skipped.SkippedReason.Should().Contain("overlap policy");
        skipped
            .Disposition.Should()
            .Be(CronOccurrenceDisposition.Accounted, "the skipped instant is handled and must never be replayed");

        var stored = await provider.GetCronJobByIdAsync(definition.Id, AbortToken);
        stored!.ReconciledThroughUtc.Should().Be(_Due, "the schedule moves past a skipped instant");
        stored.NextDueUtc.Should().Be(_Next);
    }

    [Theory]
    [InlineData(JobStatus.Succeeded)]
    [InlineData(JobStatus.Failed)]
    [InlineData(JobStatus.Skipped)]
    [InlineData(JobStatus.Cancelled)]
    public async Task should_materialize_normally_when_every_earlier_occurrence_is_finished(JobStatus finished)
    {
        var provider = _Create();
        var definition = _Definition(CronOverlapPolicy.Skip);
        await provider.InsertCronJobsAsync([definition], AbortToken);
        await provider.InsertCronJobOccurrencesAsync([_Occurrence(definition.Id, _Previous, finished)], AbortToken);

        var result = await provider.MaterializeCronScheduleOccurrenceAsync(_Materialization(definition), AbortToken);

        result.Outcome.Should().Be(CronScheduleMaterializationOutcome.OccurrenceCreated);
        var created = (
            await provider.GetAllCronJobOccurrencesAsync(x => x.CronJobId == definition.Id, AbortToken)
        ).Single(x => x.ExecutionTime == _Due);
        created.Status.Should().Be(JobStatus.Idle);
    }

    [Fact]
    public async Task should_materialize_alongside_an_unfinished_occurrence_under_allow()
    {
        var provider = _Create();
        var definition = _Definition(CronOverlapPolicy.Allow);
        await provider.InsertCronJobsAsync([definition], AbortToken);
        await provider.InsertCronJobOccurrencesAsync(
            [_Occurrence(definition.Id, _Previous, JobStatus.InProgress)],
            AbortToken
        );

        var result = await provider.MaterializeCronScheduleOccurrenceAsync(_Materialization(definition), AbortToken);

        result.Outcome.Should().Be(CronScheduleMaterializationOutcome.OccurrenceCreated);
    }

    [Fact]
    public async Task should_ignore_unfinished_occurrences_of_other_definitions()
    {
        var provider = _Create();
        var definition = _Definition(CronOverlapPolicy.Skip);
        var sibling = _Definition(CronOverlapPolicy.Skip);
        await provider.InsertCronJobsAsync([definition, sibling], AbortToken);
        await provider.InsertCronJobOccurrencesAsync(
            [_Occurrence(sibling.Id, _Previous, JobStatus.InProgress)],
            AbortToken
        );

        var result = await provider.MaterializeCronScheduleOccurrenceAsync(_Materialization(definition), AbortToken);

        result.Outcome.Should().Be(CronScheduleMaterializationOutcome.OccurrenceCreated);
    }

    [Fact]
    public async Task should_skip_a_recovery_run_while_an_execution_from_before_the_outage_is_unfinished()
    {
        var provider = _Create();
        var definition = _Definition(CronOverlapPolicy.Skip);
        definition.ReconciledThroughUtc = _Previous.AddHours(-3);
        definition.NextDueUtc = _Previous.AddHours(-2);
        await provider.InsertCronJobsAsync([definition], AbortToken);
        var running = _Occurrence(definition.Id, definition.ReconciledThroughUtc, JobStatus.InProgress);
        await provider.InsertCronJobOccurrencesAsync([running], AbortToken);

        var result = await provider.ApplyCronRecoveryAsync(_Recovery(definition), AbortToken);

        result.Should().NotBeNull();
        result!.CoalescedRun.Should().BeNull("a run that would overlap the unfinished execution must not be claimable");
        result.OverlapSkippedRun.Should().NotBeNull();

        var occurrences = await provider.GetAllCronJobOccurrencesAsync(x => x.CronJobId == definition.Id, AbortToken);
        var skippedRun = occurrences.Single(x => x.Id == result.OverlapSkippedRun!.Id);
        skippedRun.Status.Should().Be(JobStatus.Skipped);
        skippedRun.SkippedReason.Should().Contain("overlap policy");
        skippedRun.Disposition.Should().Be(CronOccurrenceDisposition.Accounted);
        occurrences.Single(x => x.Id == running.Id).Status.Should().Be(JobStatus.InProgress, "never touched");

        var stored = await provider.GetCronJobByIdAsync(definition.Id, AbortToken);
        stored!.ReconciledThroughUtc.Should().Be(_Now.UtcDateTime, "the resolved backlog is never reconsidered");
    }

    [Fact]
    public async Task should_keep_a_recovery_run_when_the_only_blockers_were_retired_by_that_recovery()
    {
        var provider = _Create();
        var definition = _Definition(CronOverlapPolicy.Skip);
        definition.ReconciledThroughUtc = _Previous.AddHours(-3);
        definition.NextDueUtc = _Previous.AddHours(-2);
        await provider.InsertCronJobsAsync([definition], AbortToken);
        // Idle rows inside the missed window are the backlog itself: recovery retires or repurposes them, so they
        // must not also count as unfinished work the run would overlap.
        await provider.InsertCronJobOccurrencesAsync(
            [
                _Occurrence(definition.Id, _Previous.AddHours(-2), JobStatus.Idle),
                _Occurrence(definition.Id, _Previous.AddHours(-1), JobStatus.Idle),
            ],
            AbortToken
        );

        var result = await provider.ApplyCronRecoveryAsync(
            _Recovery(definition, [_Previous.AddHours(-2), _Previous.AddHours(-1), _Previous]),
            AbortToken
        );

        result!.CoalescedRun.Should().NotBeNull();
        result.OverlapSkippedRun.Should().BeNull();
    }

    [Fact]
    public async Task should_seed_the_resolved_overlap_policy_only_when_a_definition_is_created()
    {
        var provider = _Create();

        await provider.MigrateDefinedCronJobsAsync(
            [
                new CronSeedDefinition("seeded", "0 * * * * *", MissedRunPolicy.Coalesce, 60)
                {
                    OnOverlap = CronOverlapPolicy.Skip,
                },
            ],
            AbortToken
        );
        var created = (await provider.GetCronJobsAsync(predicate: null, AbortToken)).Single();
        created.OnOverlap.Should().Be(CronOverlapPolicy.Skip);

        var overridden = (FakeCronJob)created.Clone();
        overridden.OnOverlap = CronOverlapPolicy.Allow;
        var updated = await provider.UpdateCronJobsAtomicallyAsync(
            [new CronJobAtomicUpdate<FakeCronJob>(overridden, created.ScheduleRevision, NextOccurrenceFactory: null)],
            _Now,
            AbortToken
        );
        updated.Should().NotBeNull();
        updated!
            .Single()
            .ScheduleRevision.Should()
            .Be(created.ScheduleRevision, "materialization reads the policy fresh, so no stale copy needs fencing");

        await provider.MigrateDefinedCronJobsAsync(
            [
                new CronSeedDefinition("seeded", "0 * * * * *", MissedRunPolicy.Coalesce, 60)
                {
                    OnOverlap = CronOverlapPolicy.Skip,
                },
            ],
            AbortToken
        );

        var afterRestart = (await provider.GetCronJobsAsync(predicate: null, AbortToken)).Single();
        afterRestart
            .OnOverlap.Should()
            .Be(CronOverlapPolicy.Allow, "the attribute seeds at creation only and never reverts an operator override");
    }

    /// <summary>
    /// A schedule edit must not pre-create its next occurrence next to a running one: the timed-out sweep would later
    /// claim that row with no overlap check. The instant is left to materialization, which judges it when due.
    /// </summary>
    [Fact]
    public async Task should_leave_an_edit_replacement_to_materialization_while_an_occurrence_is_running()
    {
        var provider = _Create();
        var definition = _Definition(CronOverlapPolicy.Skip);
        await provider.InsertCronJobsAsync([definition], AbortToken);
        await provider.InsertCronJobOccurrencesAsync(
            [_Occurrence(definition.Id, _Previous, JobStatus.InProgress)],
            AbortToken
        );

        var edited = (FakeCronJob)definition.Clone();
        edited.Expression = "0 30 * * * *";
        var updated = await provider.UpdateCronJobsAtomicallyAsync(
            [new CronJobAtomicUpdate<FakeCronJob>(edited, definition.ScheduleRevision, _NextAt(definition.Id, _Next))],
            _Now,
            AbortToken
        );

        updated.Should().NotBeNull();
        var occurrences = await provider.GetAllCronJobOccurrencesAsync(x => x.CronJobId == definition.Id, AbortToken);
        occurrences.Should().ContainSingle("only the running occurrence remains; no replacement was pre-created");
        var stored = await provider.GetCronJobByIdAsync(definition.Id, AbortToken);
        stored!.NextDueUtc.Should().Be(_Next, "the projection still moves, so materialization creates the instant");
    }

    [Fact]
    public async Task should_still_pre_create_an_edit_replacement_under_allow()
    {
        var provider = _Create();
        var definition = _Definition(CronOverlapPolicy.Allow);
        await provider.InsertCronJobsAsync([definition], AbortToken);
        await provider.InsertCronJobOccurrencesAsync(
            [_Occurrence(definition.Id, _Previous, JobStatus.InProgress)],
            AbortToken
        );

        var edited = (FakeCronJob)definition.Clone();
        edited.Expression = "0 30 * * * *";
        await provider.UpdateCronJobsAtomicallyAsync(
            [new CronJobAtomicUpdate<FakeCronJob>(edited, definition.ScheduleRevision, _NextAt(definition.Id, _Next))],
            _Now,
            AbortToken
        );

        (await provider.GetAllCronJobOccurrencesAsync(x => x.CronJobId == definition.Id, AbortToken))
            .Should()
            .Contain(x => x.ExecutionTime == _Next && x.Status == JobStatus.Idle);
    }

    [Fact]
    public async Task should_leave_a_resume_replacement_to_materialization_while_an_occurrence_is_running()
    {
        var provider = _Create();
        var definition = _Definition(CronOverlapPolicy.Skip);
        definition.IsPaused = true;
        await provider.InsertCronJobsAsync([definition], AbortToken);
        await provider.InsertCronJobOccurrencesAsync(
            [_Occurrence(definition.Id, _Previous, JobStatus.InProgress)],
            AbortToken
        );

        var resumed = await provider.ResumeCronJobAsync(
            definition.Id,
            definition.ScheduleRevision,
            _NextAt(definition.Id, _Next),
            _Now,
            AbortToken
        );

        resumed.Should().NotBeNull();
        resumed!.NextDueUtc.Should().Be(_Next);
        (await provider.GetAllCronJobOccurrencesAsync(x => x.CronJobId == definition.Id, AbortToken))
            .Should()
            .ContainSingle("the resume left its next instant to materialization");
    }

    /// <summary>A live row created ahead of its instant is judged when that instant falls due.</summary>
    [Fact]
    public async Task should_retire_a_pre_created_idle_occurrence_when_it_falls_due_next_to_a_running_one()
    {
        var provider = _Create();
        var definition = _Definition(CronOverlapPolicy.Skip);
        await provider.InsertCronJobsAsync([definition], AbortToken);
        var preCreated = _Occurrence(definition.Id, _Due, JobStatus.Idle);
        await provider.InsertCronJobOccurrencesAsync(
            [_Occurrence(definition.Id, _Previous, JobStatus.InProgress), preCreated],
            AbortToken
        );

        var result = await provider.MaterializeCronScheduleOccurrenceAsync(_Materialization(definition), AbortToken);

        result.Outcome.Should().Be(CronScheduleMaterializationOutcome.OccurrenceSkippedForOverlap);
        result.OccurrenceId.Should().Be(preCreated.Id);
        var stored = (
            await provider.GetAllCronJobOccurrencesAsync(x => x.CronJobId == definition.Id, AbortToken)
        ).Single(x => x.Id == preCreated.Id);
        stored.Status.Should().Be(JobStatus.Skipped);
        stored.SkippedReason.Should().Contain("overlap policy");
    }

    private static Func<DateTime, CronJobOccurrenceEntity<FakeCronJob>?> _NextAt(Guid definitionId, DateTime at) =>
        _ => new CronJobOccurrenceEntity<FakeCronJob>
        {
            Id = Guid.NewGuid(),
            CronJobId = definitionId,
            ExecutionTime = at,
            Status = JobStatus.Idle,
            CreatedAt = _Now,
            UpdatedAt = _Now,
        };

    private static CronScheduleMaterialization _Materialization(FakeCronJob definition) =>
        new()
        {
            Advance = new CronScheduleAdvance
            {
                CronJobId = definition.Id,
                ObservedReconciledThroughUtc = definition.ReconciledThroughUtc,
                ExpectedScheduleRevision = definition.ScheduleRevision,
                ReconciledThroughUtc = _Due,
                NextDueUtc = _Next,
                RequireProjectionDue = true,
            },
            ExecutionTimeUtc = _Due,
        };

    private static CronRecoveryRequest _Recovery(FakeCronJob definition, DateTime[]? missedInstants = null) =>
        new()
        {
            CronJobId = definition.Id,
            ObservedReconciledThroughUtc = definition.ReconciledThroughUtc,
            ExpectedScheduleRevision = definition.ScheduleRevision,
            RecoveredThroughUtc = _Now.UtcDateTime,
            NextDueUtc = _Next,
            BoundedProgressThroughUtc = _Now.UtcDateTime,
            NextDueAfterBoundedProgressUtc = _Next,
            EvaluationSaturated = false,
            Policy = MissedRunPolicy.Coalesce,
            EarliestMissedUtc = definition.NextDueUtc,
            MissedInstantsUtc = missedInstants ?? [definition.NextDueUtc],
            CoalescedOccurrenceId = Guid.NewGuid(),
            OnNodeDeath = NodeDeathPolicy.Retry,
            OperationTimeUtc = _Now,
        };

    private static JobsInMemoryPersistenceProvider<FakeTimeJob, FakeCronJob> _Create()
    {
        var services = new ServiceCollection();
        services.AddHeadlessGuidGenerator();
        services.AddSingleton<TimeProvider>(new FakeTimeProvider(_Now));
        services.AddSingleton(new SchedulerOptionsBuilder { NodeId = _Owner });
        return new JobsInMemoryPersistenceProvider<FakeTimeJob, FakeCronJob>(services.BuildServiceProvider());
    }

    private static FakeCronJob _Definition(CronOverlapPolicy policy) =>
        new()
        {
            Id = Guid.NewGuid(),
            Function = "cron-overlap",
            Expression = "0 0 * * * *",
            ScheduleRevision = 2,
            ReconciledThroughUtc = _Previous,
            NextDueUtc = _Due,
            OnMissedRun = MissedRunPolicy.Coalesce,
            MissedRunGraceSeconds = 3600,
            OnOverlap = policy,
            CreatedAt = _Now.AddDays(-1),
            UpdatedAt = _Now.AddHours(-4),
        };

    private static CronJobOccurrenceEntity<FakeCronJob> _Occurrence(
        Guid definitionId,
        DateTime executionTime,
        JobStatus status
    ) =>
        new()
        {
            Id = Guid.NewGuid(),
            CronJobId = definitionId,
            ExecutionTime = executionTime,
            Status = status,
            OwnerId = status is JobStatus.Queued or JobStatus.InProgress ? "node-b@1" : null,
            LockedUntil = status is JobStatus.Queued or JobStatus.InProgress ? _Now.UtcDateTime.AddMinutes(5) : null,
            CreatedAt = _Now.AddHours(-2),
            UpdatedAt = _Now.AddHours(-2),
        };
}
