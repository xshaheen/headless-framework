// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Checks;
using Tests.Helpers;

namespace Tests;

#pragma warning disable xUnit1045 // Avoid using TheoryData type arguments that might not be serializable
public class IsNegativeOrZeroTests
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

    public static readonly TheoryData<object> PositiveData =
    [
        (short)3,
        3,
        5L,
        5.5f,
        7.5,
        7.5d,
        TimeSpan.Parse("00:00:10", CultureInfo.InvariantCulture),
    ];

    [Theory]
    [MemberData(nameof(PositiveData))]
    public void is_negative_or_zero_should_throw_argument_out_of_range_exception_when_positive(object argument)
    {
        Action action = argument switch
        {
            short => () => Argument.IsNegativeOrZero((short)argument),
            int => () => Argument.IsNegativeOrZero((int)argument),
            long => () => Argument.IsNegativeOrZero((long)argument),
            float => () => Argument.IsNegativeOrZero((float)argument),
            double => () => Argument.IsNegativeOrZero((double)argument),
            decimal => () => Argument.IsNegativeOrZero((decimal)argument),
            TimeSpan => () => Argument.IsNegativeOrZero((TimeSpan)argument),
            _ => throw new InvalidOperationException("Unsupported argument type"),
        };

        action.Should().Throw<ArgumentOutOfRangeException>();
    }

    public static readonly TheoryData<object, string> PositiveDataWithCustomMessage = new()
    {
        { (short)3, "Error argument must be negative or zero." },
        { 3, "Error argument must be negative or zero." },
        { 5L, "Error argument must be negative or zero." },
        { 5.5f, "Error argument must be negative or zero." },
        { 7.5, "Error argument must be negative or zero." },
        { 7.5d, "Error argument must be negative or zero." },
        { TimeSpan.Parse("00:00:10", CultureInfo.InvariantCulture), "Error argument must be negative or zero." },
    };

    [Theory]
    [MemberData(nameof(PositiveDataWithCustomMessage))]
    public void is_negative_or_zero_should_throw_argument_out_of_range_exception_when_positive_with_custom_message(
        object argument,
        string message
    )
    {
        Action action = argument switch
        {
            short value => () => Argument.IsNegativeOrZero(value, message),
            int value => () => Argument.IsNegativeOrZero(value, message),
            long value => () => Argument.IsNegativeOrZero(value, message),
            float value => () => Argument.IsNegativeOrZero(value, message),
            double value => () => Argument.IsNegativeOrZero(value, message),
            decimal value => () => Argument.IsNegativeOrZero(value, message),
            TimeSpan value => () => Argument.IsNegativeOrZero(value, message),
            _ => throw new InvalidOperationException("Unsupported argument type"),
        };

        action.Should().Throw<ArgumentOutOfRangeException>().WithMessage($"{message} (Parameter 'value')");
    }
}
