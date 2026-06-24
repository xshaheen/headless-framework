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

    [Theory]
    [InlineData(DateTimeKind.Utc)]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public void clear_time_should_preserve_kind(DateTimeKind kind)
    {
        // given - DateTime equality ignores Kind, so Kind must be asserted explicitly
        var dateTime = new DateTime(2021, 1, 1, 12, 30, 45, kind);

        // when
        var result = dateTime.ClearTime();

        // then
        result.Kind.Should().Be(kind);
        result.Should().Be(new DateTime(2021, 1, 1));
    }

    [Theory]
    [InlineData(DateTimeKind.Utc)]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public void truncate_helpers_should_preserve_kind(DateTimeKind kind)
    {
        // given - sub-millisecond ticks so every truncation level actually changes the value
        var dateTime = new DateTime(2021, 1, 1, 12, 30, 45, 123, kind).AddTicks(4567);

        // when / then
        dateTime.TruncateToMilliseconds().Kind.Should().Be(kind);
        dateTime.TruncateToSeconds().Kind.Should().Be(kind);
        dateTime.TruncateToMinutes().Kind.Should().Be(kind);
        dateTime.TruncateToHours().Kind.Should().Be(kind);
    }

    [Fact]
    public void truncate_to_milliseconds_should_floor_sub_millisecond_ticks()
    {
        // given - 123 ms plus 4567 sub-millisecond ticks
        var dateTime = new DateTime(2021, 1, 1, 12, 30, 45, 123).AddTicks(4567);

        // when
        var result = dateTime.TruncateToMilliseconds();

        // then
        result.Should().Be(new DateTime(2021, 1, 1, 12, 30, 45, 123));
    }

    [Fact]
    public void safe_add_should_clamp_to_max_without_overflowing_for_extreme_positive_span()
    {
        // given - date.Ticks + value.Ticks overflows long before the bounds check in the old code,
        // wrapping negative and wrongly returning MinValue instead of clamping to MaxValue
        var dateTime = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        // when
        var result = dateTime.SafeAdd(TimeSpan.MaxValue);

        // then
        result.Should().Be(DateTime.MaxValue);
    }
}
