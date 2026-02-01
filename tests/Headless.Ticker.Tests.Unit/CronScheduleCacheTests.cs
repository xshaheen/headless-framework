using Headless.Ticker;

namespace Tests;

public sealed class CronScheduleCacheTests
{
    [Fact]
    public void GetNextOccurrenceOrDefault_Returns_Null_For_Invalid_Expression()
    {
        CronScheduleCache.TimeZoneInfo = TimeZoneInfo.Utc;

        var next = CronScheduleCache.GetNextOccurrenceOrDefault("invalid cron", DateTime.UtcNow);

        next.Should().BeNull();
    }

    [Fact]
    public void GetNextOccurrenceOrDefault_Normalizes_Whitespace_And_Caches()
    {
        CronScheduleCache.TimeZoneInfo = TimeZoneInfo.Utc;
        var now = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        const string expr1 = "*/5 * * * * *";
        const string expr2 = "*/5    *   *   *   *   *";

        var next1 = CronScheduleCache.GetNextOccurrenceOrDefault(expr1, now);
        var next2 = CronScheduleCache.GetNextOccurrenceOrDefault(expr2, now);

        next1.Should().NotBeNull();
        next2.Should().NotBeNull();
        next2.Should().Be(next1);

        var invalidated = CronScheduleCache.Invalidate(expr1);
        invalidated.Should().BeTrue();
    }
}
