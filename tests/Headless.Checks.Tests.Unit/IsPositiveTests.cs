// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Tests.Helpers;

namespace Tests;

public sealed class IsPositiveTests
{
    private readonly InputsTestArgument _validValues = new();

    [Fact]
    public void should_return_same_value_when_is_positive_positive()
    {
        Argument.IsPositive(_validValues.IntValue).Should().Be(_validValues.IntValue);
        Argument.IsPositive(_validValues.DecimalValue).Should().Be(_validValues.DecimalValue);
        Argument.IsPositive(_validValues.DoubleValue).Should().Be(_validValues.DoubleValue);
        Argument.IsPositive(_validValues.FloatValue).Should().Be(_validValues.FloatValue);
        Argument.IsPositive(_validValues.TimeSpanValue).Should().Be(_validValues.TimeSpanValue);
    }

    public enum NumericKind
    {
        Int,
        Float,
        Double,
        TimeSpan,
    }

    // Serializable enum discriminator (xUnit1045) instead of a mixed-type TheoryData<object>; each
    // kind is mapped to the typed guard call inside the test so Test Explorer can enumerate the rows.
    public static readonly TheoryData<NumericKind> TestData =
    [
        NumericKind.Int,
        NumericKind.Float,
        NumericKind.Double,
        NumericKind.TimeSpan,
    ];

    [Theory]
    [MemberData(nameof(TestData))]
    public void should_throw_argument_out_of_range_exception_when_is_positive_negative(NumericKind kind)
    {
        Action action = kind switch
        {
            NumericKind.Int => () => Argument.IsPositive(-5),
            NumericKind.Float => () => Argument.IsPositive(-5.5f),
            NumericKind.Double => () => Argument.IsPositive(-7.5),
            NumericKind.TimeSpan => () =>
                Argument.IsPositive(TimeSpan.Parse("-00:00:10", CultureInfo.InvariantCulture)),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

        action.Should().Throw<ArgumentOutOfRangeException>();
    }
}
