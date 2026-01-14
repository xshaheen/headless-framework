// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Checks;

namespace Tests;

public class NullableOverloadTests
{
    // TimeSpan nullable tests

    [Fact]
    public void is_positive_with_null_time_span_returns_null()
    {
        // given
        TimeSpan? argument = null;

        // when & then
        Argument.IsPositive(argument).Should().BeNull();
    }

    [Fact]
    public void is_negative_with_null_time_span_returns_null()
    {
        // given
        TimeSpan? argument = null;

        // when & then
        Argument.IsNegative(argument).Should().BeNull();
    }

    [Fact]
    public void is_positive_or_zero_with_null_time_span_returns_null()
    {
        // given
        TimeSpan? argument = null;

        // when & then
        Argument.IsPositiveOrZero(argument).Should().BeNull();
    }

    [Fact]
    public void is_negative_or_zero_with_null_time_span_returns_null()
    {
        // given
        TimeSpan? argument = null;

        // when & then
        Argument.IsNegativeOrZero(argument).Should().BeNull();
    }

    [Fact]
    public void is_positive_with_positive_time_span_returns_value()
    {
        // given
        TimeSpan? value = TimeSpan.FromHours(1);

        // when & then
        Argument.IsPositive(value).Should().Be(value);
    }

    [Fact]
    public void is_negative_with_negative_time_span_returns_value()
    {
        // given
        TimeSpan? value = TimeSpan.FromHours(-1);

        // when & then
        Argument.IsNegative(value).Should().Be(value);
    }

    // Nullable short tests

    [Fact]
    public void is_positive_with_null_short_returns_null()
    {
        // given
        short? argument = null;

        // when & then
        Argument.IsPositive(argument).Should().BeNull();
    }

    [Fact]
    public void is_negative_with_null_short_returns_null()
    {
        // given
        short? argument = null;

        // when & then
        Argument.IsNegative(argument).Should().BeNull();
    }

    [Fact]
    public void is_positive_or_zero_with_null_short_returns_null()
    {
        // given
        short? argument = null;

        // when & then
        Argument.IsPositiveOrZero(argument).Should().BeNull();
    }

    [Fact]
    public void is_negative_or_zero_with_null_short_returns_null()
    {
        // given
        short? argument = null;

        // when & then
        Argument.IsNegativeOrZero(argument).Should().BeNull();
    }

    // Nullable int tests

    [Fact]
    public void is_positive_with_null_int_returns_null()
    {
        // given
        int? argument = null;

        // when & then
        Argument.IsPositive(argument).Should().BeNull();
    }

    [Fact]
    public void is_negative_with_null_int_returns_null()
    {
        // given
        int? argument = null;

        // when & then
        Argument.IsNegative(argument).Should().BeNull();
    }

    [Fact]
    public void is_positive_or_zero_with_null_int_returns_null()
    {
        // given
        int? argument = null;

        // when & then
        Argument.IsPositiveOrZero(argument).Should().BeNull();
    }

    [Fact]
    public void is_negative_or_zero_with_null_int_returns_null()
    {
        // given
        int? argument = null;

        // when & then
        Argument.IsNegativeOrZero(argument).Should().BeNull();
    }

    // Nullable long tests

    [Fact]
    public void is_positive_with_null_long_returns_null()
    {
        // given
        long? argument = null;

        // when & then
        Argument.IsPositive(argument).Should().BeNull();
    }

    [Fact]
    public void is_negative_with_null_long_returns_null()
    {
        // given
        long? argument = null;

        // when & then
        Argument.IsNegative(argument).Should().BeNull();
    }

    [Fact]
    public void is_positive_or_zero_with_null_long_returns_null()
    {
        // given
        long? argument = null;

        // when & then
        Argument.IsPositiveOrZero(argument).Should().BeNull();
    }

    [Fact]
    public void is_negative_or_zero_with_null_long_returns_null()
    {
        // given
        long? argument = null;

        // when & then
        Argument.IsNegativeOrZero(argument).Should().BeNull();
    }

    // Nullable float tests

    [Fact]
    public void is_positive_with_null_float_returns_null()
    {
        // given
        float? argument = null;

        // when & then
        Argument.IsPositive(argument).Should().BeNull();
    }

    [Fact]
    public void is_negative_with_null_float_returns_null()
    {
        // given
        float? argument = null;

        // when & then
        Argument.IsNegative(argument).Should().BeNull();
    }

    [Fact]
    public void is_positive_or_zero_with_null_float_returns_null()
    {
        // given
        float? argument = null;

        // when & then
        Argument.IsPositiveOrZero(argument).Should().BeNull();
    }

    [Fact]
    public void is_negative_or_zero_with_null_float_returns_null()
    {
        // given
        float? argument = null;

        // when & then
        Argument.IsNegativeOrZero(argument).Should().BeNull();
    }

    // Nullable double tests

    [Fact]
    public void is_positive_with_null_double_returns_null()
    {
        // given
        double? argument = null;

        // when & then
        Argument.IsPositive(argument).Should().BeNull();
    }

    [Fact]
    public void is_negative_with_null_double_returns_null()
    {
        // given
        double? argument = null;

        // when & then
        Argument.IsNegative(argument).Should().BeNull();
    }

    [Fact]
    public void is_positive_or_zero_with_null_double_returns_null()
    {
        // given
        double? argument = null;

        // when & then
        Argument.IsPositiveOrZero(argument).Should().BeNull();
    }

    [Fact]
    public void is_negative_or_zero_with_null_double_returns_null()
    {
        // given
        double? argument = null;

        // when & then
        Argument.IsNegativeOrZero(argument).Should().BeNull();
    }

    // Nullable decimal tests

    [Fact]
    public void is_positive_with_null_decimal_returns_null()
    {
        // given
        decimal? argument = null;

        // when & then
        Argument.IsPositive(argument).Should().BeNull();
    }

    [Fact]
    public void is_negative_with_null_decimal_returns_null()
    {
        // given
        decimal? argument = null;

        // when & then
        Argument.IsNegative(argument).Should().BeNull();
    }

    [Fact]
    public void is_positive_or_zero_with_null_decimal_returns_null()
    {
        // given
        decimal? argument = null;

        // when & then
        Argument.IsPositiveOrZero(argument).Should().BeNull();
    }

    [Fact]
    public void is_negative_or_zero_with_null_decimal_returns_null()
    {
        // given
        decimal? argument = null;

        // when & then
        Argument.IsNegativeOrZero(argument).Should().BeNull();
    }

    // ToInvariantString branch coverage tests

    [Fact]
    public void is_positive_with_datetime_creates_message_with_invariant_string()
    {
        // given
        DateTime argument = DateTime.MinValue;

        // when
        Action action = () => Argument.IsPositive(argument.Ticks);

        // then - this exercises DateTime formatting in ToInvariantString
        action.Should().ThrowExactly<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void is_positive_with_datetimeoffset_creates_message_with_invariant_string()
    {
        // given
        DateTimeOffset argument = DateTimeOffset.MinValue;

        // when
        Action action = () => Argument.IsPositive(argument.Ticks);

        // then - this exercises DateTimeOffset formatting in ToInvariantString
        action.Should().ThrowExactly<ArgumentOutOfRangeException>();
    }

    // Nullable overloads with custom messages to cover message branches

    [Fact]
    public void is_positive_with_null_int_and_custom_message_returns_null()
    {
        // given
        int? argument = null;
        string customMessage = "Custom error";

        // when & then
        Argument.IsPositive(argument, customMessage).Should().BeNull();
    }

    [Fact]
    public void is_positive_nullable_with_invalid_value_and_custom_message_throws()
    {
        // given
        int? argument = -5;
        string customMessage = "Value must be positive";

        // when
        Action action = () => Argument.IsPositive(argument, customMessage);

        // then
        action.Should().ThrowExactly<ArgumentOutOfRangeException>().WithMessage($"*{customMessage}*");
    }

    [Fact]
    public void is_negative_nullable_with_invalid_value_and_custom_message_throws()
    {
        // given
        int? argument = 5;
        string customMessage = "Value must be negative";

        // when
        Action action = () => Argument.IsNegative(argument, customMessage);

        // then
        action.Should().ThrowExactly<ArgumentOutOfRangeException>().WithMessage($"*{customMessage}*");
    }

    [Fact]
    public void is_positive_or_zero_nullable_with_invalid_value_and_custom_message_throws()
    {
        // given
        int? argument = -5;
        string customMessage = "Value must be positive or zero";

        // when
        Action action = () => Argument.IsPositiveOrZero(argument, customMessage);

        // then
        action.Should().ThrowExactly<ArgumentOutOfRangeException>().WithMessage($"*{customMessage}*");
    }

    [Fact]
    public void is_negative_or_zero_nullable_with_invalid_value_and_custom_message_throws()
    {
        // given
        int? argument = 5;
        string customMessage = "Value must be negative or zero";

        // when
        Action action = () => Argument.IsNegativeOrZero(argument, customMessage);

        // then
        action.Should().ThrowExactly<ArgumentOutOfRangeException>().WithMessage($"*{customMessage}*");
    }

    [Fact]
    public void is_positive_nullable_time_span_with_invalid_value_throws()
    {
        // given
        TimeSpan? argument = TimeSpan.FromSeconds(-10);

        // when
        Action action = () => Argument.IsPositive(argument);

        // then
        action.Should().ThrowExactly<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void is_negative_nullable_time_span_with_invalid_value_throws()
    {
        // given
        TimeSpan? argument = TimeSpan.FromSeconds(10);

        // when
        Action action = () => Argument.IsNegative(argument);

        // then
        action.Should().ThrowExactly<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void is_positive_or_zero_nullable_time_span_with_invalid_value_throws()
    {
        // given
        TimeSpan? argument = TimeSpan.FromSeconds(-10);

        // when
        Action action = () => Argument.IsPositiveOrZero(argument);

        // then
        action.Should().ThrowExactly<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void is_negative_or_zero_nullable_time_span_with_invalid_value_throws()
    {
        // given
        TimeSpan? argument = TimeSpan.FromSeconds(10);

        // when
        Action action = () => Argument.IsNegativeOrZero(argument);

        // then
        action.Should().ThrowExactly<ArgumentOutOfRangeException>();
    }
}
