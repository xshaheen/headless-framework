using Headless.Generator.Primitives;

namespace Tests.Extensions;

public sealed class DateOnlyExtensionsTests
{
    [Fact]
    public void should_convert_dateonly_to_datetime_with_minvalue_time_and_local_kind()
    {
        // given
        var dateOnly = new DateOnly(2024, 6, 15);

        // when
        var result = dateOnly.ToDateTime();

        // then
        result.Year.Should().Be(2024);
        result.Month.Should().Be(6);
        result.Day.Should().Be(15);
        result.Hour.Should().Be(0);
        result.Minute.Should().Be(0);
        result.Second.Should().Be(0);
        result.Millisecond.Should().Be(0);
        result.Kind.Should().Be(DateTimeKind.Local);
    }

    [Fact]
    public void should_convert_timeonly_to_datetime_with_correct_ticks_and_local_kind()
    {
        // given
        var timeOnly = new TimeOnly(14, 30, 45, 123);

        // when
        var result = timeOnly.ToDateTime();

        // then
        result.Hour.Should().Be(14);
        result.Minute.Should().Be(30);
        result.Second.Should().Be(45);
        result.Millisecond.Should().Be(123);
        result.Kind.Should().Be(DateTimeKind.Local);
        result.Ticks.Should().Be(timeOnly.Ticks);
    }
}
