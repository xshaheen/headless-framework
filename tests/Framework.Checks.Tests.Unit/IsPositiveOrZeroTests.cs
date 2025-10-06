// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Checks;
using Tests.Helpers;

namespace Tests;

#pragma warning disable xUnit1045 // Avoid using TheoryData type arguments that might not be serializable
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

    public static readonly TheoryData<object> NegativeData =
    [
        (short)-3,
        -3,
        -5L,
        -5.5f,
        -7.5,
        -7.5d,
        TimeSpan.Parse("-00:00:10", CultureInfo.InvariantCulture),
    ];

    [Theory]
    [MemberData(nameof(NegativeData))]
    public void is_positive_or_zero_should_throw_argument_out_of_range_exception_when_negative(object argument)
    {
        Action action = argument switch
        {
            short => () => Argument.IsPositiveOrZero((short)argument),
            int => () => Argument.IsPositiveOrZero((int)argument),
            long => () => Argument.IsPositiveOrZero((long)argument),
            float => () => Argument.IsPositiveOrZero((float)argument),
            double => () => Argument.IsPositiveOrZero((double)argument),
            decimal => () => Argument.IsPositiveOrZero((decimal)argument),
            TimeSpan => () => Argument.IsPositiveOrZero((TimeSpan)argument),
            _ => throw new InvalidOperationException("Unsupported argument type"),
        };

        action.Should().ThrowExactly<ArgumentOutOfRangeException>();
    }
}
