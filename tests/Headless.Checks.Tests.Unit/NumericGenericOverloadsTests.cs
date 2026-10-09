// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Numerics;
using Headless.Checks;

namespace Tests;

public sealed class NumericGenericOverloadsTests
{
    // IsPositive tests for generic INumber<T> overloads

    [Fact]
    public void is_positive_uint_with_zero_throws()
    {
        // given
        const uint argument = 0u;

        // when
        Action action = () => Argument.IsPositive(argument);

        // then
        action.Should().ThrowExactly<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void is_positive_big_integer_with_positive_value_does_not_throw()
    {
        // given
        BigInteger value = new(12345);

        // when & then
        Argument.IsPositive(value).Should().Be(value);
    }

    [Fact]
    public void is_positive_big_integer_with_negative_value_throws()
    {
        // given
        BigInteger argument = new(-12345);

        // when
        Action action = () => Argument.IsPositive(argument);

        // then
        action.Should().ThrowExactly<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void is_positive_half_with_positive_value_does_not_throw()
    {
        // given
        var value = (Half)5.5;

        // when & then
        Argument.IsPositive(value).Should().Be(value);
    }

    [Fact]
    public void is_positive_half_with_negative_value_throws()
    {
        // given
        var argument = (Half)(-5.5);

        // when
        Action action = () => Argument.IsPositive(argument);

        // then
        action.Should().ThrowExactly<ArgumentOutOfRangeException>();
    }

    // IsNegative tests for generic INumber<T> overloads

    [Fact]
    public void is_negative_big_integer_with_negative_value_does_not_throw()
    {
        // given
        BigInteger value = new(-12345);

        // when & then
        Argument.IsNegative(value).Should().Be(value);
    }

    [Fact]
    public void is_negative_big_integer_with_positive_value_throws()
    {
        // given
        BigInteger argument = new(12345);

        // when
        Action action = () => Argument.IsNegative(argument);

        // then
        action.Should().ThrowExactly<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void is_negative_half_with_negative_value_does_not_throw()
    {
        // given
        var value = (Half)(-5.5);

        // when & then
        Argument.IsNegative(value).Should().Be(value);
    }

    [Fact]
    public void is_negative_half_with_positive_value_throws()
    {
        // given
        var argument = (Half)5.5;

        // when
        Action action = () => Argument.IsNegative(argument);

        // then
        action.Should().ThrowExactly<ArgumentOutOfRangeException>();
    }

    // IsPositiveOrZero tests for generic INumber<T> overloads

    [Fact]
    public void is_positive_or_zero_half_with_zero_does_not_throw()
    {
        // given
        var value = Half.Zero;

        // when & then
        Argument.IsPositiveOrZero(value).Should().Be(value);
    }

    // IsNegativeOrZero tests for generic INumber<T> overloads

    [Fact]
    public void is_negative_or_zero_half_with_zero_does_not_throw()
    {
        // given
        var value = Half.Zero;

        // when & then
        Argument.IsNegativeOrZero(value).Should().Be(value);
    }
}
