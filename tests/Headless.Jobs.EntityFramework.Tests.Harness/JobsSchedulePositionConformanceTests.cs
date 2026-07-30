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
/// No <c>StartAsync</c>: the advance is a direct persistence call that takes no ownership and stamps no lease, so it
/// must not depend on membership or on a skewed clock driving it.
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

    private static IJobPersistenceProvider<TimeJobEntity, CronJobEntity> _Persistence(IHost host) =>
        host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();

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
