// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Checks;
using Tests.Helpers;

namespace Tests;

public class ArgumentIsNegativeOrZeroTests
{
    private readonly InputsTestArgument _validValues = new()
    {
        IntValue = -5,
        DecimalValue = 0,
        DoubleValue = 0,
        FloatValue = -1f,
        TimeSpanValue = TimeSpan.FromDays(-5),
    };

    [Fact]
    public void is_negative_or_zero_should_return_same_value_when_positive()
    {
        Argument.IsNegativeOrZero(_validValues.IntValue).Should().Be(_validValues.IntValue);
        Argument.IsNegativeOrZero(_validValues.DecimalValue).Should().Be(_validValues.DecimalValue);
        Argument.IsNegativeOrZero(_validValues.DoubleValue).Should().Be(_validValues.DoubleValue);
        Argument.IsNegativeOrZero(_validValues.FloatValue).Should().Be(_validValues.FloatValue);
        Argument.IsNegativeOrZero(_validValues.TimeSpanValue).Should().Be(_validValues.TimeSpanValue);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(5.5f)]
    [InlineData(7.5)]
    [InlineData("00:00:10")]
    public void is_negative_or_zero_should_throw_argument_out_of_range_exception_when_negative(object argument)
    {
        Action action = argument switch
        {
            int => () => Argument.IsNegativeOrZero((int) argument),
            float => () => Argument.IsNegativeOrZero((float) argument),
            decimal => () => Argument.IsNegativeOrZero((decimal) argument),
            TimeSpan => () => Argument.IsNegativeOrZero((TimeSpan) argument),
            _ => throw new InvalidOperationException("Unsupported argument type"),
        };

        action.Should().Throw<ArgumentOutOfRangeException>();
    }
}
