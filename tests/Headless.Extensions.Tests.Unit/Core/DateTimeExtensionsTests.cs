using Humanizer;

namespace Tests.Core;

public sealed class DateTimeExtensionsTests
{
    [Fact]
    public void clear_time_test()
    {
        // given
        var dateTime = new DateTime(2021, 1, 1, 12, 30, 45, 123);

        // when
        var result = dateTime.ClearTime();

        // then
        result.Should().Be(new DateTime(2021, 1, 1));
    }

    [Fact]
    public void truncate_to_milliseconds_test()
    {
        // given
        var dateTime = new DateTime(2021, 1, 1, 12, 30, 45, 123);

        // when
        var result = dateTime.TruncateToMilliseconds();

        // then
        result.Should().Be(new DateTime(2021, 1, 1, 12, 30, 45, 123));
    }

    [Fact]
    public void truncate_to_seconds_test()
    {
        // given
        var dateTime = new DateTime(2021, 1, 1, 12, 30, 45, 123);

        // when
        var result = dateTime.TruncateToSeconds();

        // then
        result.Should().Be(new DateTime(2021, 1, 1, 12, 30, 45));
    }

    [Fact]
    public void truncate_to_minutes_test()
    {
        // given
        var dateTime = new DateTime(2021, 1, 1, 12, 30, 45, 123);

        // when
        var result = dateTime.TruncateToMinutes();

        // then
        result.Should().Be(new DateTime(2021, 1, 1, 12, 30, 0));
    }

    [Fact]
    public void truncate_to_hours_test()
    {
        // given
        var dateTime = new DateTime(2021, 1, 1, 12, 30, 45, 123);

        // when
        var result = dateTime.TruncateToHours();

        // then
        result.Should().Be(new DateTime(2021, 1, 1, 12, 0, 0));
    }

    public static readonly TheoryData<DateTime, TimeSpan, DateTime> SafeAddData = new()
    {
        { DateTime.MinValue, -15.Minutes(), DateTime.MinValue },
        { DateTime.MaxValue, 15.Minutes(), DateTime.MaxValue },
        { DateTime.MaxValue.Add(-10.Minutes()), 15.Minutes(), DateTime.MaxValue },
        { DateTime.MinValue.Add(10.Minutes()), -15.Minutes(), DateTime.MinValue },
        { new DateTime(2021, 1, 1), 10.Hours(), new DateTime(2021, 1, 1, 10, 0, 0, 0) },
        { new DateTime(2021, 1, 1, 10, 0, 0, 0), -10.Hours(), new DateTime(2021, 1, 1) },
    };

    [Theory]
    [MemberData(nameof(SafeAddData))]
    public void safe_add_test(DateTime dateTime, TimeSpan timeSpan, DateTime expected)
    {
        // when
        var result = dateTime.SafeAdd(timeSpan);

        // then
        result.Should().Be(expected);
    }

    [Fact]
    public void floor_test()
    {
        // given
        var dateTime = new DateTime(2021, 1, 1, 12, 31, 45, 123);

        // when
        var result = dateTime.Floor(TimeSpan.FromMinutes(15));

        // then
        result.Should().Be(new DateTime(2021, 1, 1, 12, 30, 0));
    }

    [Fact]
    public void ceiling_test()
    {
        // given
        var dateTime = new DateTime(2021, 1, 1, 12, 31, 45, 123);

        // when
        var result = dateTime.Ceiling(TimeSpan.FromMinutes(15));

        // then
        result.Should().Be(new DateTime(2021, 1, 1, 12, 45, 0));
    }
}
