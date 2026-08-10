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
/// Cross-provider conformance for misfire recovery. The in-memory suite proves the same scenarios, but it cannot
/// prove them <i>for</i> a relational backend: recovery resolves occurrences and moves the watermark inside one
/// transaction, and whether that composition holds is a property of the store, not of the shared contract.
/// </summary>
/// <remarks>
/// The invariant every scenario shares: recovery never leaves two live occurrences for one instant, and never
/// executes an instant twice.
/// </remarks>
public abstract class JobsRecoveryConformanceTests<TFixture>(TFixture fixture) : TestBase
    where TFixture : class, IJobsCoordinationFixture
{
    /// <summary>AE1: coalesce over a multi-occurrence outage produces one run reporting the earliest missed instant.</summary>
    public virtual async Task coalesce_materializes_one_run_stamped_with_the_earliest_missed_instant()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost("recovery-coalesce");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        var persistence = _Persistence(host);

        // Reconciled through an hour ago; recovery runs now. An hourly schedule is therefore behind by several ticks.
        var cronId = Guid.NewGuid();
        await fixture.SeedCronJobAsync(
            cronId,
            "recovery-coalesce",
            "0 0 * * * *",
            NodeDeathPolicy.Retry,
            ct,
            reconciledThroughOffsetSeconds: -14400,
            nextDueOffsetSeconds: -10800
        );
        var seeded = await fixture.ReadCronSchedulePositionAsync(cronId, ct);

        var result = await persistence.ApplyCronRecoveryAsync(_Request(cronId, seeded, MissedRunPolicy.Coalesce), ct);

        result.Should().NotBeNull();
        result!.CoalescedRun.Should().NotBeNull();
        result
            .CoalescedRun!.ExecutionTime.Should()
            .BeCloseTo(seeded.NextDueUtc, TimeSpan.FromMicroseconds(1), "the run reports the earliest missed instant");
        result
            .CoalescedRun.RecoveredFromUtc.Should()
            .NotBeNull("the marker must be durable — the watermark has already moved past the backlog");
        result.CoalescedRun.IsRecoveryRun.Should().BeTrue();

        var occurrences = await persistence.GetAllCronJobOccurrencesAsync(x => x.CronJobId == cronId, ct);
        occurrences.Should().ContainSingle("coalesce materializes one run however many were missed");
        occurrences[0].Id.Should().Be(result.CoalescedRun.Id);
        occurrences[0].Status.Should().Be(JobStatus.Idle, "the one preserved recovery run remains claimable");
        result.SkippedOccurrenceCount.Should().Be(0, "the preserved recovery run is not part of residual cleanup");

        var position = await fixture.ReadCronSchedulePositionAsync(cronId, ct);
        position
            .ReconciledThroughUtc.Should()
            .BeCloseTo(_RecoveryInstant(seeded), TimeSpan.FromMicroseconds(1), "the backlog is never reconsidered");
    }

    /// <summary>AE2: skip produces no run and still carries the watermark to the recovery instant.</summary>
    public virtual async Task skip_materializes_nothing_and_still_advances_the_watermark()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost("recovery-skip");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        var persistence = _Persistence(host);

        var cronId = Guid.NewGuid();
        await fixture.SeedCronJobAsync(
            cronId,
            "recovery-skip",
            "0 0 * * * *",
            NodeDeathPolicy.Retry,
            ct,
            reconciledThroughOffsetSeconds: -14400,
            nextDueOffsetSeconds: -10800
        );
        var seeded = await fixture.ReadCronSchedulePositionAsync(cronId, ct);

        var result = await persistence.ApplyCronRecoveryAsync(_Request(cronId, seeded, MissedRunPolicy.Skip), ct);

        result.Should().NotBeNull();
        result!.CoalescedRun.Should().BeNull();
        (await persistence.GetAllCronJobOccurrencesAsync(x => x.CronJobId == cronId, ct)).Should().BeEmpty();

        var position = await fixture.ReadCronSchedulePositionAsync(cronId, ct);
        position.ReconciledThroughUtc.Should().BeCloseTo(_RecoveryInstant(seeded), TimeSpan.FromMicroseconds(1));
    }

    /// <summary>AE15: a queued occurrence repurposed by coalesce has its ownership revoked and carries the stamp.</summary>
    public virtual async Task coalesce_repurposes_a_queued_occurrence_and_revokes_ownership()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost("recovery-repurpose");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        var persistence = _Persistence(host);

        var cronId = Guid.NewGuid();
        await fixture.SeedCronJobAsync(
            cronId,
            "recovery-repurpose",
            "0 0 * * * *",
            NodeDeathPolicy.Retry,
            ct,
            reconciledThroughOffsetSeconds: -14400,
            nextDueOffsetSeconds: -10800
        );
        var seeded = await fixture.ReadCronSchedulePositionAsync(cronId, ct);

        // A row already claimed and queued by another node, but not yet executing.
        var occurrenceId = Guid.NewGuid();
        await fixture.SeedCronOccurrenceAsync(
            occurrenceId,
            cronId,
            (int)JobStatus.Queued,
            "node-b@1",
            NodeDeathPolicy.Retry,
            DateTime.UtcNow.AddMinutes(5),
            seeded.NextDueUtc,
            ct
        );

        var result = await persistence.ApplyCronRecoveryAsync(_Request(cronId, seeded, MissedRunPolicy.Coalesce), ct);

        result!.CoalescedRun.Should().NotBeNull();
        result.CoalescedRun!.Id.Should().Be(occurrenceId, "the existing row is reused, not duplicated");

        var stored = (await persistence.GetAllCronJobOccurrencesAsync(x => x.CronJobId == cronId, ct)).Single();
        stored
            .OwnerId.Should()
            .BeNull(
                "the claim path's in-progress transition requires OwnerId == owner, so revoking it is what makes the "
                    + "prior owner drop the row"
            );
        stored.LockedUntil.Should().BeNull();
        stored.RecoveredFromUtc.Should().NotBeNull();
        stored.Status.Should().Be(JobStatus.Idle);
    }

    /// <summary>
    /// R18 over an occupied earliest instant: an executing or terminal row at the earliest missed instant accounts
    /// for that instant only. The rest of the backlog still gets its one run, materialized at the next
    /// unaccounted-for missed instant — not abandoned wholesale.
    /// </summary>
    public virtual async Task coalesce_steps_past_an_occupied_earliest_instant_to_the_next_missed_instant()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost("recovery-step-past");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        var persistence = _Persistence(host);

        var cronId = Guid.NewGuid();
        await fixture.SeedCronJobAsync(
            cronId,
            "recovery-step-past",
            "0 0 * * * *",
            NodeDeathPolicy.Retry,
            ct,
            reconciledThroughOffsetSeconds: -14400,
            nextDueOffsetSeconds: -10800
        );
        var seeded = await fixture.ReadCronSchedulePositionAsync(cronId, ct);
        var secondMissed = seeded.NextDueUtc.AddHours(1);

        // The earliest missed instant already ran to completion (a resume-created row the fallback picked up).
        var finishedId = Guid.NewGuid();
        await fixture.SeedCronOccurrenceAsync(
            finishedId,
            cronId,
            (int)JobStatus.Succeeded,
            ownerId: null,
            NodeDeathPolicy.Retry,
            lockedUntil: null,
            seeded.NextDueUtc,
            ct
        );

        var result = await persistence.ApplyCronRecoveryAsync(
            _Request(cronId, seeded, MissedRunPolicy.Coalesce, [seeded.NextDueUtc, secondMissed]),
            ct
        );

        result!.CoalescedRun.Should().NotBeNull("the later tick was genuinely missed even though the earliest ran");
        result.CoalescedRun!.ExecutionTime.Should().BeCloseTo(secondMissed, TimeSpan.FromMicroseconds(1));
        result.CoalescedRun.RecoveredFromUtc.Should().NotBeNull();
        result
            .CoalescedRun.RecoveredFromUtc!.Value.Should()
            .BeCloseTo(secondMissed, TimeSpan.FromMicroseconds(1), "the run stands for the first unaccounted instant");

        var rows = await persistence.GetAllCronJobOccurrencesAsync(x => x.CronJobId == cronId, ct);
        rows.Should().HaveCount(2);
        rows.Single(x => x.Id == finishedId).Status.Should().Be(JobStatus.Succeeded, "left undisturbed");
    }

    public virtual async Task saturated_coalesce_preserves_an_unexamined_idle_n_plus_one_for_the_next_pass()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost("recovery-saturated-prefix");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        var persistence = _Persistence(host);
        var cronId = Guid.NewGuid();
        await fixture.SeedCronJobAsync(
            cronId,
            "recovery-saturated-prefix",
            "0 0 * * * *",
            NodeDeathPolicy.Retry,
            ct,
            reconciledThroughOffsetSeconds: -14400,
            nextDueOffsetSeconds: -10800
        );
        var seeded = await fixture.ReadCronSchedulePositionAsync(cronId, ct);
        var second = seeded.NextDueUtc.AddHours(1);
        var nPlusOne = seeded.NextDueUtc.AddHours(2);
        var terminalIds = new[] { Guid.NewGuid(), Guid.NewGuid() };
        await fixture.SeedCronOccurrenceAsync(
            terminalIds[0],
            cronId,
            (int)JobStatus.Succeeded,
            null,
            NodeDeathPolicy.Retry,
            null,
            seeded.NextDueUtc,
            ct
        );
        await fixture.SeedCronOccurrenceAsync(
            terminalIds[1],
            cronId,
            (int)JobStatus.Succeeded,
            null,
            NodeDeathPolicy.Retry,
            null,
            second,
            ct
        );
        var nPlusOneId = Guid.NewGuid();
        await fixture.SeedCronOccurrenceAsync(
            nPlusOneId,
            cronId,
            (int)JobStatus.Idle,
            null,
            NodeDeathPolicy.Retry,
            null,
            nPlusOne,
            ct
        );

        var first = await persistence.ApplyCronRecoveryAsync(
            _Request(cronId, seeded, MissedRunPolicy.Coalesce, [seeded.NextDueUtc, second]) with
            {
                EvaluationSaturated = true,
                BoundedProgressThroughUtc = second,
                NextDueAfterBoundedProgressUtc = nPlusOne,
            },
            ct
        );

        first!.CoalescedRun.Should().BeNull();
        first.ReconciledThroughUtc.Should().BeCloseTo(second, TimeSpan.FromMicroseconds(1));
        (await persistence.GetAllCronJobOccurrencesAsync(x => x.CronJobId == cronId, ct))
            .Single(x => x.Id == nPlusOneId)
            .Status.Should()
            .Be(JobStatus.Idle);

        var secondPass = await persistence.ApplyCronRecoveryAsync(
            _Request(cronId, seeded, MissedRunPolicy.Coalesce, [nPlusOne]) with
            {
                ObservedReconciledThroughUtc = second,
                EarliestMissedUtc = nPlusOne,
            },
            ct
        );

        secondPass!.CoalescedRun.Should().NotBeNull();
        secondPass.CoalescedRun!.Id.Should().Be(nPlusOneId);
    }

    public virtual async Task fingerprint_keyset_progress_survives_provider_recreation()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        Guid? cursor;
        Guid? highWatermark;
        var ids = new[]
        {
            new Guid("00000001-0000-0000-0000-000000000000"),
            new Guid("00000002-0000-0000-0000-000000000000"),
            new Guid("00000003-0000-0000-0000-000000000000"),
        };

        using (var firstHost = fixture.BuildHost("fingerprint-restart-a"))
        {
            await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(firstHost, ct);
            var firstProvider = _Persistence(firstHost);
            await firstProvider.InsertCronJobsAsync(ids.Select(_StaleDefinition).ToArray(), ct);
            var first = await firstProvider.GetStaleFingerprintDefinitionsAsync(
                new CronFingerprintSweepRequest { CurrentFingerprints = ["current"], Limit = 1 },
                ct
            );
            first.Candidates.Should().ContainSingle().Which.CronJobId.Should().Be(ids[0]);
            cursor = first.Candidates[0].CronJobId;
            highWatermark = first.SnapshotHighWatermarkId;
            (
                await firstProvider.DeferStaleFingerprintDefinitionAsync(
                    new CronFingerprintDeferRequest
                    {
                        CronJobId = ids[0],
                        ExpectedScheduleRevision = 0,
                        ObservedReconciledThroughUtc = DateTime.UnixEpoch,
                        ObservedEvaluationFingerprint = "stale",
                        InitialDelay = TimeSpan.FromHours(1),
                        MaximumDelay = TimeSpan.FromHours(24),
                    },
                    ct
                )
            ).Should().BeTrue();
        }

        using var secondHost = fixture.BuildHost("fingerprint-restart-b");
        var secondProvider = _Persistence(secondHost);
        var second = await secondProvider.GetStaleFingerprintDefinitionsAsync(
            new CronFingerprintSweepRequest
            {
                CurrentFingerprints = ["current"],
                Limit = 2,
                AfterId = cursor,
                ThroughId = highWatermark,
                AllowWrap = true,
            },
            ct
        );
        second.Candidates.Select(x => x.CronJobId).Should().Equal(ids[1], ids[2]);
    }

    public virtual async Task fingerprint_wrap_returns_low_id_after_exactly_full_forward_page()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost("fingerprint-full-forward-wrap");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        var persistence = _Persistence(host);
        var ids = new[]
        {
            new Guid("00000001-0000-0000-0000-000000000000"),
            new Guid("00000003-0000-0000-0000-000000000000"),
            new Guid("00000004-0000-0000-0000-000000000000"),
        };
        await persistence.InsertCronJobsAsync(ids.Select(_StaleDefinition).ToArray(), ct);

        var first = await persistence.GetStaleFingerprintDefinitionsAsync(
            new CronFingerprintSweepRequest
            {
                CurrentFingerprints = ["current"],
                Limit = 2,
                AfterId = new Guid("00000002-0000-0000-0000-000000000000"),
                ThroughId = ids[2],
                AllowWrap = true,
            },
            ct
        );

        first.Candidates.Select(x => x.CronJobId).Should().Equal(ids[1], ids[2]);
        first.HasMore.Should().BeTrue();
        first.Wrapped.Should().BeFalse("the page had capacity only to probe the wrapped range");

        foreach (var candidate in first.Candidates)
        {
            (
                await persistence.DeferStaleFingerprintDefinitionAsync(
                    new CronFingerprintDeferRequest
                    {
                        CronJobId = candidate.CronJobId,
                        ExpectedScheduleRevision = candidate.ScheduleRevision,
                        ObservedReconciledThroughUtc = candidate.ReconciledThroughUtc,
                        ObservedEvaluationFingerprint = candidate.EvaluationFingerprint,
                        InitialDelay = TimeSpan.FromHours(1),
                        MaximumDelay = TimeSpan.FromHours(24),
                    },
                    ct
                )
            ).Should().BeTrue();
        }

        var second = await persistence.GetStaleFingerprintDefinitionsAsync(
            new CronFingerprintSweepRequest
            {
                CurrentFingerprints = ["current"],
                Limit = 2,
                AfterId = first.Candidates[^1].CronJobId,
                ThroughId = first.SnapshotHighWatermarkId,
                AllowWrap = true,
            },
            ct
        );

        second.Candidates.Should().ContainSingle().Which.CronJobId.Should().Be(ids[0]);
        second.Wrapped.Should().BeTrue();
        second.HasMore.Should().BeFalse();
    }

    private static CronJobEntity _StaleDefinition(Guid id) =>
        new()
        {
            Id = id,
            Function = $"fingerprint-{id:N}",
            Expression = "0 * * * * *",
            ReconciledThroughUtc = DateTime.UnixEpoch,
            NextDueUtc = DateTime.UnixEpoch.AddMinutes(1),
            EvaluationFingerprint = "stale",
            OnMissedRun = MissedRunPolicy.Coalesce,
            MissedRunGraceSeconds = 60,
            Request = [],
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

    /// <summary>AE9: an occurrence another node is executing is left alone and its instant is not duplicated.</summary>
    public virtual async Task recovery_leaves_an_executing_occurrence_untouched()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost("recovery-inflight");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        var persistence = _Persistence(host);

        var cronId = Guid.NewGuid();
        await fixture.SeedCronJobAsync(
            cronId,
            "recovery-inflight",
            "0 0 * * * *",
            NodeDeathPolicy.Retry,
            ct,
            reconciledThroughOffsetSeconds: -14400,
            nextDueOffsetSeconds: -10800
        );
        var seeded = await fixture.ReadCronSchedulePositionAsync(cronId, ct);

        var occurrenceId = Guid.NewGuid();
        await fixture.SeedCronOccurrenceAsync(
            occurrenceId,
            cronId,
            (int)JobStatus.InProgress,
            "node-b@1",
            NodeDeathPolicy.Retry,
            DateTime.UtcNow.AddMinutes(5),
            seeded.NextDueUtc,
            ct
        );

        var result = await persistence.ApplyCronRecoveryAsync(_Request(cronId, seeded, MissedRunPolicy.Coalesce), ct);

        result!.CoalescedRun.Should().BeNull("a second run at that instant would duplicate work already in flight");

        var stored = (await persistence.GetAllCronJobOccurrencesAsync(x => x.CronJobId == cronId, ct)).Single();
        stored.Status.Should().Be(JobStatus.InProgress, "running work is never disturbed by recovery");
        stored.OwnerId.Should().Be("node-b@1");

        var position = await fixture.ReadCronSchedulePositionAsync(cronId, ct);
        position
            .ReconciledThroughUtc.Should()
            .BeCloseTo(_RecoveryInstant(seeded), TimeSpan.FromMicroseconds(1), "the watermark still advances past it");
    }

    /// <summary>AE10: an occurrence that already completed is not executed a second time.</summary>
    public virtual async Task recovery_does_not_re_execute_a_completed_occurrence()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost("recovery-completed");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        var persistence = _Persistence(host);

        var cronId = Guid.NewGuid();
        await fixture.SeedCronJobAsync(
            cronId,
            "recovery-completed",
            "0 0 * * * *",
            NodeDeathPolicy.Retry,
            ct,
            reconciledThroughOffsetSeconds: -14400,
            nextDueOffsetSeconds: -10800
        );
        var seeded = await fixture.ReadCronSchedulePositionAsync(cronId, ct);

        // Terminal, so the filtered uniqueness index no longer covers it — only R7 prevents a re-run.
        await fixture.SeedCronOccurrenceAsync(
            Guid.NewGuid(),
            cronId,
            (int)JobStatus.Succeeded,
            ownerId: null,
            NodeDeathPolicy.Retry,
            lockedUntil: null,
            seeded.NextDueUtc,
            ct
        );

        var result = await persistence.ApplyCronRecoveryAsync(_Request(cronId, seeded, MissedRunPolicy.Coalesce), ct);

        result!.CoalescedRun.Should().BeNull("that instant already ran");
        (await persistence.GetAllCronJobOccurrencesAsync(x => x.CronJobId == cronId, ct))
            .Should()
            .ContainSingle()
            .Which.Status.Should()
            .Be(JobStatus.Succeeded);
    }

    public virtual async Task direct_claim_preserves_the_recovery_stamp()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost("recovery-direct-claim");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        await host.StartAsync(ct);
        var persistence = _Persistence(host);

        var cronId = Guid.NewGuid();
        await fixture.SeedCronJobAsync(
            cronId,
            "recovery-direct-claim",
            "0 0 * * * *",
            NodeDeathPolicy.Retry,
            ct,
            reconciledThroughOffsetSeconds: -14400,
            nextDueOffsetSeconds: -10800
        );
        var seeded = await fixture.ReadCronSchedulePositionAsync(cronId, ct);
        var recovery = await persistence.ApplyCronRecoveryAsync(_Request(cronId, seeded, MissedRunPolicy.Coalesce), ct);
        var coalesced = recovery!.CoalescedRun!;
        var dispatch = new JobManagerDispatchContext(cronId)
        {
            FunctionName = "recovery-direct-claim",
            Expression = "0 0 * * * *",
            NextCronOccurrence = new NextCronOccurrence(coalesced.Id, coalesced.CreatedAt),
        };

        var claimed = await persistence
            .QueueCronJobOccurrencesAsync((coalesced.ExecutionTime, [dispatch]), ct)
            .ToArrayAsync(ct);

        var occurrence = claimed.Should().ContainSingle().Which;
        occurrence.Id.Should().Be(coalesced.Id);
        occurrence
            .RecoveredFromUtc.Should()
            .BeCloseTo(seeded.NextDueUtc, TimeSpan.FromMicroseconds(1), "the direct claim projection must carry it");
        await host.StopAsync(ct);
    }

    public virtual async Task fallback_claim_preserves_the_recovery_stamp()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost("recovery-fallback-claim");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        await host.StartAsync(ct);
        var persistence = _Persistence(host);

        var cronId = Guid.NewGuid();
        await fixture.SeedCronJobAsync(
            cronId,
            "recovery-fallback-claim",
            "0 0 * * * *",
            NodeDeathPolicy.Retry,
            ct,
            reconciledThroughOffsetSeconds: -14400,
            nextDueOffsetSeconds: -10800
        );
        var seeded = await fixture.ReadCronSchedulePositionAsync(cronId, ct);
        var recovery = await persistence.ApplyCronRecoveryAsync(_Request(cronId, seeded, MissedRunPolicy.Coalesce), ct);

        var claimed = await persistence.QueueTimedOutCronJobOccurrencesAsync(ct).ToArrayAsync(ct);

        var occurrence = claimed.Should().ContainSingle().Which;
        occurrence.Id.Should().Be(recovery!.CoalescedRun!.Id);
        occurrence
            .RecoveredFromUtc.Should()
            .BeCloseTo(
                seeded.NextDueUtc,
                TimeSpan.FromMicroseconds(1),
                "the restart/fallback projection must carry the durable stamp"
            );
        await host.StopAsync(ct);
    }

    public virtual async Task concurrent_recovery_of_one_backlog_produces_exactly_one_winner()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost("recovery-race");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        var persistence = _Persistence(host);

        var cronId = Guid.NewGuid();
        await fixture.SeedCronJobAsync(
            cronId,
            "recovery-race",
            "0 0 * * * *",
            NodeDeathPolicy.Retry,
            ct,
            reconciledThroughOffsetSeconds: -14400,
            nextDueOffsetSeconds: -10800
        );
        var seeded = await fixture.ReadCronSchedulePositionAsync(cronId, ct);

        // Every racer submits the SAME observed watermark, as N nodes waking together on one backlog would. Each needs
        // its own occurrence id: two winners writing the same id would look like one winner and hide the defect.
        var results = await Task.WhenAll(
            Enumerable
                .Range(0, 6)
                .Select(async _ =>
                {
                    await Task.Yield();
                    return await persistence.ApplyCronRecoveryAsync(
                        _Request(cronId, seeded, MissedRunPolicy.Coalesce),
                        ct
                    );
                })
        );

        results
            .Count(x => x is not null)
            .Should()
            .Be(1, "the watermark fence must serialize concurrent recoveries down to a single winner");
        (await persistence.GetAllCronJobOccurrencesAsync(x => x.CronJobId == cronId, ct))
            .Should()
            .ContainSingle("a losing racer must not materialize a second run for the same backlog");
    }

    private static DateTime _RecoveryInstant((DateTime ReconciledThroughUtc, DateTime NextDueUtc) seeded) =>
        seeded.NextDueUtc.AddHours(2);

    private static CronRecoveryRequest _Request(
        Guid cronJobId,
        (DateTime ReconciledThroughUtc, DateTime NextDueUtc) seeded,
        MissedRunPolicy policy,
        DateTime[]? missedInstants = null
    ) =>
        new()
        {
            CronJobId = cronJobId,
            ObservedReconciledThroughUtc = seeded.ReconciledThroughUtc,
            ExpectedScheduleRevision = 0L,
            RecoveredThroughUtc = _RecoveryInstant(seeded),
            NextDueUtc = _RecoveryInstant(seeded).AddHours(1),
            BoundedProgressThroughUtc = _RecoveryInstant(seeded),
            NextDueAfterBoundedProgressUtc = _RecoveryInstant(seeded).AddHours(1),
            EvaluationSaturated = false,
            Policy = policy,
            EarliestMissedUtc = seeded.NextDueUtc,
            MissedInstantsUtc = missedInstants ?? [seeded.NextDueUtc],
            CoalescedOccurrenceId = Guid.NewGuid(),
            OnNodeDeath = NodeDeathPolicy.Retry,
            OperationTimeUtc = DateTimeOffset.UtcNow,
        };

    private static IJobPersistenceProvider<TimeJobEntity, CronJobEntity> _Persistence(IHost host) =>
        host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
}
