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
    public void get_next_occurrence_or_default_uses_the_later_utc_instant_for_an_ambiguous_fall_occurrence()
    {
        var cache = new CronScheduleCache(_CreateTestTimeZone());
        var beforeOverlap = new DateTime(2026, 10, 24, 22, 0, 0, DateTimeKind.Utc);

        var next = cache.GetNextOccurrenceOrDefault("0 30 2 * * *", beforeOverlap);

        next.Should().Be(new DateTime(2026, 10, 25, 0, 30, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void get_next_occurrence_or_default_does_not_skip_the_later_fall_occurrence_after_restart()
    {
        var cache = new CronScheduleCache(_CreateTestTimeZone());
        var betweenOverlapInstants = new DateTime(2026, 10, 24, 23, 45, 0, DateTimeKind.Utc);

        var next = cache.GetNextOccurrenceOrDefault("0 30 2 * * *", betweenOverlapInstants);

        next.Should().Be(new DateTime(2026, 10, 25, 0, 30, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void get_next_occurrence_or_default_shifts_an_explicit_iana_spring_occurrence_through_the_gap()
    {
        var beforeGap = new DateTime(2026, 3, 8, 5, 0, 0, DateTimeKind.Utc);

        var next = _cache.GetNextOccurrenceOrDefault("0 30 2 * * *", beforeGap, "America/New_York");

        next.Should().Be(new DateTime(2026, 3, 8, 7, 30, 0, DateTimeKind.Utc));
        next!.Value.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void get_next_occurrence_or_default_uses_the_later_utc_instant_for_an_explicit_iana_fall_occurrence()
    {
        var beforeOverlap = new DateTime(2026, 11, 1, 4, 0, 0, DateTimeKind.Utc);

        var next = _cache.GetNextOccurrenceOrDefault("0 30 1 * * *", beforeOverlap, "America/New_York");

        next.Should().Be(new DateTime(2026, 11, 1, 6, 30, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void get_next_occurrence_or_default_remains_deterministic_when_origin_is_inside_an_explicit_iana_overlap()
    {
        var betweenOverlapInstants = new DateTime(2026, 11, 1, 5, 45, 0, DateTimeKind.Utc);

        var next = _cache.GetNextOccurrenceOrDefault("0 30 1 * * *", betweenOverlapInstants, "America/New_York");

        next.Should().Be(new DateTime(2026, 11, 1, 6, 30, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void get_next_occurrence_or_default_uses_the_configured_global_timezone_when_definition_timezone_is_null()
    {
        var cache = new CronScheduleCache(TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles"));
        var origin = new DateTime(2026, 1, 1, 16, 30, 0, DateTimeKind.Utc);

        var next = cache.GetNextOccurrenceOrDefault("0 0 9 * * *", origin, timeZoneId: null);

        next.Should().Be(new DateTime(2026, 1, 1, 17, 0, 0, DateTimeKind.Utc));
    }

    [Theory]
    [InlineData("Eastern Standard Time")]
    [InlineData("Headless/Unknown")]
    public void get_next_occurrence_or_default_rejects_non_iana_timezone_ids(string timeZoneId)
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
