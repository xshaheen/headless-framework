// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs;
using Headless.Testing.Tests;

namespace Tests;

/// <summary>
/// A schedule preview lists the next fires of a stored or draft expression in its zone with a plain reading, or says
/// why the expression cannot be scheduled. Expected fires are derived by hand from the zone's offsets.
/// </summary>
public sealed class CronSchedulePreviewTests : TestBase
{
    [Fact]
    public void should_list_five_weekday_nine_o_clock_fires_across_the_cairo_spring_forward()
    {
        // given: Cairo moves from +02:00 to +03:00 at 22:00Z on 23 April 2026, a Thursday night.
        var cache = new CronScheduleCache(CronTestZones.Cairo());

        // when
        var preview = cache.Preview("0 0 9 * * 1-5", timeZoneId: null, _Utc(2026, 4, 22, 0, 0), count: 5);

        // then
        preview.ValidationMessage.Should().BeNull();
        preview
            .NextFiresUtc.Should()
            .Equal(
                _Utc(2026, 4, 22, 7, 0), // Wednesday 09:00 +02:00
                _Utc(2026, 4, 23, 7, 0), // Thursday 09:00 +02:00
                _Utc(2026, 4, 24, 6, 0), // Friday 09:00 +03:00
                _Utc(2026, 4, 27, 6, 0), // Monday
                _Utc(2026, 4, 28, 6, 0) // Tuesday
            );
        preview.Reading.Should().Be("At 09:00, Monday through Friday");
    }

    [Fact]
    public void should_return_a_validation_message_and_no_fires_for_a_draft_that_does_not_parse()
    {
        var cache = new CronScheduleCache(TimeZoneInfo.Utc);

        var preview = cache.Preview("0 9 * *", timeZoneId: null, _Utc(2026, 4, 22, 0, 0), count: 5);

        preview.IsValid.Should().BeFalse();
        preview.ValidationMessage.Should().NotBeNullOrWhiteSpace();
        preview.NextFiresUtc.Should().BeEmpty();
        preview.Reading.Should().BeNull();
    }

    [Fact]
    public void should_return_a_validation_message_for_a_zone_this_host_cannot_resolve()
    {
        var cache = new CronScheduleCache(TimeZoneInfo.Utc);

        var preview = cache.Preview("0 0 9 * * *", "Nowhere/Imaginary", _Utc(2026, 4, 22, 0, 0), count: 3);

        preview.ValidationMessage.Should().Contain("Nowhere/Imaginary");
        preview.NextFiresUtc.Should().BeEmpty();
        preview.Reading.Should().Be("At 09:00, every day");
    }

    [Fact]
    public void should_return_a_validation_message_for_an_expression_that_never_fires_again()
    {
        var cache = new CronScheduleCache(TimeZoneInfo.Utc);

        var preview = cache.Preview("0 0 0 30 2 *", timeZoneId: null, _Utc(2026, 4, 22, 0, 0), count: 3);

        preview.ValidationMessage.Should().Be("The expression never fires again.");
        preview.NextFiresUtc.Should().BeEmpty();
    }

    [Theory]
    [InlineData("0 */15 * * * *", "Every 15 minutes")]
    [InlineData("0 30 * * * *", "At minute 30 of every hour")]
    [InlineData("*/10 * * * * *", "Every 10 seconds")]
    [InlineData("0 0 3 1 * *", "At 03:00, on day 1 of the month")]
    [InlineData("0 15 6 * 1,7 0", "At 06:15, in January, July, on Sunday")]
    [InlineData("0 0 9-17 * * *", "At minute 0, hours 9 through 17")]
    [InlineData("0 0 0 1-15 * *", "At 00:00, on days 1 through 15 of the month")]
    public void should_read_an_expression_in_plain_words(string expression, string reading)
    {
        CronExpressionReading.Describe(expression).Should().Be(reading);
    }

    private static DateTime _Utc(int year, int month, int day, int hour, int minute)
    {
        return new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Utc);
    }
}
