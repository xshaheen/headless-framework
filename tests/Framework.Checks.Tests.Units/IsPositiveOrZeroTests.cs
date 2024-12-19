// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Checks;
using Tests.Helpers;

namespace Tests;

public class IsPositiveOrZeroTests
{
    private readonly InputsTestArgument _validValues = new()
    {
        IntValue = 5,
        DecimalValue = 0,
        DoubleValue = 0,
        FloatValue = 1f,
        TimeSpanValue = TimeSpan.FromDays(35),
    };

    [Fact]
    public void is_positive_or_zero_should_return_same_value_when_positive()
    {
        Argument.IsPositiveOrZero(_validValues.IntValue).Should().Be(_validValues.IntValue);
        Argument.IsPositiveOrZero(_validValues.DecimalValue).Should().Be(_validValues.DecimalValue);
        Argument.IsPositiveOrZero(_validValues.DoubleValue).Should().Be(_validValues.DoubleValue);
        Argument.IsPositiveOrZero(_validValues.FloatValue).Should().Be(_validValues.FloatValue);
        Argument.IsPositiveOrZero(_validValues.TimeSpanValue).Should().Be(_validValues.TimeSpanValue);
    }

    [Theory]
    [InlineData(-3)]
    [InlineData(-5.5f)]
    [InlineData(-7.5)]
    [InlineData("-00:00:10")]
    public void is_positive_or_zero_should_throw_argument_out_of_range_exception_when_negative(object argument)
    {
        switch (argument)
        {
            case int:
                Assert.Throws<ArgumentOutOfRangeException>(() => Argument.IsPositiveOrZero((int)argument));

                break;
            case float:
                Assert.Throws<ArgumentOutOfRangeException>(() => Argument.IsPositiveOrZero((float)argument));

                break;
            case decimal:
                Assert.Throws<ArgumentOutOfRangeException>(() => Argument.IsPositiveOrZero((decimal)argument));

                break;
            case TimeSpan:
                Assert.Throws<ArgumentOutOfRangeException>(() => Argument.IsPositiveOrZero((TimeSpan)argument));

                break;
        }
    }
}
