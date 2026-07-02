// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Tests.Helpers;

namespace Tests;

public sealed class IsPositiveOrZeroTests
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

    public enum NumericKind
    {
        Short,
        Int,
        Long,
        Float,
        Double,
        TimeSpan,
    }

    // Serializable enum discriminator (xUnit1045) instead of a mixed-type TheoryData<object>; each
    // kind is mapped to the typed guard call inside the test so Test Explorer can enumerate the rows.
    public static readonly TheoryData<NumericKind> NegativeData =
    [
        NumericKind.Short,
        NumericKind.Int,
        NumericKind.Long,
        NumericKind.Float,
        NumericKind.Double,
        NumericKind.TimeSpan,
    ];

    [Theory]
    [MemberData(nameof(NegativeData))]
    public void is_positive_or_zero_should_throw_argument_out_of_range_exception_when_negative(NumericKind kind)
    {
        Action action = kind switch
        {
            NumericKind.Short => () => Argument.IsPositiveOrZero((short)-3),
            NumericKind.Int => () => Argument.IsPositiveOrZero(-3),
            NumericKind.Long => () => Argument.IsPositiveOrZero(-5L),
            NumericKind.Float => () => Argument.IsPositiveOrZero(-5.5f),
            NumericKind.Double => () => Argument.IsPositiveOrZero(-7.5),
            NumericKind.TimeSpan => () =>
                Argument.IsPositiveOrZero(TimeSpan.Parse("-00:00:10", CultureInfo.InvariantCulture)),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

        action.Should().ThrowExactly<ArgumentOutOfRangeException>();
    }
}
