// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Tests;

public sealed class FloatingPointSpecialValuesTests
{
    // IsPositive with special values

    [Fact]
    public void is_positive_with_positive_infinity_throws()
    {
        // given
        const double argument = double.PositiveInfinity;

        // when
        Action action = () => Argument.IsPositive(argument);

        // then
        action.Should().ThrowExactly<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void is_positive_with_negative_infinity_throws()
    {
        // given
        const double argument = double.NegativeInfinity;

        // when
        Action action = () => Argument.IsPositive(argument);

        // then
        action.Should().ThrowExactly<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void is_positive_with_nan_throws()
    {
        // given
        const double argument = double.NaN;

        // when
        Action action = () => Argument.IsPositive(argument);

        // then
        action.Should().ThrowExactly<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void is_positive_with_epsilon_returns_value()
    {
        // given
        const double value = double.Epsilon;

        // when & then
        Argument.IsPositive(value).Should().Be(value);
    }

    [Fact]
    public void is_positive_float_with_positive_infinity_throws()
    {
        // given
        const float argument = float.PositiveInfinity;

        // when
        Action action = () => Argument.IsPositive(argument);

        // then
        action.Should().ThrowExactly<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void is_positive_float_with_negative_infinity_throws()
    {
        // given
        const float argument = float.NegativeInfinity;

        // when
        Action action = () => Argument.IsPositive(argument);

        // then
        action.Should().ThrowExactly<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void is_positive_float_with_nan_throws()
    {
        // given
        const float argument = float.NaN;

        // when
        Action action = () => Argument.IsPositive(argument);

        // then
        action.Should().ThrowExactly<ArgumentOutOfRangeException>();
    }

    // IsNegative with special values

    [Fact]
    public void is_negative_with_positive_infinity_throws()
    {
        // given
        const double argument = double.PositiveInfinity;

        // when
        Action action = () => Argument.IsNegative(argument);

        // then
        action.Should().ThrowExactly<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void is_negative_with_negative_infinity_throws()
    {
        // given
        const double argument = double.NegativeInfinity;

        // when
        Action action = () => Argument.IsNegative(argument);

        // then
        action.Should().ThrowExactly<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void is_negative_with_nan_throws()
    {
        // given
        const double argument = double.NaN;

        // when
        Action action = () => Argument.IsNegative(argument);

        // then
        action.Should().ThrowExactly<ArgumentOutOfRangeException>();
    }

    // IsNotNaN tests

    [Fact]
    public void is_not_nan_with_nan_throws()
    {
        // given
        const double argument = double.NaN;

        // when
        Action action = () => Argument.IsNotNaN(argument);

        // then
        action.Should().ThrowExactly<ArgumentException>();
    }

    [Fact]
    public void is_not_nan_with_normal_value_returns_value()
    {
        // given
        const double value = 42.5;

        // when & then
        Argument.IsNotNaN(value).Should().Be(value);
    }

    [Fact]
    public void is_not_nan_with_infinity_returns_value()
    {
        // given
        const double positiveInfinity = double.PositiveInfinity;
        const double negativeInfinity = double.NegativeInfinity;

        // when & then
        Argument.IsNotNaN(positiveInfinity).Should().Be(positiveInfinity);
        Argument.IsNotNaN(negativeInfinity).Should().Be(negativeInfinity);
    }

    [Fact]
    public void is_not_nan_float_with_nan_throws()
    {
        // given
        const float argument = float.NaN;

        // when
        Action action = () => Argument.IsNotNaN(argument);

        // then
        action.Should().ThrowExactly<ArgumentException>();
    }

    // Boundary values

    [Fact]
    public void is_positive_with_double_max_value_returns_value()
    {
        // given
        const double value = double.MaxValue;

        // when & then
        Argument.IsPositive(value).Should().Be(value);
    }

    [Fact]
    public void is_negative_with_double_min_value_returns_value()
    {
        // given
        const double value = double.MinValue;

        // when & then
        Argument.IsNegative(value).Should().Be(value);
    }

    [Fact]
    public void is_positive_with_int_max_value_returns_value()
    {
        // given
        const int value = int.MaxValue;

        // when & then
        Argument.IsPositive(value).Should().Be(value);
    }

    [Fact]
    public void is_negative_with_int_min_value_returns_value()
    {
        // given
        const int value = int.MinValue;

        // when & then
        Argument.IsNegative(value).Should().Be(value);
    }

    [Fact]
    public void is_positive_with_zero_throws()
    {
        // given
        const int argument = 0;
        const double doubleArg = 0.0;
        const float floatArg = 0.0f;

        // when
        Action actionInt = () => Argument.IsPositive(argument);
        Action actionDouble = () => Argument.IsPositive(doubleArg);
        Action actionFloat = () => Argument.IsPositive(floatArg);

        // then
        actionInt.Should().ThrowExactly<ArgumentOutOfRangeException>();
        actionDouble.Should().ThrowExactly<ArgumentOutOfRangeException>();
        actionFloat.Should().ThrowExactly<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void is_negative_with_zero_throws()
    {
        // given
        const int argument = 0;
        const double doubleArg = 0.0;
        const float floatArg = 0.0f;

        // when
        Action actionInt = () => Argument.IsNegative(argument);
        Action actionDouble = () => Argument.IsNegative(doubleArg);
        Action actionFloat = () => Argument.IsNegative(floatArg);

        // then
        actionInt.Should().ThrowExactly<ArgumentOutOfRangeException>();
        actionDouble.Should().ThrowExactly<ArgumentOutOfRangeException>();
        actionFloat.Should().ThrowExactly<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void is_positive_or_zero_with_zero_returns_value()
    {
        // given
        const int intValue = 0;
        const double doubleValue = 0.0;
        const float floatValue = 0.0f;

        // when & then
        Argument.IsPositiveOrZero(intValue).Should().Be(intValue);
        Argument.IsPositiveOrZero(doubleValue).Should().Be(doubleValue);
        Argument.IsPositiveOrZero(floatValue).Should().Be(floatValue);
    }

    [Fact]
    public void is_negative_or_zero_with_zero_returns_value()
    {
        // given
        const int intValue = 0;
        const double doubleValue = 0.0;
        const float floatValue = 0.0f;

        // when & then
        Argument.IsNegativeOrZero(intValue).Should().Be(intValue);
        Argument.IsNegativeOrZero(doubleValue).Should().Be(doubleValue);
        Argument.IsNegativeOrZero(floatValue).Should().Be(floatValue);
    }
}
