// Copyright (c) Mahmoud Shaheen. All rights reserved.

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

    /// <summary>AE3: a single occurrence delayed by less than the grace threshold dispatches normally.</summary>
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

    /// <summary>AE7: a sub-grace backlog still routes to recovery.</summary>
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

    /// <summary>AE12: a backlog past the evaluation ceiling still decides correctly and reports a lower bound.</summary>
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
        result.IsRecovery.Should().BeFalse("zero is the legacy migration sentinel and uses the framework default");
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
    /// AE6: pause needs no special case here. Resume rebases the watermark to the resume instant, so the paused span
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
}
