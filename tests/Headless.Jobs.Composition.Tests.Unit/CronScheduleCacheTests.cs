// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs;

namespace Tests;

public sealed class CronScheduleCacheTests
{
    private readonly CronScheduleCache _cache = new(TimeZoneInfo.Utc);

    [Fact]
    public void get_next_occurrence_or_default_returns_null_for_invalid_expression()
    {
        var next = _cache.GetNextOccurrenceOrDefault("invalid cron", DateTime.UtcNow);

        next.Should().BeNull();
    }

    [Theory]
    [InlineData("0 0 0 30 2 *")] // Feb 30 never exists
    [InlineData("0 0 0 31 4 *")] // Apr 31 never exists
    [InlineData("0 0 0 31 2 *")] // Feb 31 never exists
    public void get_next_occurrence_or_default_returns_null_for_expressions_that_never_fire(string expression)
    {
        // NCrontab reports "no future occurrence" as a DateTime.MaxValue-era value, not null — without the
        // sentinel guard these expressions were accepted as valid recurring jobs and a permanently pending
        // year-9999 occurrence row was materialized and re-leased forever.
        var next = _cache.GetNextOccurrenceOrDefault(expression, DateTime.UtcNow);

        next.Should().BeNull();
    }

    [Fact]
    public void get_next_occurrence_or_default_normalizes_whitespace_and_caches()
    {
        var now = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        const string expr1 = "*/5 * * * * *";
        const string expr2 = "*/5    *   *   *   *   *";

        var next1 = _cache.GetNextOccurrenceOrDefault(expr1, now);
        var next2 = _cache.GetNextOccurrenceOrDefault(expr2, now);

        next1.Should().NotBeNull();
        next2.Should().NotBeNull();
        next2.Should().Be(next1);

        var invalidated = _cache.Invalidate(expr1);
        invalidated.Should().BeTrue();
    }

    [Fact]
    public void get_next_occurrence_or_default_shifts_an_invalid_spring_occurrence_forward_by_the_dst_gap()
    {
        var cache = new CronScheduleCache(_CreateTestTimeZone());
        var beforeGap = new DateTime(2026, 3, 28, 23, 0, 0, DateTimeKind.Utc);

        var next = cache.GetNextOccurrenceOrDefault("0 30 2 * * *", beforeGap);

        next.Should().Be(new DateTime(2026, 3, 29, 0, 30, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void get_next_occurrence_or_default_uses_the_first_pass_for_an_ambiguous_fall_occurrence()
    {
        var cache = new CronScheduleCache(_CreateTestTimeZone());
        var beforeOverlap = new DateTime(2026, 10, 24, 22, 0, 0, DateTimeKind.Utc);

        var next = cache.GetNextOccurrenceOrDefault("0 30 2 * * *", beforeOverlap);

        // 02:30 (+03:00), the first time the clock shows 02:30.
        next.Should().Be(new DateTime(2026, 10, 24, 23, 30, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void get_next_occurrence_or_default_does_not_run_the_second_pass_of_a_fixed_time_after_restart()
    {
        var cache = new CronScheduleCache(_CreateTestTimeZone());
        var betweenOverlapInstants = new DateTime(2026, 10, 24, 23, 45, 0, DateTimeKind.Utc);

        var next = cache.GetNextOccurrenceOrDefault("0 30 2 * * *", betweenOverlapInstants);

        // The first pass already fired at 23:30Z; a fixed time has no second pass, so the next run is the next day's.
        next.Should().Be(new DateTime(2026, 10, 26, 0, 30, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void should_shift_occurrence_through_gap_when_explicit_iana_time_is_invalid()
    {
        var beforeGap = new DateTime(2026, 3, 8, 5, 0, 0, DateTimeKind.Utc);

        var next = _cache.GetNextOccurrenceOrDefault("0 30 2 * * *", beforeGap, "America/New_York");

        next.Should().Be(new DateTime(2026, 3, 8, 7, 30, 0, DateTimeKind.Utc));
        next!.Value.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void should_use_first_pass_when_explicit_iana_time_is_ambiguous()
    {
        var beforeOverlap = new DateTime(2026, 11, 1, 4, 0, 0, DateTimeKind.Utc);

        var next = _cache.GetNextOccurrenceOrDefault("0 30 1 * * *", beforeOverlap, "America/New_York");

        next.Should().Be(new DateTime(2026, 11, 1, 5, 30, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void should_skip_second_pass_when_origin_is_inside_explicit_iana_overlap()
    {
        var betweenOverlapInstants = new DateTime(2026, 11, 1, 5, 45, 0, DateTimeKind.Utc);

        var next = _cache.GetNextOccurrenceOrDefault("0 30 1 * * *", betweenOverlapInstants, "America/New_York");

        next.Should().Be(new DateTime(2026, 11, 2, 6, 30, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void should_use_configured_global_timezone_when_definition_timezone_is_null()
    {
        var cache = new CronScheduleCache(TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles"));
        var origin = new DateTime(2026, 1, 1, 16, 30, 0, DateTimeKind.Utc);

        var next = cache.GetNextOccurrenceOrDefault("0 0 9 * * *", origin, timeZoneId: null);

        next.Should().Be(new DateTime(2026, 1, 1, 17, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void should_reject_a_local_kind_cursor_instead_of_reading_it_as_utc()
    {
        var localCursor = new DateTime(2026, 10, 29, 20, 0, 0, DateTimeKind.Local);

        var act = () => _cache.GetNextOccurrenceOrDefault("0 */15 * * * *", localCursor);

        act.Should().Throw<ArgumentException>().WithParameterName("dateTime");
    }

    [Theory]
    [InlineData("Eastern Standard Time")]
    [InlineData("Headless/Unknown")]
    public void should_reject_timezone_when_identifier_is_not_iana(string timeZoneId)
    {
        var act = () => _cache.GetNextOccurrenceOrDefault("0 0 * * * *", DateTime.UnixEpoch, timeZoneId);

        act.Should().Throw<ArgumentException>().WithMessage($"*{timeZoneId}*");
    }

    private static TimeZoneInfo _CreateTestTimeZone()
    {
        var daylightTransitionStart = TimeZoneInfo.TransitionTime.CreateFixedDateRule(
            new DateTime(1, 1, 1, 2, 0, 0),
            3,
            29
        );
        var daylightTransitionEnd = TimeZoneInfo.TransitionTime.CreateFixedDateRule(
            new DateTime(1, 1, 1, 3, 0, 0),
            10,
            25
        );
        var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
            new DateTime(2026, 1, 1),
            new DateTime(2026, 12, 31),
            TimeSpan.FromHours(1),
            daylightTransitionStart,
            daylightTransitionEnd
        );

        return TimeZoneInfo.CreateCustomTimeZone(
            "Headless.Test.Dst",
            TimeSpan.FromHours(2),
            "Headless Test",
            "Headless Standard",
            "Headless Daylight",
            [rule]
        );
    }
}
