// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Checks;
using Framework.Checks.Internals;
using Tests.Helpers;

namespace Tests;

public sealed class ComparableTypesTests
{
    [Fact]
    public void is_equal_to_should_return_expected_input_when_valid()
    {
        // given
        InputsTestArgument inputsTestArgument = new();
        InputsTestArgument expected = new();

        // when & then
        Argument.IsEqualTo(inputsTestArgument, expected).Should().Be(inputsTestArgument);
    }

    [Fact]
    public void is_equal_to_should_throw_out_of_range_exception_when_not_equal()
    {
        // given
        InputsTestArgument argument = new();
        InputsTestArgument expected = new() { IntValue = 20 };

        // when & then
        Assert.Throws<ArgumentOutOfRangeException>(() => Argument.IsEqualTo(argument, expected))
            .Message.Should().Contain($"\"{nameof(argument)}\" to be equal to {expected.ToInvariantString()}, but found {argument.ToInvariantString()}.");
    }

    [Fact]
    public void is_less_than_or_equal_to_should_return_argument_when_less_than_or_equal()
    {
        // given
        InputsTestArgument inputsTestArgument = new();
        InputsTestArgument expected = new();
        inputsTestArgument.IntValue = 1;

        // when & then
        Argument.IsLessThanOrEqualTo(inputsTestArgument, expected).Should().Be(inputsTestArgument);
    }

    [Fact]
    public void is_less_than_or_equal_to_should_throw_argument_out_of_range_exception_when_greater_than()
    {
        // gvien
        int argument = 15;
        int expected = 10;

        // when & then
        Assert.Throws<ArgumentOutOfRangeException>(() => Argument.IsLessThanOrEqualTo(argument, expected))
            .Message.Should().Contain($"\"{nameof(argument)}\" to be less than or equal to {expected}.");
    }

    [Fact]
    public void is_greater_than_or_equal_to_should_return_argument_when_greater_than_or_equal()
    {
        // when & then
        Argument.IsGreaterThanOrEqualTo(10, 5).Should().Be(10);
    }

    [Fact]
    public void is_greater_than_or_equal_to_should_throw_argument_out_of_range_exception_when_less_than()
    {
        // given
        int argument = 3;
        int expected = 5;

        // when & then
        Assert.Throws<ArgumentOutOfRangeException>(() => Argument.IsGreaterThanOrEqualTo(argument, expected))
            .Message.Should().Contain($"\"{nameof(argument)}\" to be greater than or equal to {expected}.");
    }

    [Fact]
    public void is_less_than_should_return_argument_when_less_than()
    {
        // given
        InputsTestArgument argument = new()
        {
            IntValue = 1,
            DecimalValue = 1m,
            DoubleValue = 1,
            FloatValue = 1,
            TimeSpanValue = TimeSpan.Zero,
        };

        InputsTestArgument expected = new();

        // when & then
        Argument.IsLessThan(argument, expected).Should().Be(argument);
    }

    [Fact]
    public void is_less_than_should_throw_argument_out_of_range_exception_when_greater_than_or_equal()
    {
        // given
        int argument = 5;
        int expected = 5;

        // when & then
        Assert.Throws<ArgumentOutOfRangeException>(() => Argument.IsLessThan(argument, expected))
            .Message.Should().Contain($"\"{nameof(argument)}\" to be less than {expected}.");
    }

    [Fact]
    public void is_greater_than_should_return_argument_when_greater_than()
    {
        // when
        Argument.IsGreaterThan(10, 5).Should().Be(10);
    }

    [Fact]
    public void is_greater_than_should_throw_argument_out_of_range_exception_when_less_than_or()
    {
        // given
        int argument = 3;
        int expected = 5;

        // when & then

        Assert.Throws<ArgumentOutOfRangeException>(() => Argument.IsGreaterThan(argument, expected))
            .Message.Should().Contain($"\"{nameof(argument)}\" to be greater than {expected}.");
    }

    [Fact]
    public void range_should_throw_argument_exception_when_minimum_is_greater_than_maximum()
    {
        // given
        int minimumValue = 10;
        int maximumValue = 5;

        // when & then
        Assert.Throws<ArgumentException>(() => Argument.Range(minimumValue, maximumValue))
            .Message.Should().Contain($"{nameof(minimumValue)} should be less or equal than {nameof(maximumValue)}");
    }
    [Fact]
    public void range_should_not_throw_when_minimum_is_less_than_maximum()
    {
        // Arrange
        int minimumValue = 5;
        int maximumValue = 10;

        // Act & Assert
        Argument.Range(minimumValue, maximumValue);
    }
    [Fact]
    public void is_inclusive_between_should_return_argument_when_in_range()
    {
        // when
        Argument.IsInclusiveBetween(5, 3, 10).Should().Be(5);
    }

    [Fact]
    public void is_inclusive_between_should_throw_argument_out_of_range_exception_when_out_of_range()
    {
        // given
        int argument = 5;
        int minimumValue = 10;
        int maximumValue = 20;

        // when
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            Argument.IsInclusiveBetween(argument, minimumValue, maximumValue)
        );

        // then
        exception.Message.Should().Contain($"The input {nameof(argument)}={argument} must be between {nameof(minimumValue)} and {nameof(maximumValue)} inclusive [{nameof(minimumValue)}, {nameof(maximumValue)}].");
    }

    [Fact]
    public void is_exclusive_between_should_return_argument_when_in_range()
    {
        // when & then
        Argument.IsExclusiveBetween(5, 3, 10).Should().Be(5);
    }

    [Fact]
    public void is_exclusive_between_should_throw_argument_out_of_range_exception_when_out_of_range()
    {
        // given
        int argument = 1;
        int minimumValue = 3;
        int maximumValue = 10;

        // when
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            Argument.IsExclusiveBetween(argument, minimumValue, maximumValue)
        );

        // then
        exception.Message.Should().Contain($"The input {nameof(argument)}={argument} must be between {nameof(minimumValue)} and {nameof(maximumValue)} exclusively ({nameof(minimumValue)}, {nameof(maximumValue)})");
    }
}
