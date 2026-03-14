// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Humanizer;

namespace Tests.Core;

public sealed class DateTimeOffsetExtensionsTests
{
    private static readonly TimeSpan _Offset = TimeSpan.FromHours(3);

    [Fact]
    public void to_timezone_should_convert_to_target_timezone()
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
    public void clear_time_should_return_midnight()
    {
        // given
        var dateTimeOffset = new DateTimeOffset(2021, 6, 15, 14, 30, 45, 123, _Offset);

        // when
        var result = dateTimeOffset.ClearTime();

        // then
        result.Should().Be(new DateTimeOffset(2021, 6, 15, 0, 0, 0, _Offset));
    }

    [Fact]
    public void get_start_of_day_should_return_midnight()
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
    public void get_end_of_day_should_return_end_of_day()
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
    public void get_start_of_month_should_return_first_day()
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
    public void get_end_of_month_should_return_last_day()
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
    public void get_start_of_year_should_return_jan_1()
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
    public void get_end_of_year_should_return_dec_31()
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
    public void truncate_to_milliseconds_should_remove_sub_millisecond_precision()
    {
        // given
        var dateTimeOffset = new DateTimeOffset(2021, 6, 15, 14, 30, 45, 123, _Offset);

        // when
        var result = dateTimeOffset.TruncateToMilliseconds();

        // then
        result.Should().Be(new DateTimeOffset(2021, 6, 15, 14, 30, 45, 123, _Offset));
    }

    [Fact]
    public void truncate_to_seconds_should_remove_milliseconds()
    {
        // given
        var dateTimeOffset = new DateTimeOffset(2021, 6, 15, 14, 30, 45, 123, _Offset);

        // when
        var result = dateTimeOffset.TruncateToSeconds();

        // then
        result.Should().Be(new DateTimeOffset(2021, 6, 15, 14, 30, 45, 0, _Offset));
    }

    [Fact]
    public void truncate_to_minutes_should_remove_seconds()
    {
        // given
        var dateTimeOffset = new DateTimeOffset(2021, 6, 15, 14, 30, 45, 123, _Offset);

        // when
        var result = dateTimeOffset.TruncateToMinutes();

        // then
        result.Should().Be(new DateTimeOffset(2021, 6, 15, 14, 30, 0, 0, _Offset));
    }

    [Fact]
    public void truncate_to_hours_should_remove_minutes()
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
    public void safe_add_should_clamp_to_boundaries(DateTimeOffset dateTime, TimeSpan timeSpan, DateTimeOffset expected)
    {
        // when
        var result = dateTime.SafeAdd(timeSpan);

        // then
        result.Should().Be(expected);
    }

    [Fact]
    public void floor_should_round_down_to_interval()
    {
        // given
        var dateTimeOffset = new DateTimeOffset(2021, 1, 1, 12, 31, 45, 123, _Offset);

        // when
        var result = dateTimeOffset.Floor(TimeSpan.FromMinutes(15));

        // then
        result.Should().Be(new DateTimeOffset(2021, 1, 1, 12, 30, 0, _Offset));
    }

    [Fact]
    public void ceiling_should_round_up_to_interval()
    {
        // given
        var dateTimeOffset = new DateTimeOffset(2021, 1, 1, 12, 31, 45, 123, _Offset);

        // when
        var result = dateTimeOffset.Ceiling(TimeSpan.FromMinutes(15));

        // then
        result.Should().Be(new DateTimeOffset(2021, 1, 1, 12, 45, 0, _Offset));
    }

    [Fact]
    public void to_date_only_should_convert_correctly()
    {
        // given
        var dateTimeOffset = new DateTimeOffset(2021, 6, 15, 14, 30, 45, _Offset);

        // when
        var result = dateTimeOffset.ToDateOnly();

        // then
        result.Should().Be(new DateOnly(2021, 6, 15));
    }

    [Fact]
    public void to_utc_date_only_should_convert_correctly()
    {
        // given
        var dateTimeOffset = new DateTimeOffset(2021, 6, 15, 2, 30, 45, TimeSpan.FromHours(5));

        // when
        var result = dateTimeOffset.ToUtcDateOnly();

        // then
        result.Should().Be(new DateOnly(2021, 6, 14)); // UTC is 5 hours behind
    }

    [Fact]
    public void to_time_only_should_convert_correctly()
    {
        // given
        var dateTimeOffset = new DateTimeOffset(2021, 6, 15, 14, 30, 45, _Offset);

        // when
        var result = dateTimeOffset.ToTimeOnly();

        // then
        result.Should().Be(new TimeOnly(14, 30, 45));
    }

    [Fact]
    public void to_utc_time_only_should_convert_correctly()
    {
        // given
        var dateTimeOffset = new DateTimeOffset(2021, 6, 15, 14, 30, 45, TimeSpan.FromHours(3));

        // when
        var result = dateTimeOffset.ToUtcTimeOnly();

        // then
        result.Should().Be(new TimeOnly(11, 30, 45)); // UTC is 3 hours behind
    }
}
