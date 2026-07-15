// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Tests.Helpers;

namespace Tests;

public sealed class IsNegativeOrZeroTests
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
    public void should_return_same_value_when_is_negative_or_zero_positive()
    {
        Argument.IsNegativeOrZero(_validValues.IntValue).Should().Be(_validValues.IntValue);
        Argument.IsNegativeOrZero(_validValues.DecimalValue).Should().Be(_validValues.DecimalValue);
        Argument.IsNegativeOrZero(_validValues.DoubleValue).Should().Be(_validValues.DoubleValue);
        Argument.IsNegativeOrZero(_validValues.FloatValue).Should().Be(_validValues.FloatValue);
        Argument.IsNegativeOrZero(_validValues.TimeSpanValue).Should().Be(_validValues.TimeSpanValue);
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
    public static readonly TheoryData<NumericKind> PositiveData =
    [
        NumericKind.Short,
        NumericKind.Int,
        NumericKind.Long,
        NumericKind.Float,
        NumericKind.Double,
        NumericKind.TimeSpan,
    ];

    [Theory]
    [MemberData(nameof(PositiveData))]
    public void should_throw_argument_out_of_range_exception_when_is_negative_or_zero_positive(NumericKind kind)
    {
        Action action = kind switch
        {
            NumericKind.Short => () => Argument.IsNegativeOrZero((short)3),
            NumericKind.Int => () => Argument.IsNegativeOrZero(3),
            NumericKind.Long => () => Argument.IsNegativeOrZero(5L),
            NumericKind.Float => () => Argument.IsNegativeOrZero(5.5f),
            NumericKind.Double => () => Argument.IsNegativeOrZero(7.5),
            NumericKind.TimeSpan => () =>
                Argument.IsNegativeOrZero(TimeSpan.Parse("00:00:10", CultureInfo.InvariantCulture)),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

        action.Should().Throw<ArgumentOutOfRangeException>();
    }

    public static readonly TheoryData<NumericKind> PositiveDataWithCustomMessage =
    [
        NumericKind.Short,
        NumericKind.Int,
        NumericKind.Long,
        NumericKind.Float,
        NumericKind.Double,
        NumericKind.TimeSpan,
    ];

    [Theory]
    [MemberData(nameof(PositiveDataWithCustomMessage))]
    public void should_throw_argument_out_of_range_exception_when_is_negative_or_zero_positive_with_custom_message(
        NumericKind kind
    )
    {
        const string message = "Error argument must be negative or zero.";

        // The guard captures the parameter name via [CallerArgumentExpression]; call through a local
        // named `value` so the thrown message stays "(Parameter 'value')" regardless of the numeric type.
        Action action = kind switch
        {
            NumericKind.Short => () =>
            {
                const short value = 3;
                Argument.IsNegativeOrZero(value, message);
            },
            NumericKind.Int => () =>
            {
                const int value = 3;
                Argument.IsNegativeOrZero(value, message);
            },
            NumericKind.Long => () =>
            {
                const long value = 5L;
                Argument.IsNegativeOrZero(value, message);
            },
            NumericKind.Float => () =>
            {
                const float value = 5.5f;
                Argument.IsNegativeOrZero(value, message);
            },
            NumericKind.Double => () =>
            {
                const double value = 7.5;
                Argument.IsNegativeOrZero(value, message);
            },
            NumericKind.TimeSpan => () =>
            {
                var value = TimeSpan.Parse("00:00:10", CultureInfo.InvariantCulture);
                Argument.IsNegativeOrZero(value, message);
            },
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

        action.Should().Throw<ArgumentOutOfRangeException>().WithMessage($"{message} (Parameter 'value')");
    }
}
