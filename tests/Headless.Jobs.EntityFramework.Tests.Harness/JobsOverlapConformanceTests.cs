// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Models;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests;

#pragma warning disable CA1707 // Test names follow the repo's readable snake_case convention.

/// <summary>
/// Cross-provider conformance for the cron overlap policy. The in-memory suite proves the rule; this proves it holds
/// inside a relational provider's materialization transaction, where the definition row lock is what makes the
/// "is an earlier occurrence unfinished?" read and the occurrence write one decision.
/// </summary>
public abstract class JobsOverlapConformanceTests<TFixture>(TFixture fixture) : TestBase
    where TFixture : class, IJobsCoordinationFixture
{
    private const string _OverlapReason = "Skipped by overlap policy: an earlier occurrence was still unfinished";

    /// <summary>Skip records the due occurrence as skipped while an earlier one is in progress.</summary>
    public virtual async Task skip_records_the_due_occurrence_as_skipped_while_an_earlier_one_is_in_progress()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost("overlap-skip");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        var persistence = _Persistence(host);

        var cronId = Guid.NewGuid();
        var seeded = await _SeedDueDefinitionAsync(cronId, "overlap-skip", CronOverlapPolicy.Skip, ct);
        await _SeedEarlierAsync(cronId, seeded, JobStatus.InProgress, ct);

        var result = await persistence.MaterializeCronScheduleOccurrenceAsync(_Materialization(cronId, seeded), ct);

        result.Outcome.Should().Be(CronScheduleMaterializationOutcome.OccurrenceSkippedForOverlap);
        var skipped = (await persistence.GetAllCronJobOccurrencesAsync(x => x.CronJobId == cronId, ct)).Single(x =>
            x.Id == result.OccurrenceId
        );
        skipped.Status.Should().Be(JobStatus.Skipped);
        skipped.SkippedReason.Should().Be(_OverlapReason);
        skipped.Disposition.Should().Be(CronOccurrenceDisposition.Accounted, "the skipped instant must never replay");

        var position = await fixture.ReadCronSchedulePositionAsync(cronId, ct);
        position
            .ReconciledThroughUtc.Should()
            .BeCloseTo(seeded.NextDueUtc, TimeSpan.FromMicroseconds(1), "the schedule moves past a skipped instant");
    }

    /// <summary>
    /// A retry waiting to re-run after its node died is the same unfinished run, so it blocks the next instant too.
    /// </summary>
    public virtual async Task skip_treats_an_idle_retry_as_unfinished()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost("overlap-retry");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        var persistence = _Persistence(host);

        var cronId = Guid.NewGuid();
        var seeded = await _SeedDueDefinitionAsync(cronId, "overlap-retry", CronOverlapPolicy.Skip, ct);
        await _SeedEarlierAsync(cronId, seeded, JobStatus.Idle, ct);

        var result = await persistence.MaterializeCronScheduleOccurrenceAsync(_Materialization(cronId, seeded), ct);

        result.Outcome.Should().Be(CronScheduleMaterializationOutcome.OccurrenceSkippedForOverlap);
    }

    /// <summary>A finished earlier occurrence does not block the due one.</summary>
    public virtual async Task skip_materializes_normally_once_the_earlier_occurrence_finished()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost("overlap-finished");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        var persistence = _Persistence(host);

        var cronId = Guid.NewGuid();
        var seeded = await _SeedDueDefinitionAsync(cronId, "overlap-finished", CronOverlapPolicy.Skip, ct);
        await _SeedEarlierAsync(cronId, seeded, JobStatus.Succeeded, ct);

        var result = await persistence.MaterializeCronScheduleOccurrenceAsync(_Materialization(cronId, seeded), ct);

        result.Outcome.Should().Be(CronScheduleMaterializationOutcome.OccurrenceCreated);
        (await persistence.GetAllCronJobOccurrencesAsync(x => x.CronJobId == cronId, ct))
            .Single(x => x.Id == result.OccurrenceId)
            .Status.Should()
            .Be(JobStatus.Idle);
    }

    /// <summary>Allow keeps the historical behavior and materializes alongside an in-progress occurrence.</summary>
    public virtual async Task allow_materializes_alongside_an_in_progress_occurrence()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost("overlap-allow");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        var persistence = _Persistence(host);

        var cronId = Guid.NewGuid();
        var seeded = await _SeedDueDefinitionAsync(cronId, "overlap-allow", CronOverlapPolicy.Allow, ct);
        await _SeedEarlierAsync(cronId, seeded, JobStatus.InProgress, ct);

        var result = await persistence.MaterializeCronScheduleOccurrenceAsync(_Materialization(cronId, seeded), ct);

        result.Outcome.Should().Be(CronScheduleMaterializationOutcome.OccurrenceCreated);
    }

    /// <summary>
    /// Two nodes materializing the same due instant against an unfinished occurrence produce exactly one skipped row
    /// and no live one: the loser loses the position fence, never re-decides overlap on its own.
    /// </summary>
    public virtual async Task concurrent_nodes_record_one_skipped_occurrence_and_no_live_one()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var nodeA = fixture.BuildHost("overlap-race-a");
        using var nodeB = fixture.BuildHost("overlap-race-b");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(nodeA, ct);

        var cronId = Guid.NewGuid();
        var seeded = await _SeedDueDefinitionAsync(cronId, "overlap-race", CronOverlapPolicy.Skip, ct);
        await _SeedEarlierAsync(cronId, seeded, JobStatus.InProgress, ct);

        var results = await Task.WhenAll(
            _Persistence(nodeA).MaterializeCronScheduleOccurrenceAsync(_Materialization(cronId, seeded), ct),
            _Persistence(nodeB).MaterializeCronScheduleOccurrenceAsync(_Materialization(cronId, seeded), ct)
        );

        results
            .Select(x => x.Outcome)
            .Should()
            .BeEquivalentTo([
                CronScheduleMaterializationOutcome.OccurrenceSkippedForOverlap,
                CronScheduleMaterializationOutcome.LostFence,
            ]);

        var atInstant = (await _Persistence(nodeA).GetAllCronJobOccurrencesAsync(x => x.CronJobId == cronId, ct)).Where(
            x => x.ExecutionTime != seeded.ReconciledThroughUtc
        );
        atInstant.Should().ContainSingle().Which.Status.Should().Be(JobStatus.Skipped);
    }

    /// <summary>
    /// A recovery run that would overlap an execution started before the outage is recorded as skipped, and the
    /// execution itself is left untouched.
    /// </summary>
    public virtual async Task recovery_skips_its_run_while_an_execution_from_before_the_outage_is_unfinished()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost("overlap-recovery");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        var persistence = _Persistence(host);

        var cronId = Guid.NewGuid();
        await fixture.SeedCronJobAsync(
            cronId,
            "overlap-recovery",
            "0 0 * * * *",
            NodeDeathPolicy.Retry,
            ct,
            reconciledThroughOffsetSeconds: -14400,
            nextDueOffsetSeconds: -10800,
            onOverlap: CronOverlapPolicy.Skip
        );
        var seeded = await fixture.ReadCronSchedulePositionAsync(cronId, ct);
        var runningId = await _SeedEarlierAsync(cronId, seeded, JobStatus.InProgress, ct);

        var result = await persistence.ApplyCronRecoveryAsync(_Recovery(cronId, seeded), ct);

        result.Should().NotBeNull();
        result!.CoalescedRun.Should().BeNull();
        result.OverlapSkippedRun.Should().NotBeNull();

        var occurrences = await persistence.GetAllCronJobOccurrencesAsync(x => x.CronJobId == cronId, ct);
        var skippedRun = occurrences.Single(x => x.Id == result.OverlapSkippedRun!.Id);
        skippedRun.Status.Should().Be(JobStatus.Skipped);
        skippedRun.SkippedReason.Should().Be(_OverlapReason);
        skippedRun.Disposition.Should().Be(CronOccurrenceDisposition.Accounted);
        occurrences.Single(x => x.Id == runningId).Status.Should().Be(JobStatus.InProgress, "never touched");
    }

    /// <summary>
    /// The seeded value lands on a created definition, and a runtime override survives the next startup
    /// reconciliation — the same authority rule the missed-run policy follows.
    /// </summary>
    public virtual async Task seeding_applies_the_overlap_policy_only_at_creation()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost("overlap-seed");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        var persistence = _Persistence(host);
        var seed = new CronSeedDefinition("overlap-seed", "0 0 * * * *", MissedRunPolicy.Coalesce, 60)
        {
            OnOverlap = CronOverlapPolicy.Skip,
        };

        await persistence.MigrateDefinedCronJobsAsync([seed], ct);
        var created = (await persistence.GetCronJobsAsync(predicate: null, ct)).Single();
        created.OnOverlap.Should().Be(CronOverlapPolicy.Skip);

        created.OnOverlap = CronOverlapPolicy.Allow;
        var updated = await persistence.UpdateCronJobsAtomicallyAsync(
            [new CronJobAtomicUpdate<CronJobEntity>(created, created.ScheduleRevision, NextOccurrenceFactory: null)],
            DateTimeOffset.UtcNow,
            ct
        );
        updated.Should().NotBeNull();

        await persistence.MigrateDefinedCronJobsAsync([seed], ct);

        (await persistence.GetCronJobsAsync(predicate: null, ct))
            .Single()
            .OnOverlap.Should()
            .Be(CronOverlapPolicy.Allow, "the attribute seeds at creation only and never reverts an operator override");
    }

    /// <summary>
    /// A schedule edit next to a running occurrence leaves its next instant to materialization rather than inserting a
    /// row the timed-out sweep would later claim without an overlap check.
    /// </summary>
    public virtual async Task schedule_edit_does_not_pre_create_a_replacement_next_to_a_running_occurrence()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost("overlap-edit");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        var persistence = _Persistence(host);

        var cronId = Guid.NewGuid();
        var seeded = await _SeedDueDefinitionAsync(cronId, "overlap-edit", CronOverlapPolicy.Skip, ct);
        var runningId = await _SeedEarlierAsync(cronId, seeded, JobStatus.InProgress, ct);
        var definition = (await persistence.GetCronJobsAsync(x => x.Id == cronId, ct)).Single();
        definition.Expression = "0 30 * * * *";
        var nextAt = seeded.NextDueUtc.AddHours(1);

        var updated = await persistence.UpdateCronJobsAtomicallyAsync(
            [
                new CronJobAtomicUpdate<CronJobEntity>(
                    definition,
                    definition.ScheduleRevision,
                    _ => new CronJobOccurrenceEntity<CronJobEntity>
                    {
                        Id = Guid.NewGuid(),
                        CronJobId = cronId,
                        ExecutionTime = nextAt,
                        Status = JobStatus.Idle,
                        CreatedAt = DateTimeOffset.UtcNow,
                        UpdatedAt = DateTimeOffset.UtcNow,
                    }
                ),
            ],
            DateTimeOffset.UtcNow,
            ct
        );

        updated.Should().NotBeNull();
        (await persistence.GetAllCronJobOccurrencesAsync(x => x.CronJobId == cronId, ct))
            .Should()
            .ContainSingle("no replacement was pre-created")
            .Which.Id.Should()
            .Be(runningId);
        var position = await fixture.ReadCronSchedulePositionAsync(cronId, ct);
        position.NextDueUtc.Should().BeCloseTo(nextAt, TimeSpan.FromMicroseconds(1));
    }

    /// <summary>A live row created ahead of its instant is judged for overlap when the instant falls due.</summary>
    public virtual async Task materialization_retires_a_pre_created_idle_occurrence_next_to_a_running_one()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost("overlap-precreated");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        var persistence = _Persistence(host);

        var cronId = Guid.NewGuid();
        var seeded = await _SeedDueDefinitionAsync(cronId, "overlap-precreated", CronOverlapPolicy.Skip, ct);
        await _SeedEarlierAsync(cronId, seeded, JobStatus.InProgress, ct);
        var preCreatedId = Guid.NewGuid();
        await fixture.SeedCronOccurrenceAsync(
            preCreatedId,
            cronId,
            (int)JobStatus.Idle,
            null,
            NodeDeathPolicy.Retry,
            null,
            seeded.NextDueUtc,
            ct
        );

        var result = await persistence.MaterializeCronScheduleOccurrenceAsync(_Materialization(cronId, seeded), ct);

        result.Outcome.Should().Be(CronScheduleMaterializationOutcome.OccurrenceSkippedForOverlap);
        result.OccurrenceId.Should().Be(preCreatedId);
        var retired = (await persistence.GetAllCronJobOccurrencesAsync(x => x.CronJobId == cronId, ct)).Single(x =>
            x.Id == preCreatedId
        );
        retired.Status.Should().Be(JobStatus.Skipped);
        retired.SkippedReason.Should().Be(_OverlapReason);
    }

    private async Task<(DateTime ReconciledThroughUtc, DateTime NextDueUtc)> _SeedDueDefinitionAsync(
        Guid cronId,
        string function,
        CronOverlapPolicy policy,
        CancellationToken ct
    )
    {
        // Due thirty seconds ago, reconciled through the previous hour: an ordinary on-time tick, not a recovery.
        await fixture.SeedCronJobAsync(
            cronId,
            function,
            "0 0 * * * *",
            NodeDeathPolicy.Retry,
            ct,
            reconciledThroughOffsetSeconds: -3630,
            nextDueOffsetSeconds: -30,
            onOverlap: policy
        );

        return await fixture.ReadCronSchedulePositionAsync(cronId, ct);
    }

    /// <summary>Seeds the previous occurrence at the watermark instant, owned by another node when it is claimed.</summary>
    private async Task<Guid> _SeedEarlierAsync(
        Guid cronId,
        (DateTime ReconciledThroughUtc, DateTime NextDueUtc) seeded,
        JobStatus status,
        CancellationToken ct
    )
    {
        var id = Guid.NewGuid();
        var owned = status is JobStatus.Queued or JobStatus.InProgress;
        await fixture.SeedCronOccurrenceAsync(
            id,
            cronId,
            (int)status,
            owned ? "node-b@1" : null,
            NodeDeathPolicy.Retry,
            owned ? DateTime.UtcNow.AddMinutes(5) : null,
            seeded.ReconciledThroughUtc,
            ct
        );

        return id;
    }

    private static CronScheduleMaterialization _Materialization(
        Guid cronId,
        (DateTime ReconciledThroughUtc, DateTime NextDueUtc) seeded
    ) =>
        new()
        {
            Advance = new CronScheduleAdvance
            {
                CronJobId = cronId,
                ObservedReconciledThroughUtc = seeded.ReconciledThroughUtc,
                ExpectedScheduleRevision = 0L,
                ReconciledThroughUtc = seeded.NextDueUtc,
                NextDueUtc = seeded.NextDueUtc.AddHours(1),
                RequireProjectionDue = true,
            },
            ExecutionTimeUtc = seeded.NextDueUtc,
        };

    private static CronRecoveryRequest _Recovery(
        Guid cronId,
        (DateTime ReconciledThroughUtc, DateTime NextDueUtc) seeded
    ) =>
        new()
        {
            CronJobId = cronId,
            ObservedReconciledThroughUtc = seeded.ReconciledThroughUtc,
            ExpectedScheduleRevision = 0L,
            RecoveredThroughUtc = seeded.NextDueUtc.AddHours(2),
            NextDueUtc = seeded.NextDueUtc.AddHours(3),
            BoundedProgressThroughUtc = seeded.NextDueUtc.AddHours(2),
            NextDueAfterBoundedProgressUtc = seeded.NextDueUtc.AddHours(3),
            EvaluationSaturated = false,
            Policy = MissedRunPolicy.Coalesce,
            EarliestMissedUtc = seeded.NextDueUtc,
            MissedInstantsUtc = [seeded.NextDueUtc],
            CoalescedOccurrenceId = Guid.NewGuid(),
            OnNodeDeath = NodeDeathPolicy.Retry,
            OperationTimeUtc = DateTimeOffset.UtcNow,
        };

    private static IJobPersistenceProvider<TimeJobEntity, CronJobEntity> _Persistence(IHost host) =>
        host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
}
