// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Checks;
using Tests.Helpers;

namespace Tests;

public class IsNegativeTests
{
    private readonly InputsTestArgument _validValues = new()
    {
        IntValue = -5,
        DecimalValue = -9.5m,
        DoubleValue = -9,
        FloatValue = -1f,
        TimeSpanValue = TimeSpan.FromDays(-5),
    };

    [Fact]
    public void is_negative_should_return_same_value_when_positive()
    {
        Argument.IsNegative(_validValues.IntValue).Should().Be(_validValues.IntValue);
        Argument.IsNegative(_validValues.DecimalValue).Should().Be(_validValues.DecimalValue);
        Argument.IsNegative(_validValues.DoubleValue).Should().Be(_validValues.DoubleValue);
        Argument.IsNegative(_validValues.FloatValue).Should().Be(_validValues.FloatValue);
        Argument.IsNegative(_validValues.TimeSpanValue).Should().Be(_validValues.TimeSpanValue);
    }

    public static readonly TheoryData<object> Data =
    [
        (short)5,
        5,
        5L,
        5.5f,
        7.5,
        TimeSpan.Parse("00:00:10", CultureInfo.InvariantCulture),
    ];

    [Theory]
    [MemberData(nameof(Data))]
    public void is_negative_should_throw_argument_out_of_range_exception_when_negative(object argument)
    {
        Action act = argument switch
        {
            short => () => Argument.IsNegative((short)argument),
            int => () => Argument.IsNegative((int)argument),
            long => () => Argument.IsNegative((long)argument),
            float => () => Argument.IsNegative((float)argument),
            double => () => Argument.IsNegative((double)argument),
            decimal => () => Argument.IsNegative((decimal)argument),
            TimeSpan => () => Argument.IsNegative((TimeSpan)argument),
            _ => throw new InvalidOperationException("Unsupported argument type"),
        };

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void is_not_of_type_should_throw_argument_exception_when_argument_is_of_type()
    {
        // given
        object argument = "test";

        // when
        var action = () => Argument.IsNotOfType<string>(argument);

        // then
        action.Should().ThrowExactly<ArgumentException>();
    }
}
