// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs;
using Headless.Testing.Tests;

namespace Tests;

/// <summary>
/// The fingerprint's whole job is to make an invisible change visible: an expression and a timezone identifier can
/// stay byte-identical while the instant they resolve to moves, because the zone's rules changed underneath them.
/// </summary>
/// <remarks>
/// The rule-change scenarios use synthetic zones rather than real ones. Asserting on a real zone's rules would make
/// the test a hostage to whatever tzdata the machine ships — it would pass or fail on the CI image's IANA version
/// rather than on this code, which is exactly the kind of test that erodes trust in a suite.
/// </remarks>
public sealed class CronEvaluationFingerprintTests : TestBase
{
    private static CronScheduleCache _Cache() => new(TimeZoneInfo.Utc);

    [Fact]
    public void should_produce_the_same_fingerprint_for_the_same_zone_under_the_same_rules()
    {
        var first = _Cache().ComputeEvaluationFingerprint("Europe/Berlin");
        var second = _Cache().ComputeEvaluationFingerprint("Europe/Berlin");

        second.Should().Be(first);
        first.Should().HaveLength(64, "a SHA-256 digest, and it must fit the 128-char persisted column");
    }

    [Fact]
    public void should_produce_a_different_fingerprint_for_a_different_zone()
    {
        var berlin = _Cache().ComputeEvaluationFingerprint("Europe/Berlin");
        var cairo = _Cache().ComputeEvaluationFingerprint("Africa/Cairo");

        cairo.Should().NotBe(berlin, "two zones interpret the same wall-clock expression as different instants");
    }

    [Fact]
    public void should_reflect_the_scheduler_wide_fallback_when_the_definition_names_no_zone()
    {
        // A definition with no timezone takes the scheduler's zone, so its fingerprint must describe THAT zone —
        // otherwise changing the scheduler's zone would leave every such definition claiming to be current.
        var utcScheduler = new CronScheduleCache(TimeZoneInfo.Utc).ComputeEvaluationFingerprint(timeZoneId: null);
        var berlinScheduler = new CronScheduleCache(
            TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin")
        ).ComputeEvaluationFingerprint(timeZoneId: null);

        berlinScheduler.Should().NotBe(utcScheduler);
    }

    /// <summary>
    /// AE14's premise: a tzdata update shifts a zone's transition while the expression and zone string stay identical.
    /// </summary>
    [Fact]
    public void should_change_when_the_zones_transition_rules_change()
    {
        var before = CronEvaluationFingerprint.Compute(_Zone(daylightStartMonth: 3));
        var after = CronEvaluationFingerprint.Compute(_Zone(daylightStartMonth: 4));

        after
            .Should()
            .NotBe(
                before,
                "the identifier and offset are unchanged — only the transition month moved, which is precisely the "
                    + "shape of a tzdata update that silently changes what an unchanged expression resolves to"
            );
    }

    [Fact]
    public void should_change_when_the_daylight_delta_changes()
    {
        var before = CronEvaluationFingerprint.Compute(_Zone(daylightStartMonth: 3, deltaHours: 1));
        var after = CronEvaluationFingerprint.Compute(_Zone(daylightStartMonth: 3, deltaHours: 2));

        after.Should().NotBe(before);
    }

    [Fact]
    public void should_change_when_an_adjustment_rules_base_offset_delta_changes()
    {
        var before = CronEvaluationFingerprint.Compute(_Zone(daylightStartMonth: 3, baseOffsetDeltaHours: 0));
        var after = CronEvaluationFingerprint.Compute(_Zone(daylightStartMonth: 3, baseOffsetDeltaHours: 1));

        after.Should().NotBe(before);
    }

    [Fact]
    public void should_not_change_for_an_identically_defined_zone()
    {
        var first = CronEvaluationFingerprint.Compute(_Zone(daylightStartMonth: 3));
        var second = CronEvaluationFingerprint.Compute(_Zone(daylightStartMonth: 3));

        second.Should().Be(first, "identical rules must fingerprint identically or every sweep would rebase forever");
    }

    /// <summary>
    /// Stability across restarts is load-bearing: a fingerprint built from a per-process randomized hash would make
    /// every definition look stale on every boot and rebase the entire schedule set on startup.
    /// </summary>
    [Fact]
    public void should_be_stable_rather_than_derived_from_a_randomized_runtime_hash()
    {
        var zone = _Zone(daylightStartMonth: 3);
        var fingerprint = CronEvaluationFingerprint.Compute(zone);

        // A randomized hash would differ between two structurally identical inputs built independently in the same
        // process only across restarts, which a single-process test cannot observe. What it CAN prove is that the
        // digest is a pure function of the declared rules and carries no instance identity.
        CronEvaluationFingerprint.Compute(_Zone(daylightStartMonth: 3)).Should().Be(fingerprint);
        fingerprint.Should().MatchRegex("^[0-9a-f]{64}$", "a hex SHA-256 digest, not a runtime hash code");
    }

    private static TimeZoneInfo _Zone(int daylightStartMonth, int deltaHours = 1, int baseOffsetDeltaHours = 0)
    {
        var start = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(
            new DateTime(1, 1, 1, 2, 0, 0, DateTimeKind.Unspecified),
            daylightStartMonth,
            5,
            DayOfWeek.Sunday
        );
        var end = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(
            new DateTime(1, 1, 1, 3, 0, 0, DateTimeKind.Unspecified),
            10,
            5,
            DayOfWeek.Sunday
        );
        var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
            DateTime.MinValue.Date,
            DateTime.MaxValue.Date,
            TimeSpan.FromHours(deltaHours),
            start,
            end,
            TimeSpan.FromHours(baseOffsetDeltaHours)
        );

        return TimeZoneInfo.CreateCustomTimeZone(
            "Test/Synthetic",
            TimeSpan.FromHours(1),
            "Synthetic",
            "Synthetic Standard",
            "Synthetic Daylight",
            [rule]
        );
    }
}
