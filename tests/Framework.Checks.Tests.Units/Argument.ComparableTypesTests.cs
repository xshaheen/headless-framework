// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Checks;
using Tests.Helpers;

namespace Tests;

public sealed class ArgumentComparableTypesTests
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
        InputsTestArgument inputsTestArgument = new();
        InputsTestArgument expected = new()
        {
            IntValue = 20,
        };

        // when & then
        Assert.Throws<ArgumentOutOfRangeException>(() => Argument.IsEqualTo(inputsTestArgument, expected));
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
        // when & then
        Assert.Throws<ArgumentOutOfRangeException>(() => Argument.IsLessThanOrEqualTo(15, 10));
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
        // when & then
        Assert.Throws<ArgumentOutOfRangeException>(() => Argument.IsGreaterThanOrEqualTo(3, 5));
    }

    [Fact]
    public void is_less_than_should_return_argument_when_less_than()
    {
        // given
        InputsTestArgument inputsTestArgument = new()
        {
            IntValue = 1,
            DecimalValue = 1m,
            DoubleValue = 1,
            FloatValue = 1,
            TimeSpanValue = TimeSpan.Zero,
        };
        InputsTestArgument expected = new();

        // when & then
        Argument.IsLessThan(inputsTestArgument, expected).Should().Be(inputsTestArgument);
    }

    [Fact]
    public void is_less_than_should_throw_argument_out_of_range_exception_when_greater_than_or_equal()
    {
        // when & then
        Assert.Throws<ArgumentOutOfRangeException>(() => Argument.IsLessThan(5, 5));
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
        // when & then
        Assert.Throws<ArgumentOutOfRangeException>(() => Argument.IsGreaterThan(3, 5));
    }

    [Fact]
    public void range_should_throw_argument_exception_when_minimum_is_greater_than_maximum()
    {
        // when & then
        Assert.Throws<ArgumentException>(() => Argument.Range(10, 5));
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
        // when & then
        Assert.Throws<ArgumentOutOfRangeException>(() => Argument.IsInclusiveBetween(15, 3, 10));
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
        // when & then
        Assert.Throws<ArgumentOutOfRangeException>(() => Argument.IsExclusiveBetween(1, 3, 10));
    }
}
