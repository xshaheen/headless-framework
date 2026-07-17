// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Constants;
using Humanizer;

namespace Tests.Core;

public sealed class DateTimeOffsetExtensionsTests
{
    private static readonly TimeSpan _Offset = TimeSpan.FromHours(3);

    [Fact]
    public void should_convert_to_target_timezone_when_to_timezone()
    {
        // given
        var dateTimeOffset = new DateTimeOffset(2021, 6, 15, 12, 0, 0, TimeSpan.Zero);
        var targetTimezone = TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time");

        // when
        var result = dateTimeOffset.ToTimezone(targetTimezone);

        // then
        result.Offset.Should().Be(targetTimezone.GetUtcOffset(dateTimeOffset));
    }

    [Fact]
    public void should_return_midnight_when_clear_time()
    {
        // given
        var dateTimeOffset = new DateTimeOffset(2021, 6, 15, 14, 30, 45, 123, _Offset);

        // when
        var result = dateTimeOffset.ClearTime();

        // then
        result.Should().Be(new DateTimeOffset(2021, 6, 15, 0, 0, 0, _Offset));
    }

    [Fact]
    public void should_return_midnight_when_get_start_of_day()
    {
        // given
        var dateTimeOffset = new DateTimeOffset(2021, 6, 15, 14, 30, 45, TimeSpan.Zero);

        // when
        var result = dateTimeOffset.GetStartOfDay(_Offset);

        // then
        result.Hour.Should().Be(0);
        result.Minute.Should().Be(0);
        result.Second.Should().Be(0);
        result.Offset.Should().Be(_Offset);
    }

    [Fact]
    public void should_return_end_of_day_when_get_end_of_day()
    {
        // given
        var dateTimeOffset = new DateTimeOffset(2021, 6, 15, 14, 30, 45, TimeSpan.Zero);

        // when
        var result = dateTimeOffset.GetEndOfDay(_Offset);

        // then
        result.Hour.Should().Be(23);
        result.Minute.Should().Be(59);
        result.Second.Should().Be(59);
        result.Millisecond.Should().Be(999);
        result.Offset.Should().Be(_Offset);
    }

    [Fact]
    public void should_return_first_day_when_get_start_of_month()
    {
        // given
        var dateTimeOffset = new DateTimeOffset(2021, 6, 15, 14, 30, 45, TimeSpan.Zero);

        // when
        var result = dateTimeOffset.GetStartOfMonth(_Offset);

        // then
        result.Day.Should().Be(1);
        result.Month.Should().Be(6);
        result.Hour.Should().Be(0);
        result.Minute.Should().Be(0);
        result.Second.Should().Be(0);
        result.Offset.Should().Be(_Offset);
    }

    [Fact]
    public void should_return_last_day_when_get_end_of_month()
    {
        // given
        var dateTimeOffset = new DateTimeOffset(2021, 6, 15, 14, 30, 45, TimeSpan.Zero);

        // when
        var result = dateTimeOffset.GetEndOfMonth(_Offset);

        // then
        result.Day.Should().Be(30); // June has 30 days
        result.Month.Should().Be(6);
        result.Hour.Should().Be(23);
        result.Minute.Should().Be(59);
        result.Second.Should().Be(59);
        result.Offset.Should().Be(_Offset);
    }

    [Fact]
    public void should_handle_offset_that_crosses_a_month_boundary_when_get_end_of_month()
    {
        // given - Jan 31 23:00 UTC; a +2h offset shifts the local calendar date into February
        var dateTimeOffset = new DateTimeOffset(2021, 1, 31, 23, 0, 0, TimeSpan.Zero);

        // when - must use February's day count, not build an invalid "February 31"
        var result = dateTimeOffset.GetEndOfMonth(TimeSpan.FromHours(2));

        // then - end of February (2021 has 28 days)
        result.Month.Should().Be(2);
        result.Day.Should().Be(28);
        result.Hour.Should().Be(23);
        result.Minute.Should().Be(59);
        result.Offset.Should().Be(TimeSpan.FromHours(2));
    }

    [Fact]
    public void should_return_jan_1_when_get_start_of_year()
    {
        // given
        var dateTimeOffset = new DateTimeOffset(2021, 6, 15, 14, 30, 45, TimeSpan.Zero);

        // when
        var result = dateTimeOffset.GetStartOfYear(_Offset);

        // then
        result.Month.Should().Be(1);
        result.Day.Should().Be(1);
        result.Hour.Should().Be(0);
        result.Minute.Should().Be(0);
        result.Second.Should().Be(0);
        result.Offset.Should().Be(_Offset);
    }

    [Fact]
    public void should_return_dec_31_when_get_end_of_year()
    {
        // given
        var dateTimeOffset = new DateTimeOffset(2021, 6, 15, 14, 30, 45, TimeSpan.Zero);

        // when
        var result = dateTimeOffset.GetEndOfYear(_Offset);

        // then
        result.Month.Should().Be(12);
        result.Day.Should().Be(31);
        result.Hour.Should().Be(23);
        result.Minute.Should().Be(59);
        result.Second.Should().Be(59);
        result.Offset.Should().Be(_Offset);
    }

    [Fact]
    public void should_remove_sub_millisecond_precision_when_truncate_to_milliseconds()
    {
        // given
        var dateTimeOffset = new DateTimeOffset(2021, 6, 15, 14, 30, 45, 123, _Offset);

        // when
        var result = dateTimeOffset.TruncateToMilliseconds();

        // then
        result.Should().Be(new DateTimeOffset(2021, 6, 15, 14, 30, 45, 123, _Offset));
    }

    [Fact]
    public void should_remove_milliseconds_when_truncate_to_seconds()
    {
        // given
        var dateTimeOffset = new DateTimeOffset(2021, 6, 15, 14, 30, 45, 123, _Offset);

        // when
        var result = dateTimeOffset.TruncateToSeconds();

        // then
        result.Should().Be(new DateTimeOffset(2021, 6, 15, 14, 30, 45, 0, _Offset));
    }

    [Fact]
    public void should_remove_seconds_when_truncate_to_minutes()
    {
        // given
        var dateTimeOffset = new DateTimeOffset(2021, 6, 15, 14, 30, 45, 123, _Offset);

        // when
        var result = dateTimeOffset.TruncateToMinutes();

        // then
        result.Should().Be(new DateTimeOffset(2021, 6, 15, 14, 30, 0, 0, _Offset));
    }

    [Fact]
    public void should_remove_minutes_when_truncate_to_hours()
    {
        // given
        var dateTimeOffset = new DateTimeOffset(2021, 6, 15, 14, 30, 45, 123, _Offset);

        // when
        var result = dateTimeOffset.TruncateToHours();

        // then
        result.Should().Be(new DateTimeOffset(2021, 6, 15, 14, 0, 0, 0, _Offset));
    }

    public static readonly TheoryData<DateTimeOffset, TimeSpan, DateTimeOffset> SafeAddData = new()
    {
        { DateTimeOffset.MinValue, -15.Minutes(), DateTimeOffset.MinValue },
        { DateTimeOffset.MaxValue, 15.Minutes(), DateTimeOffset.MaxValue },
        { DateTimeOffset.MaxValue.Add(-10.Minutes()), 15.Minutes(), DateTimeOffset.MaxValue },
        { DateTimeOffset.MinValue.Add(10.Minutes()), -15.Minutes(), DateTimeOffset.MinValue },
        {
            new DateTimeOffset(2021, 1, 1, 0, 0, 0, TimeSpan.Zero),
            10.Hours(),
            new DateTimeOffset(2021, 1, 1, 10, 0, 0, TimeSpan.Zero)
        },
        {
            new DateTimeOffset(2021, 1, 1, 10, 0, 0, TimeSpan.Zero),
            -10.Hours(),
            new DateTimeOffset(2021, 1, 1, 0, 0, 0, TimeSpan.Zero)
        },
    };

    [Theory]
    [MemberData(nameof(SafeAddData))]
    public void should_clamp_to_boundaries_when_safe_add(
        DateTimeOffset dateTime,
        TimeSpan timeSpan,
        DateTimeOffset expected
    )
    {
        // when
        var result = dateTime.SafeAdd(timeSpan);

        // then
        result.Should().Be(expected);
    }

    [Fact]
    public void should_clamp_to_max_without_overflowing_for_extreme_positive_span_when_safe_add()
    {
        // given - date.Ticks + value.Ticks overflows long before the bounds check in the old code,
        // wrapping negative and wrongly returning MinValue instead of clamping to MaxValue
        var dateTimeOffset = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

        // when
        var result = dateTimeOffset.SafeAdd(TimeSpan.MaxValue);

        // then
        result.Should().Be(DateTimeOffset.MaxValue);
    }

    [Fact]
    public void should_round_down_to_interval_when_floor()
    {
        // given
        var dateTimeOffset = new DateTimeOffset(2021, 1, 1, 12, 31, 45, 123, _Offset);

        // when
        var result = dateTimeOffset.Floor(TimeSpan.FromMinutes(15));

        // then
        result.Should().Be(new DateTimeOffset(2021, 1, 1, 12, 30, 0, _Offset));
    }

    [Fact]
    public void should_round_up_to_interval_when_ceiling()
    {
        // given
        var dateTimeOffset = new DateTimeOffset(2021, 1, 1, 12, 31, 45, 123, _Offset);

        // when
        var result = dateTimeOffset.Ceiling(TimeSpan.FromMinutes(15));

        // then
        result.Should().Be(new DateTimeOffset(2021, 1, 1, 12, 45, 0, _Offset));
    }

    [Fact]
    public void should_convert_correctly_when_to_date_only()
    {
        // given
        var dateTimeOffset = new DateTimeOffset(2021, 6, 15, 14, 30, 45, _Offset);

        // when
        var result = dateTimeOffset.ToDateOnly();

        // then
        result.Should().Be(new DateOnly(2021, 6, 15));
    }

    [Fact]
    public void should_convert_correctly_when_to_utc_date_only()
    {
        // given
        var dateTimeOffset = new DateTimeOffset(2021, 6, 15, 2, 30, 45, TimeSpan.FromHours(5));

        // when
        var result = dateTimeOffset.ToUtcDateOnly();

        // then
        result.Should().Be(new DateOnly(2021, 6, 14)); // UTC is 5 hours behind
    }

    [Fact]
    public void should_convert_correctly_when_to_time_only()
    {
        // given
        var dateTimeOffset = new DateTimeOffset(2021, 6, 15, 14, 30, 45, _Offset);

        // when
        var result = dateTimeOffset.ToTimeOnly();

        // then
        result.Should().Be(new TimeOnly(14, 30, 45));
    }

    [Fact]
    public void should_convert_correctly_when_to_utc_time_only()
    {
        // given
        var dateTimeOffset = new DateTimeOffset(2021, 6, 15, 14, 30, 45, TimeSpan.FromHours(3));

        // when
        var result = dateTimeOffset.ToUtcTimeOnly();

        // then
        result.Should().Be(new TimeOnly(11, 30, 45)); // UTC is 3 hours behind
    }

    [Fact]
    public void should_convert_to_egypt_time_zone()
    {
        // given
        var utc = new DateTimeOffset(2024, 6, 15, 12, 0, 0, TimeSpan.Zero);

        // when
        var result = utc.ToEgyptTimeZone();

        // then
        result.Offset.Should().Be(TimezoneConstants.EgyptTimeZone.GetUtcOffset(utc));
        result.UtcDateTime.Should().Be(utc.UtcDateTime);
    }

    [Fact]
    public void should_convert_to_saudi_arabia_time_zone()
    {
        // given
        var utc = new DateTimeOffset(2024, 6, 15, 12, 0, 0, TimeSpan.Zero);

        // when
        var result = utc.ToSaudiArabiaTimeZone();

        // then
        result.Offset.Should().Be(TimezoneConstants.SaudiArabiaTimeZone.GetUtcOffset(utc));
        result.UtcDateTime.Should().Be(utc.UtcDateTime);
    }

    [Fact]
    public void should_preserve_instant_when_converting_to_egypt_time_zone()
    {
        // given
        var utc = new DateTimeOffset(2024, 1, 15, 10, 30, 45, TimeSpan.Zero);

        // when
        var result = utc.ToEgyptTimeZone();

        // then
        result.ToUniversalTime().Should().Be(utc.ToUniversalTime());
    }

    [Fact]
    public void should_preserve_instant_when_converting_to_saudi_arabia_time_zone()
    {
        // given
        var utc = new DateTimeOffset(2024, 3, 10, 14, 45, 0, TimeSpan.Zero);

        // when
        var result = utc.ToSaudiArabiaTimeZone();

        // then
        result.ToUniversalTime().Should().Be(utc.ToUniversalTime());
    }

    [Fact]
    public void should_return_zero_offset_input_unchanged_when_normalize_to_utc()
    {
        // given
        var utc = new DateTimeOffset(2024, 11, 27, 12, 0, 0, TimeSpan.Zero);

        // when
        var result = utc.NormalizeToUtc();

        // then
        result.Offset.Should().Be(TimeSpan.Zero);
        result.Should().Be(utc);
    }

    [Fact]
    public void should_zero_a_positive_offset_while_preserving_the_instant_when_normalize_to_utc()
    {
        // given
        var withOffset = new DateTimeOffset(2024, 11, 27, 15, 0, 0, _Offset);

        // when
        var result = withOffset.NormalizeToUtc();

        // then - offset becomes zero, the represented instant is unchanged
        result.Offset.Should().Be(TimeSpan.Zero);
        result.Should().Be(withOffset);
        result.UtcDateTime.Should().Be(withOffset.UtcDateTime);
    }

    [Fact]
    public void should_zero_a_negative_offset_while_preserving_the_instant_when_normalize_to_utc()
    {
        // given
        var withOffset = new DateTimeOffset(2024, 11, 27, 5, 0, 0, TimeSpan.FromHours(-4));

        // when
        var result = withOffset.NormalizeToUtc();

        // then
        result.Offset.Should().Be(TimeSpan.Zero);
        result.Should().Be(withOffset);
        result.UtcDateTime.Should().Be(withOffset.UtcDateTime);
    }
}
