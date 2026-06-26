// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;

namespace Tests.Core;

public sealed class TimeUnitTests
{
    [Theory]
    [InlineData("5m", 0, 0, 5)]
    [InlineData("2h", 0, 2, 0)]
    [InlineData("1d", 1, 0, 0, 0)]
    [InlineData("500ms", 0, 0, 0, 0, 500)]
    [InlineData("30s", 0, 0, 0, 30)]
    public void parse_valid_input_should_return_correct_timespan(
        string value,
        int expectedDays = 0,
        int expectedHours = 0,
        int expectedMinutes = 0,
        int expectedSeconds = 0,
        int expectedMilliseconds = 0
    )
    {
        // when
        var result = TimeUnit.Parse(value);

        // then
        result
            .Should()
            .Be(new TimeSpan(expectedDays, expectedHours, expectedMinutes, expectedSeconds, expectedMilliseconds));
    }

    [Theory]
    [InlineData(null, "Required argument \"value\" was null. (Parameter 'value')")]
    [InlineData("", "Required argument \"value\" was empty. (Parameter 'value')")]
    public void parse_invalid_input_should_throw_argument_exception(string? value, string expectedMessage)
    {
        // when
        Action action = () => TimeUnit.Parse(value!);

        // then
        action.Should().Throw<ArgumentException>().WithMessage(expectedMessage);
    }

    [Theory]
    [InlineData("5m", true, 0, 0, 5)]
    [InlineData("2h", true, 0, 2, 0)]
    [InlineData("ddddd", false, 0, 0, 0)]
    [InlineData("mm", false, 0, 0, 0)]
    [InlineData("hhhh", false, 0, 0, 0)]
    [InlineData("xnanos", false, 0, 0, 0)]
    [InlineData("", false, 0, 0, 0)]
    [InlineData(null, false, 0, 0, 0)]
    public void try_parse_should_return_correct_result(
        string? value,
        bool expectedSuccess,
        int expectedDays = 0,
        int expectedHours = 0,
        int expectedMinutes = 0,
        int expectedSeconds = 0,
        int expectedMilliseconds = 0
    )
    {
        // when
        var success = TimeUnit.TryParse(value!, out var result);

        // then
        success.Should().Be(expectedSuccess);

        if (expectedSuccess)
        {
            result
                .Should()
                .Be(new TimeSpan(expectedDays, expectedHours, expectedMinutes, expectedSeconds, expectedMilliseconds));
        }
        else
        {
            result.Should().BeNull();
        }
    }

    [Theory]
    [InlineData("5m ")]
    [InlineData("  5m")]
    [InlineData(" 5m ")]
    public void parse_should_trim_surrounding_whitespace_for_minutes(string value)
    {
        // when
        var result = TimeUnit.Parse(value);

        // then - the minutes branch now tests the trimmed span, so trailing space parses
        result.Should().Be(TimeSpan.FromMinutes(5));
    }

    [Fact]
    public void parse_should_treat_nanoseconds_above_int_range_without_truncation()
    {
        // given - 3e11 nanos = 3e9 ticks = 300s; the old (int) cast truncated/overflowed this
        const string value = "300000000000nanos";

        // when
        var result = TimeUnit.Parse(value);

        // then
        result.Should().Be(TimeSpan.FromSeconds(300));
    }

    [Theory]
    [InlineData("2147483647h")] // int.MaxValue hours overflows TimeSpan
    [InlineData("2147483647d")] // int.MaxValue days overflows TimeSpan
    public void try_parse_should_return_false_when_value_exceeds_timespan_range(string value)
    {
        // when
        var success = TimeUnit.TryParse(value, out var result);

        // then - overflow surfaces as a failed parse, not an uncaught exception
        success.Should().BeFalse();
        result.Should().BeNull();
    }

    [Fact]
    public void parse_should_throw_argument_exception_when_value_exceeds_timespan_range()
    {
        // when
        var action = () => TimeUnit.Parse("2147483647h");

        // then
        action.Should().Throw<ArgumentException>();
    }
}
