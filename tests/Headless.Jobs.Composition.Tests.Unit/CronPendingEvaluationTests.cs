// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using Headless.Jobs;
using Headless.Testing.Tests;

namespace Tests;

/// <summary>
/// Misfire detection: what a schedule owes as of one store instant, and whether that backlog is a misfire rather than
/// ordinary lateness. Every scenario is decided from the watermark and the schedule alone — never from occurrence
/// rows, because the case this exists to catch is precisely the one where no row was ever written.
/// </summary>
public sealed class CronPendingEvaluationTests : TestBase
{
    private const string _EveryMinute = "0 * * * * *";
    private const string _EverySecond = "* * * * * *";

    // Grace scenarios need a schedule coarse enough that a single instant can be minutes late without a second one
    // becoming pending; on an every-minute schedule "90s late" necessarily means two pending instants, not one.
    private const string _Hourly = "0 0 * * * *";
    private const int _Grace = 60;

    private static readonly DateTime _Now = new(2026, 07, 26, 12, 00, 00, DateTimeKind.Utc);

    private static CronScheduleCache _Cache() => new(TimeZoneInfo.Utc);

    [Fact]
    public void should_report_nothing_pending_when_the_watermark_is_current()
    {
        // Reconciled through now: the next occurrence is in the future, so the schedule owes nothing.
        var result = _Cache().EvaluatePending(_EveryMinute, timeZoneId: null, _Now, _Now, _Grace);

        result.Should().Be(CronPendingEvaluation.None);
        result.IsRecovery.Should().BeFalse();
        result.EarliestPendingUtc.Should().BeNull();
    }

    /// <summary>A single occurrence delayed by less than the grace threshold dispatches normally.</summary>
    [Fact]
    public void should_not_treat_a_single_occurrence_inside_the_grace_threshold_as_a_misfire()
    {
        // Watermark at 11:30, so 12:00 is the one pending instant — evaluated 30s late, inside the 60s grace.
        var result = _Cache()
            .EvaluatePending(_Hourly, timeZoneId: null, _Now.AddMinutes(-30), _Now.AddSeconds(30), _Grace);

        result.PendingCount.Should().Be(1);
        result.EarliestPendingUtc.Should().Be(_Now);
        result.IsRecovery.Should().BeFalse("30s of lateness is inside the 60s grace threshold");
    }

    [Fact]
    public void should_enter_recovery_when_a_single_occurrence_exceeds_the_grace_threshold()
    {
        // The same single pending instant, now 90s late. Still one instant: the next hourly tick is 13:00.
        var result = _Cache()
            .EvaluatePending(_Hourly, timeZoneId: null, _Now.AddMinutes(-30), _Now.AddSeconds(90), _Grace);

        result.PendingCount.Should().Be(1);
        result.EarliestPendingUtc.Should().Be(_Now);
        result.IsRecovery.Should().BeTrue("90s of lateness exceeds the 60s grace threshold");
    }

    [Fact]
    public void should_enter_recovery_on_two_pending_occurrences_regardless_of_age()
    {
        // Both instants are well inside the grace threshold individually; the COUNT is what makes this a misfire.
        var result = _Cache()
            .EvaluatePending(_EveryMinute, timeZoneId: null, _Now.AddSeconds(-1), _Now.AddMinutes(1), _Grace);

        result.PendingCount.Should().Be(2);
        result.IsRecovery.Should().BeTrue("more than one pending instant is a misfire however recent each one is");
    }

    /// <summary>A sub-grace backlog still routes to recovery.</summary>
    [Fact]
    public void should_enter_recovery_for_a_sub_grace_backlog_on_a_high_frequency_schedule()
    {
        // A one-second schedule stalled for ten seconds: ten pending instants, none older than the 60s grace.
        var result = _Cache().EvaluatePending(_EverySecond, timeZoneId: null, _Now, _Now.AddSeconds(10), _Grace);

        result.PendingCount.Should().Be(10);
        result.EarliestPendingUtc.Should().Be(_Now.AddSeconds(1));
        result.CountSaturated.Should().BeFalse();
        result.IsRecovery.Should().BeTrue("ten pending instants is a backlog even though no single one is late");
    }

    /// <summary>A backlog past the evaluation ceiling still decides correctly and reports a lower bound.</summary>
    [Fact]
    public void should_report_a_lower_bound_count_without_unbounded_evaluation()
    {
        // A one-second schedule after an hour of downtime is 3600 instants behind, well past a ceiling of 50.
        var result = _Cache()
            .EvaluatePending(_EverySecond, timeZoneId: null, _Now, _Now.AddHours(1), _Grace, evaluationCeiling: 50);

        result.PendingCount.Should().Be(50, "the walk stops at the ceiling rather than enumerating the full backlog");
        result.CountSaturated.Should().BeTrue("the count is a lower bound, not the real backlog size");
        result.IsRecovery.Should().BeTrue("saturation never changes the decision — it was settled at the 2nd instant");
        result
            .EarliestPendingUtc.Should()
            .Be(
                _Now.AddSeconds(1),
                "the earliest instant stays exact under saturation because the walk visits it first — that is what "
                    + "lets a coalesced run report an accurate scheduled instant for an unbounded backlog"
            );
    }

    [Fact]
    public void should_report_an_exact_count_when_the_backlog_lands_on_the_ceiling()
    {
        // Exactly 10 pending instants with a ceiling of 10: the walk fills the ceiling but nothing lies beyond it.
        var result = _Cache()
            .EvaluatePending(_EverySecond, timeZoneId: null, _Now, _Now.AddSeconds(10), _Grace, evaluationCeiling: 10);

        result.PendingCount.Should().Be(10);
        result.CountSaturated.Should().BeFalse("a backlog that exactly fills the ceiling is still an exact count");
    }

    [Fact]
    public void should_use_the_definitions_own_grace_rather_than_the_framework_default()
    {
        // 90s late: a misfire under the 60s default, but not under this definition's own 300s threshold.
        var lenient = _Cache()
            .EvaluatePending(_Hourly, timeZoneId: null, _Now.AddMinutes(-30), _Now.AddSeconds(90), graceSeconds: 300);

        lenient.PendingCount.Should().Be(1);
        lenient.IsRecovery.Should().BeFalse("the definition tolerates 300s of lateness");

        var strict = _Cache()
            .EvaluatePending(_Hourly, timeZoneId: null, _Now.AddMinutes(-30), _Now.AddSeconds(90), graceSeconds: 10);

        strict.IsRecovery.Should().BeTrue("the same lateness exceeds a 10s threshold");
    }

    [Fact]
    public void should_fall_back_to_the_framework_grace_when_the_definition_persists_zero()
    {
        var result = _Cache()
            .EvaluatePending(_Hourly, timeZoneId: null, _Now.AddMinutes(-30), _Now.AddSeconds(1), graceSeconds: 0);

        result.PendingCount.Should().Be(1);
        result.IsRecovery.Should().BeFalse("zero selects the framework default");
    }

    [Fact]
    public void should_reject_a_negative_persisted_grace()
    {
        var act = () =>
            _Cache()
                .EvaluatePending(_Hourly, timeZoneId: null, _Now.AddMinutes(-30), _Now.AddSeconds(1), graceSeconds: -1);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    /// <summary>
    /// Pause needs no special case here. Resume rebases the watermark to the resume instant, so the paused span
    /// is never between the watermark and now and cannot produce a pending instant.
    /// </summary>
    [Fact]
    public void should_produce_no_pending_instants_for_a_paused_span()
    {
        // Paused at 12:00, resumed at 15:30 (watermark rebased there by the resume path), evaluated moments later.
        var resumeInstant = _Now.AddHours(3).AddMinutes(30);

        var result = _Cache()
            .EvaluatePending(_EveryMinute, timeZoneId: null, resumeInstant, resumeInstant.AddSeconds(5), _Grace);

        result
            .Should()
            .Be(CronPendingEvaluation.None, "the three-and-a-half-hour pause is behind the rebased watermark");
    }

    [Fact]
    public void should_report_the_latest_pending_instant_the_walk_reached()
    {
        var result = _Cache().EvaluatePending(_EveryMinute, timeZoneId: null, _Now, _Now.AddMinutes(3), _Grace);

        result.PendingCount.Should().Be(3);
        result.EarliestPendingUtc.Should().Be(_Now.AddMinutes(1));
        result.LatestPendingUtc.Should().Be(_Now.AddMinutes(3));
    }

    // Cairo falls back at 21:00Z on 29 October 2026: 24:00 (+03:00) becomes 23:00 (+02:00), so 23:00–23:59 repeats.
    private static readonly DateTime _CairoFallBack = new(2026, 10, 29, 21, 0, 0, DateTimeKind.Utc);

    /// <summary>Every 15 minutes is a cadence that continues through the repeated hour, so both passes fire.</summary>
    private static readonly DateTime[] _CairoQuarterHours =
    [
        .. Enumerable.Range(0, 17).Select(i => _CairoFallBack.AddHours(-2).AddMinutes(15 * i)),
    ];

    [Theory]
    [InlineData("2026-10-29T19:45:00Z")]
    [InlineData("2026-10-29T20:00:00Z")]
    [InlineData("2026-10-29T20:50:00Z")]
    [InlineData("2026-10-29T21:00:00Z")]
    [InlineData("2026-10-29T21:50:00Z")]
    public void should_owe_the_same_instants_across_an_overlap_as_the_next_occurrence_walk(string watermarkText)
    {
        var watermark = DateTime.Parse(watermarkText, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal);
        var storeNow = _CairoFallBack.AddHours(2);

        var result = new CronScheduleCache(CronTestZones.Cairo()).EvaluatePending(
            "0 */15 * * * *",
            timeZoneId: null,
            watermark,
            storeNow,
            _Grace
        );

        result.PendingInstantsUtc.Should().Equal(_CairoQuarterHours.Where(x => x > watermark));
        result.NextAfterPendingUtc.Should().Be(storeNow.AddMinutes(15));
    }

    [Fact]
    public void should_project_the_same_next_instant_from_a_walk_as_from_a_direct_lookup()
    {
        var cache = new CronScheduleCache(CronTestZones.Cairo());

        for (var i = 1; i < _CairoQuarterHours.Length; i++)
        {
            var result = cache.EvaluatePending(
                "0 */15 * * * *",
                timeZoneId: null,
                _CairoQuarterHours[i - 1],
                _CairoQuarterHours[i],
                _Grace
            );

            result.LatestPendingUtc.Should().Be(_CairoQuarterHours[i]);
            result
                .NextAfterPendingUtc.Should()
                .Be(cache.GetNextOccurrenceOrDefault("0 */15 * * * *", _CairoQuarterHours[i]));
        }
    }

    [Fact]
    public void should_treat_a_missed_first_pass_as_an_ordinary_misfire()
    {
        // 23:30 (+03:00) is 20:30Z. No scheduler ran from 20:00Z; one wakes at 21:10Z, after the overlap began.
        var watermark = _CairoFallBack.AddHours(-1);
        var storeNow = _CairoFallBack.AddMinutes(10);
        var cache = new CronScheduleCache(CronTestZones.Cairo());

        var result = cache.EvaluatePending("0 30 23 * * *", timeZoneId: null, watermark, storeNow, _Grace);

        result.PendingInstantsUtc.Should().Equal(_CairoFallBack.AddMinutes(-30));
        result.IsRecovery.Should().BeTrue("the first pass is 40 minutes late, past the 60s grace threshold");

        // The second pass at 23:30 (+02:00), 21:30Z, is not a fallback: the next run is 30 October's.
        var nextDay = new DateTime(2026, 10, 30, 21, 30, 0, DateTimeKind.Utc);
        result.NextAfterPendingUtc.Should().Be(nextDay);

        var coalesce = CronRecoveryPlanner.CreatePlan(
            _Recovery(cache, result, watermark, storeNow, MissedRunPolicy.Coalesce),
            []
        );
        coalesce.RunSteps.Should().ContainSingle().Which.ExecutionTimeUtc.Should().Be(_CairoFallBack.AddMinutes(-30));
        coalesce.WhenRunEstablished.NextDueUtc.Should().Be(nextDay);

        var skip = CronRecoveryPlanner.CreatePlan(
            _Recovery(cache, result, watermark, storeNow, MissedRunPolicy.Skip),
            []
        );
        skip.RunSteps.Should().BeEmpty();
        skip.WhenNoRunEstablished.NextDueUtc.Should().Be(nextDay);
    }

    [Fact]
    public void should_owe_nothing_until_the_next_day_once_the_first_pass_is_reconciled()
    {
        // Reconciled just past the first pass (20:30Z); the repeated 23:30 (+02:00) owes nothing.
        var watermark = _CairoFallBack.AddMinutes(-29);
        var cache = new CronScheduleCache(CronTestZones.Cairo());

        cache
            .EvaluatePending("0 30 23 * * *", timeZoneId: null, watermark, _CairoFallBack.AddMinutes(10), _Grace)
            .Should()
            .Be(CronPendingEvaluation.None);

        cache
            .EvaluatePending(
                "0 30 23 * * *",
                timeZoneId: null,
                watermark,
                new DateTime(2026, 10, 30, 21, 29, 59, DateTimeKind.Utc),
                _Grace
            )
            .Should()
            .Be(CronPendingEvaluation.None);
    }

    // Built the way the dispatch path builds it from an evaluation, so the plan answers for the instants that
    // evaluation reported.
    private static CronRecoveryRequest _Recovery(
        CronScheduleCache cache,
        CronPendingEvaluation pending,
        DateTime watermark,
        DateTime storeNow,
        MissedRunPolicy policy
    )
    {
        var latest = pending.LatestPendingUtc!.Value;

        return new CronRecoveryRequest
        {
            CronJobId = Guid.NewGuid(),
            ObservedReconciledThroughUtc = watermark,
            ExpectedScheduleRevision = 1,
            RecoveredThroughUtc = storeNow,
            NextDueUtc = cache.GetNextOccurrenceOrDefault("0 30 23 * * *", storeNow)!.Value,
            BoundedProgressThroughUtc = latest,
            NextDueAfterBoundedProgressUtc = cache.GetNextOccurrenceOrDefault("0 30 23 * * *", latest)!.Value,
            EvaluationSaturated = pending.CountSaturated,
            Policy = policy,
            EarliestMissedUtc = pending.EarliestPendingUtc!.Value,
            MissedInstantsUtc = pending.PendingInstantsUtc,
            CoalescedOccurrenceId = Guid.NewGuid(),
            OperationTimeUtc = new DateTimeOffset(storeNow, TimeSpan.Zero),
        };
    }
}
