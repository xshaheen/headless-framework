// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Jobs;
using Headless.Jobs.DbContextFactory;
using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Interfaces.Managers;
using Headless.Jobs.Models;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests;

#pragma warning disable CA1707 // Test names follow the repo's readable snake_case convention.

/// <summary>
/// Provider-neutral conformance for the cron schedule-position advance — the compare-and-advance through which a
/// definition's watermark and dispatch projection move. Every scenario here must hold identically on each relational
/// backend, because the scheduler's correctness rests on exactly one property: concurrent nodes advancing from the
/// same observed watermark produce exactly one winner, and a node holding a stale definition cannot advance at all.
/// </summary>
/// <remarks>
/// These are outcome tests. The complementary guard on the SQL itself lives in
/// <see cref="JobsDatabaseClockConformanceTests{TFixture}.schedule_advance_sql_is_owned_by_the_database_clock"/>,
/// and it is the decisive one for clock ownership: a regression to a client-evaluated clock ignores
/// <c>TimeProvider</c> altogether and would slip past the injected skew exercised below. The skew test proves the
/// observable outcome; the SQL capture proves the mechanism. Neither alone is sufficient.
/// <para>
/// No <c>StartAsync</c> for the advance and materialize scenarios: those are direct persistence calls that take no
/// ownership and stamp no lease, so they must not depend on membership or on a skewed clock driving them. The
/// rationale does NOT extend to the claim path — it does take ownership, and a claim scenario run against an
/// unstarted host returns empty at its membership gate before reaching anything the test means to exercise. Claim
/// scenarios here start the host.
/// </para>
/// </remarks>
public abstract class JobsSchedulePositionConformanceTests<TFixture>(TFixture fixture) : TestBase
    where TFixture : class, IJobsCoordinationFixture
{
    public virtual async Task advance_from_the_observed_watermark_persists_the_new_position()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost("advance-happy");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        var persistence = _Persistence(host);

        var cronId = Guid.NewGuid();
        await fixture.SeedCronJobAsync(cronId, "advance-happy", "* * * * *", NodeDeathPolicy.Retry, ct);
        var seeded = await fixture.ReadCronSchedulePositionAsync(cronId, ct);
        var newProjection = seeded.NextDueUtc.AddMinutes(1);

        var result = await persistence.AdvanceCronScheduleAsync(
            new CronScheduleAdvance
            {
                CronJobId = cronId,
                ObservedReconciledThroughUtc = seeded.ReconciledThroughUtc,
                ExpectedScheduleRevision = 0L,
                ReconciledThroughUtc = seeded.NextDueUtc,
                NextDueUtc = newProjection,
            },
            ct
        );

        result.Should().NotBeNull();

        // Bounded rather than exact: PostgreSQL materializes at microsecond granularity while SQL Server datetime2(7)
        // keeps ticks, so a round-tripped instant is only ever equal to within the column's precision.
        result!.ReconciledThroughUtc.Should().BeCloseTo(seeded.NextDueUtc, TimeSpan.FromMicroseconds(1));
        result.NextDueUtc.Should().BeCloseTo(newProjection, TimeSpan.FromMicroseconds(1));

        var persisted = await fixture.ReadCronSchedulePositionAsync(cronId, ct);
        persisted.ReconciledThroughUtc.Should().BeCloseTo(seeded.NextDueUtc, TimeSpan.FromMicroseconds(1));
        persisted.NextDueUtc.Should().BeCloseTo(newProjection, TimeSpan.FromMicroseconds(1));

        // The returned instant is the store's, so it must sit near real time — this is what recovery later compares
        // the watermark against.
        result.StoreUtcNow.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
    }

    public virtual async Task advance_with_a_stale_watermark_changes_nothing()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost("advance-stale-watermark");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        var persistence = _Persistence(host);

        var cronId = Guid.NewGuid();
        await fixture.SeedCronJobAsync(cronId, "advance-stale-watermark", "* * * * *", NodeDeathPolicy.Retry, ct);
        var seeded = await fixture.ReadCronSchedulePositionAsync(cronId, ct);

        // A watermark the definition never held: this is the losing racer's view after someone else advanced.
        var result = await persistence.AdvanceCronScheduleAsync(
            new CronScheduleAdvance
            {
                CronJobId = cronId,
                ObservedReconciledThroughUtc = seeded.ReconciledThroughUtc.AddSeconds(-30),
                ExpectedScheduleRevision = 0L,
                ReconciledThroughUtc = seeded.NextDueUtc,
                NextDueUtc = seeded.NextDueUtc.AddMinutes(1),
            },
            ct
        );

        result.Should().BeNull("losing the watermark fence is reported by a null result, never by an exception");
        await _AssertPositionUnchangedAsync(cronId, seeded, ct);
    }

    public virtual async Task advance_with_a_stale_schedule_revision_changes_nothing()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost("advance-stale-revision");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        var persistence = _Persistence(host);

        var cronId = Guid.NewGuid();
        await fixture.SeedCronJobAsync(
            cronId,
            "advance-stale-revision",
            "* * * * *",
            NodeDeathPolicy.Retry,
            ct,
            scheduleRevision: 7L
        );
        var seeded = await fixture.ReadCronSchedulePositionAsync(cronId, ct);

        var result = await persistence.AdvanceCronScheduleAsync(
            new CronScheduleAdvance
            {
                CronJobId = cronId,
                ObservedReconciledThroughUtc = seeded.ReconciledThroughUtc,
                ExpectedScheduleRevision = 6L,
                ReconciledThroughUtc = seeded.NextDueUtc,
                NextDueUtc = seeded.NextDueUtc.AddMinutes(1),
            },
            ct
        );

        result
            .Should()
            .BeNull("a node holding a superseded definition snapshot must not apply a position derived from it");
        await _AssertPositionUnchangedAsync(cronId, seeded, ct);
    }

    public virtual async Task advance_against_a_paused_definition_changes_nothing()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost("advance-paused");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        var persistence = _Persistence(host);

        var cronId = Guid.NewGuid();
        await fixture.SeedCronJobAsync(
            cronId,
            "advance-paused",
            "* * * * *",
            NodeDeathPolicy.Retry,
            ct,
            isPaused: true,
            nextDueOffsetSeconds: -300
        );
        var seeded = await fixture.ReadCronSchedulePositionAsync(cronId, ct);

        var result = await persistence.AdvanceCronScheduleAsync(
            new CronScheduleAdvance
            {
                CronJobId = cronId,
                ObservedReconciledThroughUtc = seeded.ReconciledThroughUtc,
                ExpectedScheduleRevision = 0L,
                ReconciledThroughUtc = seeded.NextDueUtc,
                NextDueUtc = seeded.NextDueUtc.AddMinutes(1),
            },
            ct
        );

        result.Should().BeNull("a paused definition must not advance even when its projection is long past due");
        await _AssertPositionUnchangedAsync(cronId, seeded, ct);
    }

    public virtual async Task concurrent_advances_from_the_same_watermark_produce_exactly_one_winner()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost("advance-race");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        var persistence = _Persistence(host);

        var cronId = Guid.NewGuid();
        await fixture.SeedCronJobAsync(cronId, "advance-race", "* * * * *", NodeDeathPolicy.Retry, ct);
        var seeded = await fixture.ReadCronSchedulePositionAsync(cronId, ct);

        // Every racer submits the SAME observed watermark and the SAME target, exactly as N scheduler nodes waking
        // together would. Racing them concurrently is the point — sequential calls would prove nothing about the fence.
        const int racers = 8;
        var advance = new CronScheduleAdvance
        {
            CronJobId = cronId,
            ObservedReconciledThroughUtc = seeded.ReconciledThroughUtc,
            ExpectedScheduleRevision = 0L,
            ReconciledThroughUtc = seeded.NextDueUtc,
            NextDueUtc = seeded.NextDueUtc.AddMinutes(1),
        };

        var results = await Task.WhenAll(
            Enumerable
                .Range(0, racers)
                .Select(async _ =>
                {
                    await Task.Yield();
                    return await persistence.AdvanceCronScheduleAsync(advance, ct);
                })
        );

        results
            .Count(result => result is not null)
            .Should()
            .Be(1, "the watermark compare-and-advance must serialize concurrent nodes down to a single winner");
    }

    public virtual async Task advancing_one_definition_leaves_a_sibling_untouched()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost("advance-sibling");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        var persistence = _Persistence(host);

        var advancedId = Guid.NewGuid();
        var siblingId = Guid.NewGuid();
        await fixture.SeedCronJobAsync(advancedId, "advance-sibling-a", "* * * * *", NodeDeathPolicy.Retry, ct);
        await fixture.SeedCronJobAsync(siblingId, "advance-sibling-b", "* * * * *", NodeDeathPolicy.Retry, ct);
        var advancedBefore = await fixture.ReadCronSchedulePositionAsync(advancedId, ct);
        var siblingBefore = await fixture.ReadCronSchedulePositionAsync(siblingId, ct);

        var result = await persistence.AdvanceCronScheduleAsync(
            new CronScheduleAdvance
            {
                CronJobId = advancedId,
                ObservedReconciledThroughUtc = advancedBefore.ReconciledThroughUtc,
                ExpectedScheduleRevision = 0L,
                ReconciledThroughUtc = advancedBefore.NextDueUtc,
                NextDueUtc = advancedBefore.NextDueUtc.AddMinutes(1),
            },
            ct
        );

        result.Should().NotBeNull();
        await _AssertPositionUnchangedAsync(siblingId, siblingBefore, ct);
    }

    /// <summary>
    /// Due-ness belongs to the store. A node whose wall clock runs two hours fast must not advance a definition the
    /// database does not yet consider due, and must still advance one the database says is overdue — and the instant it
    /// is handed back must be the store's, not its own.
    /// </summary>
    public virtual async Task due_ness_and_the_returned_instant_follow_the_database_clock_not_a_skewed_node_clock()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);

        var skew = TimeSpan.FromHours(2);
        using var host = fixture.BuildHost("advance-skew", timeProvider: new SkewedTimeProvider(skew));
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        var persistence = _Persistence(host);

        // Due in 30 minutes per the STORE. The +2h node thinks this is long past due; the store must refuse it.
        var notYetDueId = Guid.NewGuid();
        await fixture.SeedCronJobAsync(
            notYetDueId,
            "advance-skew-future",
            "* * * * *",
            NodeDeathPolicy.Retry,
            ct,
            nextDueOffsetSeconds: 1800
        );
        var notYetDue = await fixture.ReadCronSchedulePositionAsync(notYetDueId, ct);

        var refused = await persistence.AdvanceCronScheduleAsync(
            new CronScheduleAdvance
            {
                CronJobId = notYetDueId,
                ObservedReconciledThroughUtc = notYetDue.ReconciledThroughUtc,
                ExpectedScheduleRevision = 0L,
                ReconciledThroughUtc = notYetDue.NextDueUtc,
                NextDueUtc = notYetDue.NextDueUtc.AddMinutes(1),
                RequireProjectionDue = true,
            },
            ct
        );

        refused
            .Should()
            .BeNull(
                "a node whose clock runs {0} fast must not advance a definition the database does not consider due",
                skew
            );
        await _AssertPositionUnchangedAsync(notYetDueId, notYetDue, ct);

        // Overdue by 5 minutes per the store: the same skewed node must still advance it.
        var overdueId = Guid.NewGuid();
        await fixture.SeedCronJobAsync(
            overdueId,
            "advance-skew-past",
            "* * * * *",
            NodeDeathPolicy.Retry,
            ct,
            reconciledThroughOffsetSeconds: -600,
            nextDueOffsetSeconds: -300
        );
        var overdue = await fixture.ReadCronSchedulePositionAsync(overdueId, ct);

        var accepted = await persistence.AdvanceCronScheduleAsync(
            new CronScheduleAdvance
            {
                CronJobId = overdueId,
                ObservedReconciledThroughUtc = overdue.ReconciledThroughUtc,
                ExpectedScheduleRevision = 0L,
                ReconciledThroughUtc = overdue.NextDueUtc,
                NextDueUtc = overdue.NextDueUtc.AddMinutes(1),
                RequireProjectionDue = true,
            },
            ct
        );

        accepted.Should().NotBeNull("the database considers this definition overdue, so the advance must succeed");

        // The decisive outcome assertion: had the store instant come from this node, it would be ~2h ahead.
        accepted!
            .StoreUtcNow.Should()
            .BeCloseTo(
                DateTime.UtcNow,
                TimeSpan.FromMinutes(5),
                "the returned instant must be the store's; the node's own clock is {0} fast",
                skew
            );
        accepted
            .StoreUtcNow.Should()
            .BeBefore(
                DateTime.UtcNow.Add(skew).AddMinutes(-30),
                "the returned instant must not carry this node's skew"
            );
    }

    public virtual async Task materialization_survives_restart_and_the_idle_occurrence_is_claimed_later()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        var cronId = Guid.NewGuid();
        CronScheduleMaterializationResult materialized;

        using (var firstHost = fixture.BuildHost("materialize-before-restart"))
        {
            await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(firstHost, ct);
            var persistence = _Persistence(firstHost);
            await fixture.SeedCronJobAsync(
                cronId,
                "materialize-restart",
                "* * * * *",
                NodeDeathPolicy.Retry,
                ct,
                reconciledThroughOffsetSeconds: -600,
                nextDueOffsetSeconds: -300
            );
            var seeded = await fixture.ReadCronSchedulePositionAsync(cronId, ct);

            materialized = await persistence.MaterializeCronScheduleOccurrenceAsync(
                _Materialization(cronId, seeded),
                ct
            );

            materialized.Outcome.Should().Be(CronScheduleMaterializationOutcome.OccurrenceCreated);
            materialized.SchedulePosition.Should().NotBeNull();
            materialized.OccurrenceId.Should().NotBeNull();
            (await fixture.CountCronOccurrencesAsync(ct)).Should().Be(1);
            var (status, owner) = await fixture.ReadCronOccurrenceAsync(materialized.OccurrenceId!.Value, ct);
            status.Should().Be((int)JobStatus.Idle);
            owner.Should().BeNull("materialization must not claim or lease the occurrence");
        }

        using var restartedHost = fixture.BuildHost("materialize-after-restart");
        await restartedHost.StartAsync(ct);
        try
        {
            var persistence = _Persistence(restartedHost);
            var context = new JobManagerDispatchContext(cronId)
            {
                FunctionName = "materialize-restart",
                Expression = "* * * * *",
                ScheduleRevision = 0,
                OnNodeDeath = materialized.OnNodeDeath!.Value,
                NextCronOccurrence = new NextCronOccurrence(
                    materialized.OccurrenceId!.Value,
                    materialized.OccurrenceCreatedAt!.Value
                ),
            };

            var claimed = await persistence
                .QueueCronJobOccurrencesAsync((materialized.SchedulePosition!.ReconciledThroughUtc, [context]), ct)
                .ToArrayAsync(ct);

            claimed.Should().ContainSingle();
            claimed[0].Id.Should().Be(materialized.OccurrenceId.Value);
            var (owner, lockedUntil) = await fixture.ReadCronOccurrenceClaimAsync(claimed[0].Id, ct);
            owner.Should().NotBeNull("the restarted node must own the later claim");
            lockedUntil.Should().NotBeNull("the later claim owns the lease stamp");
        }
        finally
        {
            await restartedHost.StopAsync(ct);
        }
    }

    public virtual async Task concurrent_materializations_commit_one_position_and_one_occurrence()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost("materialize-race");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        var persistence = _Persistence(host);
        var cronId = Guid.NewGuid();
        await fixture.SeedCronJobAsync(
            cronId,
            "materialize-race",
            "* * * * *",
            NodeDeathPolicy.Retry,
            ct,
            reconciledThroughOffsetSeconds: -600,
            nextDueOffsetSeconds: -300
        );
        var seeded = await fixture.ReadCronSchedulePositionAsync(cronId, ct);
        var materialization = _Materialization(cronId, seeded);

        var results = await Task.WhenAll(
            Enumerable
                .Range(0, 8)
                .Select(async _ =>
                {
                    await Task.Yield();
                    return await persistence.MaterializeCronScheduleOccurrenceAsync(materialization, ct);
                })
        );

        results.Count(x => x.Outcome == CronScheduleMaterializationOutcome.OccurrenceCreated).Should().Be(1);
        results.Count(x => x.Outcome == CronScheduleMaterializationOutcome.LostFence).Should().Be(7);
        (await fixture.CountCronOccurrencesAsync(ct)).Should().Be(1);
    }

    public virtual async Task terminal_occurrence_is_an_explicit_committed_outcome_without_rematerialization()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost("materialize-terminal");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        var persistence = _Persistence(host);
        var cronId = Guid.NewGuid();
        await fixture.SeedCronJobAsync(
            cronId,
            "materialize-terminal",
            "* * * * *",
            NodeDeathPolicy.Retry,
            ct,
            reconciledThroughOffsetSeconds: -600,
            nextDueOffsetSeconds: -300
        );
        var seeded = await fixture.ReadCronSchedulePositionAsync(cronId, ct);
        var occurrenceId = Guid.NewGuid();
        await fixture.SeedCronOccurrenceAsync(
            occurrenceId,
            cronId,
            (int)JobStatus.Succeeded,
            ownerId: null,
            NodeDeathPolicy.Retry,
            lockedUntil: null,
            seeded.NextDueUtc,
            ct
        );

        var result = await persistence.MaterializeCronScheduleOccurrenceAsync(_Materialization(cronId, seeded), ct);

        result.Outcome.Should().Be(CronScheduleMaterializationOutcome.OccurrenceAlreadyTerminal);
        result.OccurrenceId.Should().Be(occurrenceId);
        result
            .SchedulePosition!.ReconciledThroughUtc.Should()
            .BeCloseTo(seeded.NextDueUtc, TimeSpan.FromMicroseconds(1));
        result
            .SchedulePosition.NextDueUtc.Should()
            .BeCloseTo(seeded.NextDueUtc.AddMinutes(1), TimeSpan.FromMicroseconds(1));
        (await fixture.CountCronOccurrencesAsync(ct)).Should().Be(1);
        (await fixture.ReadCronOccurrenceAsync(occurrenceId, ct)).Status.Should().Be((int)JobStatus.Succeeded);
        var persisted = await fixture.ReadCronSchedulePositionAsync(cronId, ct);
        persisted.ReconciledThroughUtc.Should().BeCloseTo(seeded.NextDueUtc, TimeSpan.FromMicroseconds(1));
        persisted.NextDueUtc.Should().BeCloseTo(seeded.NextDueUtc.AddMinutes(1), TimeSpan.FromMicroseconds(1));
    }

    public virtual async Task existing_non_terminal_occurrence_is_reused_and_position_advances()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost("materialize-existing");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        var persistence = _Persistence(host);
        var cronId = Guid.NewGuid();
        await fixture.SeedCronJobAsync(
            cronId,
            "materialize-existing",
            "* * * * *",
            NodeDeathPolicy.Retry,
            ct,
            reconciledThroughOffsetSeconds: -600,
            nextDueOffsetSeconds: -300
        );
        var seeded = await fixture.ReadCronSchedulePositionAsync(cronId, ct);
        var occurrenceId = Guid.NewGuid();
        await fixture.SeedCronOccurrenceAsync(
            occurrenceId,
            cronId,
            (int)JobStatus.Idle,
            ownerId: null,
            NodeDeathPolicy.Retry,
            lockedUntil: null,
            seeded.NextDueUtc,
            ct
        );

        var result = await persistence.MaterializeCronScheduleOccurrenceAsync(_Materialization(cronId, seeded), ct);

        result.Outcome.Should().Be(CronScheduleMaterializationOutcome.OccurrenceExists);
        result.OccurrenceId.Should().Be(occurrenceId);
        result.OccurrenceCreatedAt.Should().NotBeNull();
        result
            .SchedulePosition!.ReconciledThroughUtc.Should()
            .BeCloseTo(seeded.NextDueUtc, TimeSpan.FromMicroseconds(1));
        result
            .SchedulePosition.NextDueUtc.Should()
            .BeCloseTo(seeded.NextDueUtc.AddMinutes(1), TimeSpan.FromMicroseconds(1));
        (await fixture.CountCronOccurrencesAsync(ct)).Should().Be(1);
        var persisted = await fixture.ReadCronSchedulePositionAsync(cronId, ct);
        persisted.ReconciledThroughUtc.Should().BeCloseTo(seeded.NextDueUtc, TimeSpan.FromMicroseconds(1));
        persisted.NextDueUtc.Should().BeCloseTo(seeded.NextDueUtc.AddMinutes(1), TimeSpan.FromMicroseconds(1));
    }

    public virtual async Task failure_after_the_position_update_rolls_back_position_and_occurrence()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        var fault = new FailAfterCronPositionUpdateInterceptor();
        using var host = fixture.BuildInterceptedHost("materialize-rollback", fault);
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        var persistence = _Persistence(host);
        var cronId = Guid.NewGuid();
        await fixture.SeedCronJobAsync(
            cronId,
            "materialize-rollback",
            "* * * * *",
            NodeDeathPolicy.Retry,
            ct,
            reconciledThroughOffsetSeconds: -600,
            nextDueOffsetSeconds: -300
        );
        var seeded = await fixture.ReadCronSchedulePositionAsync(cronId, ct);
        fault.Arm();

        var action = () => persistence.MaterializeCronScheduleOccurrenceAsync(_Materialization(cronId, seeded), ct);

        await action.Should().ThrowAsync<InvalidOperationException>().WithMessage("Injected materialization failure.");
        await _AssertPositionUnchangedAsync(cronId, seeded, ct);
        (await fixture.CountCronOccurrencesAsync(ct)).Should().Be(0);
    }

    public virtual async Task stale_and_future_materializations_are_distinct_no_mutation_outcomes()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost("materialize-refused");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        var persistence = _Persistence(host);
        var staleId = Guid.NewGuid();
        var futureId = Guid.NewGuid();
        await fixture.SeedCronJobAsync(
            staleId,
            "materialize-stale",
            "* * * * *",
            NodeDeathPolicy.Retry,
            ct,
            scheduleRevision: 7,
            reconciledThroughOffsetSeconds: -600,
            nextDueOffsetSeconds: -300
        );
        await fixture.SeedCronJobAsync(
            futureId,
            "materialize-future",
            "* * * * *",
            NodeDeathPolicy.Retry,
            ct,
            reconciledThroughOffsetSeconds: 0,
            nextDueOffsetSeconds: 1800
        );
        var stale = await fixture.ReadCronSchedulePositionAsync(staleId, ct);
        var future = await fixture.ReadCronSchedulePositionAsync(futureId, ct);

        var staleResult = await persistence.MaterializeCronScheduleOccurrenceAsync(
            _Materialization(staleId, stale) with
            {
                Advance = _Materialization(staleId, stale).Advance with { ExpectedScheduleRevision = 6 },
            },
            ct
        );
        var futureResult = await persistence.MaterializeCronScheduleOccurrenceAsync(
            _Materialization(futureId, future) with
            {
                Advance = _Materialization(futureId, future).Advance with { RequireProjectionDue = false },
            },
            ct
        );

        staleResult.Outcome.Should().Be(CronScheduleMaterializationOutcome.LostFence);
        futureResult
            .Outcome.Should()
            .Be(CronScheduleMaterializationOutcome.NotDue, "materialization is intrinsically due-gated");
        (await fixture.CountCronOccurrencesAsync(ct)).Should().Be(0);
        await _AssertPositionUnchangedAsync(staleId, stale, ct);
        await _AssertPositionUnchangedAsync(futureId, future, ct);
    }

    public virtual async Task cancellation_before_materialization_changes_neither_position_nor_occurrences()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost("materialize-cancelled");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        var persistence = _Persistence(host);
        var cronId = Guid.NewGuid();
        await fixture.SeedCronJobAsync(
            cronId,
            "materialize-cancelled",
            "* * * * *",
            NodeDeathPolicy.Retry,
            ct,
            reconciledThroughOffsetSeconds: -600,
            nextDueOffsetSeconds: -300
        );
        var seeded = await fixture.ReadCronSchedulePositionAsync(cronId, ct);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var action = () =>
            persistence.MaterializeCronScheduleOccurrenceAsync(_Materialization(cronId, seeded), cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
        await _AssertPositionUnchangedAsync(cronId, seeded, ct);
        (await fixture.CountCronOccurrencesAsync(ct)).Should().Be(0);
    }

    /// <summary>
    /// AE10: an occurrence at the projection instant that already reached a terminal status is invisible to the
    /// claimable-row reuse read and no longer covered by the filtered unique index, so without an explicit guard
    /// materialization would insert a second run for an instant that already executed. The claim path must step
    /// past the occupied instant and leave the terminal row undisturbed.
    /// </summary>
    public virtual async Task queueing_an_instant_with_a_terminal_occurrence_materializes_nothing()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost("occupied-instant");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);

        // StartAsync, unlike the advance and materialize scenarios above: the claim path stamps ownership, and
        // ClaimCronJobOccurrencesAsync bails at its first statement when TryGetStampOwner finds no membership
        // identity. Without a started host this returns empty before ever reading the occupied instant, and the
        // assertion below passes for a reason unrelated to the guard it exists to hold.
        await host.StartAsync(ct);

        try
        {
            var persistence = _Persistence(host);

            var cronId = Guid.NewGuid();
            await fixture.SeedCronJobAsync(cronId, "occupied-instant", "* * * * *", NodeDeathPolicy.Retry, ct);

            // Whole seconds: PostgreSQL materializes DateTime at microsecond granularity, so a tick-precision instant
            // would miss the guard's equality predicate and the scenario would report "materialized" for a reason
            // that has nothing to do with the terminal row.
            var now = DateTime.UtcNow;
            var executionTime = new DateTime(
                now.Year,
                now.Month,
                now.Day,
                now.Hour,
                now.Minute,
                now.Second,
                DateTimeKind.Utc
            ).AddMinutes(-1);
            var occurrenceId = Guid.NewGuid();
            await fixture.SeedCronOccurrenceAsync(
                occurrenceId,
                cronId,
                (int)JobStatus.Succeeded,
                ownerId: null,
                NodeDeathPolicy.Retry,
                lockedUntil: null,
                executionTime,
                ct
            );

            var context = new JobManagerDispatchContext(cronId)
            {
                FunctionName = "occupied-instant",
                Expression = "* * * * *",
                OnNodeDeath = NodeDeathPolicy.Retry,
                NextCronOccurrence = null,
            };

            (await persistence.QueueCronJobOccurrencesAsync((executionTime, [context]), ct).ToArrayAsync(ct))
                .Should()
                .BeEmpty("the succeeded occurrence already stands for this instant — re-running it would double-fire");

            (await fixture.CountCronOccurrencesAsync(ct))
                .Should()
                .Be(1, "suppressing the fire means no second row at the instant, not merely an empty claim result");

            var (status, owner) = await fixture.ReadCronOccurrenceAsync(occurrenceId, ct);
            status.Should().Be((int)JobStatus.Succeeded, "the existing terminal row is left undisturbed");
            owner.Should().BeNull();
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    /// <summary>
    /// R10 on the attribute-driven path: a stored projection derived under the OLD expression must not survive a
    /// code-defined expression change — a yearly→minutes edit would otherwise stay dormant until the stale
    /// projection came due. Migrate resets the position to the uninitialized sentinel so the next wake re-derives
    /// it by the creation rule under the new expression.
    /// </summary>
    public virtual async Task migrate_resets_the_position_when_the_code_defined_expression_changes()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost("migrate-reset");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        var persistence = _Persistence(host);

        var cronId = Guid.NewGuid();
        await fixture.SeedCronJobAsync(cronId, "migrate-reset", "0 0 3 1 1 *", NodeDeathPolicy.Retry, ct);
        var before = await fixture.ReadCronSchedulePositionAsync(cronId, ct);
        before.NextDueUtc.Should().NotBe(default, "the seeded definition carries an initialized position");

        await persistence.MigrateDefinedCronJobsAsync(
            [
                new CronSeedDefinition(
                    "migrate-reset",
                    "0 */5 * * * *",
                    MissedRunPolicy.Coalesce,
                    JobsRecoveryDefaults.MissedRunGraceSeconds
                ),
            ],
            ct
        );

        var after = await fixture.ReadCronSchedulePositionAsync(cronId, ct);
        after
            .ReconciledThroughUtc.Should()
            .Be(default, "the stale position must be re-derived under the new expression, not kept");
        after.NextDueUtc.Should().Be(default, "the projection was derived under the old expression");
    }

    public virtual async Task dispatch_selection_excludes_definitions_with_durable_fingerprint_defer_state()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost("dispatch-defer-filter");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        var persistence = _Persistence(host);
        var now = DateTime.UtcNow;
        var deferred = new CronJobEntity
        {
            Id = Guid.NewGuid(),
            Function = "deferred",
            Expression = "0 * * * * *",
            ReconciledThroughUtc = now.AddHours(-2),
            NextDueUtc = now.AddHours(-1),
            EvaluationFingerprint = "stale",
            FingerprintFailureCount = 1,
            FingerprintRetryAfterUtc = now.AddMinutes(-1),
        };
        var healthy = new CronJobEntity
        {
            Id = Guid.NewGuid(),
            Function = "healthy",
            Expression = "0 * * * * *",
            ReconciledThroughUtc = now,
            NextDueUtc = now.AddMinutes(5),
            EvaluationFingerprint = "current",
        };
        (await persistence.InsertCronJobsAsync([deferred, healthy], ct)).Should().Be(2);

        var result = await persistence.GetEarliestCronDispatchCandidatesAsync(limit: 64, after: null, ct);

        result.Should().NotBeNull();
        result!.Candidates.Should().ContainSingle().Which.CronJobId.Should().Be(healthy.Id);
    }

    /// <summary>
    /// R6a: the candidate cursor must be applied INSIDE the query, before the limit truncates it. A provider that
    /// truncates first and drops the resumed rows afterwards passes a single-page test and still starves the caller —
    /// so this places more excluded definitions than one page holds ahead of the healthy one and asserts the healthy
    /// one is reached.
    /// </summary>
    public virtual async Task dispatch_selection_resumes_past_a_full_page_of_excluded_definitions()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost("dispatch-candidate-cursor");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        var persistence = _Persistence(host);
        var now = DateTime.UtcNow;
        const int pageSize = 8;

        // Every excluded definition sorts strictly before the healthy one, so nothing but a resumed read reaches it.
        var excluded = Enumerable
            .Range(0, pageSize * 3)
            .Select(index => new CronJobEntity
            {
                Id = Guid.NewGuid(),
                Function = "excluded",
                Expression = "0 * * * * *",
                ReconciledThroughUtc = now,
                NextDueUtc = now.AddMinutes(index + 1),
                EvaluationFingerprint = "current",
            })
            .ToArray();
        var healthy = new CronJobEntity
        {
            Id = Guid.NewGuid(),
            Function = "healthy",
            Expression = "0 * * * * *",
            ReconciledThroughUtc = now,
            NextDueUtc = now.AddDays(1),
            EvaluationFingerprint = "current",
        };
        (await persistence.InsertCronJobsAsync([.. excluded, healthy], ct)).Should().Be(excluded.Length + 1);

        CronDispatchCandidateCursor? after = null;
        var pages = 0;
        CronDispatchCandidates? page;

        do
        {
            page = await persistence.GetEarliestCronDispatchCandidatesAsync(pageSize, after, ct);
            pages++;

            if (page is not { Candidates.Count: > 0 })
            {
                break;
            }

            // The ordering key of the page's last row, exactly as the scheduler's containment loop resumes.
            var last = page.Candidates[^1];
            after = new CronDispatchCandidateCursor(last.NextDueUtc, last.CronJobId);
        } while (page.Candidates.All(x => x.CronJobId != healthy.Id) && pages <= excluded.Length);

        page.Should().NotBeNull();
        page!
            .Candidates.Select(x => x.CronJobId)
            .Should()
            .Contain(
                healthy.Id,
                "a resumed read must move past the definitions already examined instead of re-reading the same page"
            );
        pages.Should().Be(4, "three full pages of excluded definitions precede the page carrying the healthy one");
    }

    // ---------------------------------------------------------------------------------------------------------
    // #817 — the schedule position is seeded at CREATION, from the store's clock, inside the inserting transaction.
    // ---------------------------------------------------------------------------------------------------------

    /// <summary>
    /// A definition created through <c>ICronJobManager</c> is positioned by the write itself, anchored on the store's
    /// instant — not left unpositioned for whichever node sees it first to anchor at THAT moment.
    /// </summary>
    /// <remarks>
    /// The node clock is skewed years away, so an anchor derived from <c>TimeProvider</c> is unmistakable. This is the
    /// outcome half; <see cref="a_coordinated_creation_is_anchored_at_insertion_not_at_transaction_start"/> is the
    /// mechanism half, and it is the decisive one — a transaction-start clock still lands near real time and would
    /// slip past the assertions here.
    /// </remarks>
    public virtual async Task creating_a_definition_seeds_its_position_from_the_store_clock()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost(
            "seed-single",
            timeProvider: new SkewedTimeProvider(TimeSpan.FromDays(-4000))
        );
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        var manager = host.Services.GetRequiredService<ICronJobManager<CronJobEntity>>();

        var beforeInsert = await _StoreUtcNowAsync(ct);
        var definition = _Definition("0 0 * * * *");
        await manager.AddAsync(definition, ct);
        var afterInsert = await _StoreUtcNowAsync(ct);

        var position = await fixture.ReadCronSchedulePositionAsync(definition.Id, ct);
        position
            .ReconciledThroughUtc.Should()
            .BeOnOrAfter(beforeInsert)
            .And.BeOnOrBefore(afterInsert, "the watermark is the store's instant at insertion, not this node's");
        position
            .NextDueUtc.Should()
            .BeAfter(position.ReconciledThroughUtc)
            .And.BeOnOrBefore(position.ReconciledThroughUtc.AddHours(1), "the projection is the next hourly tick");
    }

    /// <summary>The bulk path seeds every definition it writes, from one anchor, exactly as the single path does.</summary>
    public virtual async Task creating_a_batch_of_definitions_seeds_every_position_from_the_store_clock()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost(
            "seed-batch",
            timeProvider: new SkewedTimeProvider(TimeSpan.FromDays(-4000))
        );
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        var manager = host.Services.GetRequiredService<ICronJobManager<CronJobEntity>>();

        var beforeInsert = await _StoreUtcNowAsync(ct);
        var hourly = _Definition("0 0 * * * *");
        var everyMinute = _Definition("0 * * * * *");
        await manager.AddBatchAsync([hourly, everyMinute], ct);
        var afterInsert = await _StoreUtcNowAsync(ct);

        foreach (var id in new[] { hourly.Id, everyMinute.Id })
        {
            var position = await fixture.ReadCronSchedulePositionAsync(id, ct);
            position.ReconciledThroughUtc.Should().BeOnOrAfter(beforeInsert).And.BeOnOrBefore(afterInsert);
            position.NextDueUtc.Should().BeAfter(position.ReconciledThroughUtc);
        }

        var minutePosition = await fixture.ReadCronSchedulePositionAsync(everyMinute.Id, ct);
        minutePosition
            .NextDueUtc.Should()
            .BeOnOrBefore(
                minutePosition.ReconciledThroughUtc.AddMinutes(1),
                "each definition's projection is derived from its own expression, not copied from a sibling"
            );
    }

    /// <summary>The coordinated write path seeds from the store too — it is the path with no node clock to fall back on.</summary>
    public virtual async Task a_coordinated_creation_seeds_its_position_from_the_store_clock()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildCoordinatedEnqueueHost<JobsDbContext>(
            "seed-coordinated",
            timeProvider: new SkewedTimeProvider(TimeSpan.FromDays(-4000))
        );
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        await host.StartAsync(ct);

        try
        {
            var manager = host.Services.GetRequiredService<ICronJobManager<CronJobEntity>>();
            var definition = _Definition("0 0 * * * *");

            var beforeInsert = await _StoreUtcNowAsync(ct);
            await fixture.RunCoordinatedTransactionAsync(
                host.Services,
                async (_, _, innerCt) => await manager.AddAsync(definition, innerCt),
                ct
            );
            var afterInsert = await _StoreUtcNowAsync(ct);

            var position = await fixture.ReadCronSchedulePositionAsync(definition.Id, ct);
            position.ReconciledThroughUtc.Should().BeOnOrAfter(beforeInsert).And.BeOnOrBefore(afterInsert);
            position.NextDueUtc.Should().BeAfter(position.ReconciledThroughUtc);
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    /// <summary>
    /// R4a, the decisive guard. The coordinated write joins a transaction the caller already opened, and PostgreSQL
    /// freezes <c>now()</c> at transaction start — so an EF-translated clock would anchor the definition BEFORE it
    /// existed and manufacture an immediate false backlog for its missed-run policy to resolve. The anchor must come
    /// from the current-statement clock (<c>clock_timestamp()</c> / <c>SYSUTCDATETIME()</c>).
    /// </summary>
    /// <remarks>
    /// The gap is real elapsed time on purpose: it is the quantity under test, not a sequencing device. Nothing here
    /// waits for a signal — the transaction is deliberately made old, and the seed must reflect that.
    /// </remarks>
    public virtual async Task a_coordinated_creation_is_anchored_at_insertion_not_at_transaction_start()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildCoordinatedEnqueueHost<JobsDbContext>("seed-old-transaction");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        await host.StartAsync(ct);

        var transactionAge = TimeSpan.FromSeconds(2);

        try
        {
            var manager = host.Services.GetRequiredService<ICronJobManager<CronJobEntity>>();
            var definition = _Definition("0 0 * * * *");
            DateTime transactionStartClock = default;

            await fixture.RunCoordinatedTransactionAsync(
                host.Services,
                async (connection, transaction, innerCt) =>
                {
                    // The value an EF-translated DateTime.UtcNow resolves to inside this transaction. On PostgreSQL it
                    // is pinned here for the transaction's whole life; reading it first is what makes the assertion
                    // below a real measurement rather than a race.
                    transactionStartClock = await _ReadEnlistedClockAsync(connection, transaction, innerCt);
                    await Task.Delay(transactionAge, innerCt);
                    await manager.AddAsync(definition, innerCt);
                },
                ct
            );

            var position = await fixture.ReadCronSchedulePositionAsync(definition.Id, ct);

            // Tolerance, because the two sides of this comparison are measured by DIFFERENT clocks: the boundary is
            // a database instant plus a delay the HOST timed, while the seed is a database instant. Timer granularity
            // and host/DB skew make the pair land a few milliseconds apart in either direction, which an exact
            // BeOnOrAfter reports as a failure (observed: 4.5ms short of a 2,000ms boundary).
            //
            // It does not blunt the guard. The defect this test exists to catch — seeding from the transaction-start
            // clock instead of the statement clock — puts the seed at transactionStartClock itself, a full
            // transactionAge early. That is 2,000ms of signal against 100ms of tolerance: still caught by 20x. Raising
            // transactionAge widens that ratio further if this ever proves too tight; loosening the tolerance toward
            // transactionAge is what would make the assertion meaningless.
            var clockComparisonTolerance = TimeSpan.FromMilliseconds(100);
            position
                .ReconciledThroughUtc.Should()
                .BeOnOrAfter(
                    transactionStartClock.Add(transactionAge).Subtract(clockComparisonTolerance),
                    "the seed reads the statement clock, so an ambient transaction's age cannot make a definition "
                        + "look as though it existed before it was created"
                );
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    /// <summary>
    /// AE3. A definition whose next tick falls between its creation and the first scheduler poll — a window a process
    /// crash can widen arbitrarily — has that tick RECOVERED under its missed-run policy, not silently dropped.
    /// </summary>
    /// <remarks>
    /// The crash is modelled the way it is observable: the creating host goes away without ever polling, and a second
    /// host does the first selection. Without the seed the definition reaches that second host unpositioned, gets
    /// anchored at THAT moment, and the tick in between is gone with no trace — no occurrence, no recovery record,
    /// nothing to alert on.
    /// </remarks>
    public virtual async Task a_tick_between_creation_and_the_first_poll_is_recovered_not_skipped()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);

        Guid definitionId;
        DateTime seededNextDueUtc;

        // The creating process. It never polls; it writes the definition and disappears.
        using (var creator = fixture.BuildHost("ae3-creator"))
        {
            await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(creator, ct);
            var manager = creator.Services.GetRequiredService<ICronJobManager<CronJobEntity>>();
            var definition = _Definition("* * * * * *");
            await manager.AddAsync(definition, ct);

            definitionId = definition.Id;
            var seeded = await fixture.ReadCronSchedulePositionAsync(definitionId, ct);
            seededNextDueUtc = seeded.NextDueUtc;
        }

        // Let the seeded tick fall due, then hand the definition to a process that never saw it created.
        var untilTick = seededNextDueUtc - await _StoreUtcNowAsync(ct);
        if (untilTick > TimeSpan.Zero)
        {
            await Task.Delay(untilTick + TimeSpan.FromMilliseconds(300), ct);
        }

        using var restarted = fixture.BuildHost("ae3-restarted");
        await restarted.StartAsync(ct);

        try
        {
            var internalManager = restarted.Services.GetRequiredService<IInternalJobManager>();
            await internalManager.GetNextJobs(ct);

            var occurrences = await _Persistence(restarted)
                .GetAllCronJobOccurrencesAsync(x => x.CronJobId == definitionId, ct);
            occurrences
                .Should()
                .ContainSingle(
                    "the tick seeded at creation is the one this poll owes, so it fires exactly once and is not "
                        + "anchored away by the first node to see the definition"
                )
                .Which.ExecutionTime.Should()
                .BeCloseTo(
                    seededNextDueUtc,
                    TimeSpan.FromMicroseconds(1),
                    "the occurrence is the seeded tick itself, not some instant after the restart"
                );

            // At or past, not exactly at: whether the poll lands inside one tick (ordinary dispatch, watermark = the
            // tick) or after several (coalesce recovery, watermark = the recovery instant) is a latency detail. The
            // contract AE3 asserts is that the tick was ACCOUNTED FOR — the occurrence above proves it fired, and this
            // proves nothing will reconsider it.
            var position = await fixture.ReadCronSchedulePositionAsync(definitionId, ct);
            position
                .ReconciledThroughUtc.Should()
                .BeOnOrAfter(seededNextDueUtc, "the watermark moved through the tick it resolved");
        }
        finally
        {
            await restarted.StopAsync(ct);
        }
    }

    private static CronJobEntity _Definition(string expression) =>
        new()
        {
            Id = Guid.NewGuid(),
            Function = JobsCoordinationFixtureExtensions.CoordinatedFunctionName,
            Description = JobsCoordinationFixtureExtensions.CoordinatedFunctionName,
            Expression = expression,
            Request = [],
        };

    /// <summary>The store's own instant, read on an independent autocommit connection.</summary>
    private async Task<DateTime> _StoreUtcNowAsync(CancellationToken cancellationToken)
    {
        await using var connection = fixture.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {fixture.UtcNowSqlExpression}";

        return _AsUtc(await command.ExecuteScalarAsync(cancellationToken));
    }

    /// <summary>The clock an EF-translated <c>DateTime.UtcNow</c> resolves to inside the supplied transaction.</summary>
    private async Task<DateTime> _ReadEnlistedClockAsync(
        DbConnection connection,
        DbTransaction transaction,
        CancellationToken cancellationToken
    )
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT {fixture.EfTranslatedDatabaseClockSql}";

        return _AsUtc(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static DateTime _AsUtc(object? value)
    {
        var instant = (DateTime)value!;

        return instant.Kind == DateTimeKind.Utc ? instant : DateTime.SpecifyKind(instant, DateTimeKind.Utc);
    }

    private static IJobPersistenceProvider<TimeJobEntity, CronJobEntity> _Persistence(IHost host) =>
        host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();

    private static CronScheduleMaterialization _Materialization(
        Guid cronJobId,
        (DateTime ReconciledThroughUtc, DateTime NextDueUtc) position
    ) =>
        new()
        {
            Advance = new CronScheduleAdvance
            {
                CronJobId = cronJobId,
                ObservedReconciledThroughUtc = position.ReconciledThroughUtc,
                ExpectedScheduleRevision = 0,
                ReconciledThroughUtc = position.NextDueUtc,
                NextDueUtc = position.NextDueUtc.AddMinutes(1),
                RequireProjectionDue = true,
            },
            ExecutionTimeUtc = position.NextDueUtc,
        };

    private async Task _AssertPositionUnchangedAsync(
        Guid cronJobId,
        (DateTime ReconciledThroughUtc, DateTime NextDueUtc) expected,
        CancellationToken cancellationToken
    )
    {
        var actual = await fixture.ReadCronSchedulePositionAsync(cronJobId, cancellationToken);
        actual.ReconciledThroughUtc.Should().BeCloseTo(expected.ReconciledThroughUtc, TimeSpan.FromMicroseconds(1));
        actual.NextDueUtc.Should().BeCloseTo(expected.NextDueUtc, TimeSpan.FromMicroseconds(1));
    }

    /// <summary>A TimeProvider deliberately offset from real time. Only <c>GetUtcNow</c> is exercised.</summary>
    private sealed class SkewedTimeProvider(TimeSpan offset) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow.Add(offset);
    }
}

internal sealed class FailAfterCronPositionUpdateInterceptor : DbCommandInterceptor
{
    private int _armed;
    private int _positionUpdated;

    public void Arm() => Interlocked.Exchange(ref _armed, 1);

    public override ValueTask<int> NonQueryExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        int result,
        CancellationToken cancellationToken = default
    )
    {
        _RecordPositionUpdate(command);
        return base.NonQueryExecutedAsync(command, eventData, result, cancellationToken);
    }

    public override ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result,
        CancellationToken cancellationToken = default
    )
    {
        _RecordPositionUpdate(command);
        return base.ReaderExecutedAsync(command, eventData, result, cancellationToken);
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default
    )
    {
        if (Volatile.Read(ref _positionUpdated) == 1)
        {
            throw new InvalidOperationException("Injected materialization failure.");
        }

        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }

    private void _RecordPositionUpdate(DbCommand command)
    {
        if (
            Volatile.Read(ref _armed) == 1
            && command.CommandText.Contains("UPDATE", StringComparison.OrdinalIgnoreCase)
            && command.CommandText.Contains("ReconciledThroughUtc", StringComparison.Ordinal)
            && command.CommandText.Contains("NextDueUtc", StringComparison.Ordinal)
        )
        {
            Interlocked.Exchange(ref _positionUpdated, 1);
        }
    }
}
