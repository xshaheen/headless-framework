// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Checks;
using Tests.Helpers;

namespace Tests;

public sealed class ArgumentIsPositiveTests
{
    private readonly InputsTestArgument _validValues = new();

    [Fact]
    public void is_positive_should_return_same_value_when_positive()
    {
        Argument.IsPositive(_validValues.IntValue).Should().Be(_validValues.IntValue);
        Argument.IsPositive(_validValues.DecimalValue).Should().Be(_validValues.DecimalValue);
        Argument.IsPositive(_validValues.DoubleValue).Should().Be(_validValues.DoubleValue);
        Argument.IsPositive(_validValues.FloatValue).Should().Be(_validValues.FloatValue);
        Argument.IsPositive(_validValues.TimeSpanValue).Should().Be(_validValues.TimeSpanValue);
    }

    [Theory]
    [InlineData(-5)]
    [InlineData(-5.5f)]
    [InlineData(-7.5)]
    [InlineData("-00:00:10")]
    public void is_positive_should_throw_argument_out_of_range_exception_when_negative(object argument)
    {
        Action action = argument switch
        {
            int => () => Argument.IsPositive((int)argument),
            float => () => Argument.IsPositive((float)argument),
            decimal => () => Argument.IsPositive((decimal)argument),
            TimeSpan => () => Argument.IsPositive((TimeSpan)argument),
            _ => throw new InvalidOperationException("Unsupported argument type"),
        };

        action.Should().Throw<ArgumentOutOfRangeException>();
    }
}
