// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Checks;

namespace Tests;

public class IsOneOfTests
{
    [Fact]
    public void is_one_of_int_should_return_argument_when_valid()
    {
        // given
        const int argument = 5;
        var validValues = new List<int> { 1, 2, 5, 7 };

        // when & then
        Argument.IsOneOf(argument, validValues).Should().Be(argument);
    }

    [Fact]
    public void is_one_of_int_should_throw_when_invalid()
    {
        // given
        const int argument = 6;
        var validValues = new List<int> { 1, 2, 5, 7 };

        // when
        var action = () => Argument.IsOneOf(argument, validValues);

        // then
        action.Should().ThrowExactly<ArgumentException>();
    }

    [Fact]
    public void is_one_of_long_should_return_argument_when_valid()
    {
        // given
        const long argument = 10L;
        var validValues = new List<long> { 5L, 10L, 20L };

        // when & then
        Argument.IsOneOf(argument, validValues).Should().Be(argument);
    }

    [Fact]
    public void is_one_of_decimal_should_return_argument_when_valid()
    {
        // given
        const decimal argument = 1.5m;
        var validValues = new List<decimal> { 1.0m, 1.5m, 2.0m };

        // when & then
        Argument.IsOneOf(argument, validValues).Should().Be(argument);
    }

    [Fact]
    public void is_one_of_float_should_return_argument_when_valid()
    {
        // given
        const float argument = 2.5f;
        var validValues = new List<float> { 1.0f, 2.5f, 3.0f };

        // when & then
        Argument.IsOneOf(argument, validValues).Should().Be(argument);
    }

    [Fact]
    public void is_one_of_float_should_throw_when_invalid()
    {
        // given
        const float argument = 4.5f;
        var validValues = new List<float> { 1.0f, 2.5f, 3.0f };

        // when
        var action = () => Argument.IsOneOf(argument, validValues);

        // then
        action.Should().ThrowExactly<ArgumentException>();
    }

    [Fact]
    public void is_one_of_string_should_return_argument_when_valid()
    {
        // given
        const string argument = "apple";
        var validValues = new List<string> { "apple", "banana", "cherry" };

        // when & then
        Argument.IsOneOf(argument, validValues).Should().Be(argument);
    }

    [Fact]
    public void is_one_of_string_should_throw_when_invalid()
    {
        // given
        const string argument = "invalid";
        var validValues = new List<string> { "zad", "framework", "storm" };

        // when
        var action = () => Argument.IsOneOf(argument, validValues);

        // then
        action.Should().ThrowExactly<ArgumentException>();
    }

    [Fact]
    public void is_one_of_string_should_respect_string_comparer()
    {
        // given
        const string argument = "framework";
        var validValues = new List<string> { "zad", "framework", "storm" };

        // when & then
        Argument.IsOneOf(argument, validValues, StringComparer.OrdinalIgnoreCase);
    }
}
