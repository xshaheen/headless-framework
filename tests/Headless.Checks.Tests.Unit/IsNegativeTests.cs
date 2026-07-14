// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Tests.Helpers;

namespace Tests;

public sealed class IsNegativeTests
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
    public static readonly TheoryData<NumericKind> Data =
    [
        NumericKind.Short,
        NumericKind.Int,
        NumericKind.Long,
        NumericKind.Float,
        NumericKind.Double,
        NumericKind.TimeSpan,
    ];

    [Theory]
    [MemberData(nameof(Data))]
    public void is_negative_should_throw_argument_out_of_range_exception_when_negative(NumericKind kind)
    {
        Action act = kind switch
        {
            NumericKind.Short => () => Argument.IsNegative((short)5),
            NumericKind.Int => () => Argument.IsNegative(5),
            NumericKind.Long => () => Argument.IsNegative(5L),
            NumericKind.Float => () => Argument.IsNegative(5.5f),
            NumericKind.Double => () => Argument.IsNegative(7.5),
            NumericKind.TimeSpan => () => Argument.IsNegative(TimeSpan.Parse("00:00:10", CultureInfo.InvariantCulture)),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

        act.Should().Throw<ArgumentOutOfRangeException>();
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
    public void is_negative_should_throw_argument_out_of_range_exception_when_negative_with_custom_message(
        NumericKind kind
    )
    {
        const string message = "Error argument must be negative.";

        // The guard captures the parameter name via [CallerArgumentExpression]; call through a local
        // named `value` so the thrown message stays "(Parameter 'value')" regardless of the numeric type.
        Action action = kind switch
        {
            NumericKind.Short => () =>
            {
                const short value = 3;
                Argument.IsNegative(value, message);
            },
            NumericKind.Int => () =>
            {
                const int value = 3;
                Argument.IsNegative(value, message);
            },
            NumericKind.Long => () =>
            {
                const long value = 5L;
                Argument.IsNegative(value, message);
            },
            NumericKind.Float => () =>
            {
                const float value = 5.5f;
                Argument.IsNegative(value, message);
            },
            NumericKind.Double => () =>
            {
                const double value = 7.5;
                Argument.IsNegative(value, message);
            },
            NumericKind.TimeSpan => () =>
            {
                var value = TimeSpan.Parse("00:00:10", CultureInfo.InvariantCulture);
                Argument.IsNegative(value, message);
            },
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

        action.Should().Throw<ArgumentOutOfRangeException>().WithMessage($"{message} (Parameter 'value')");
    }

    [Fact]
    public void is_not_of_type_should_throw_argument_exception_when_argument_is_of_type()
    {
        // given
        object argument = "test";
        var customMessage = $"Error {nameof(argument)} is string";

        // when
        var action = () => Argument.IsNotOfType<string>(argument);
        var actionWithCustomMessage = () => Argument.IsNotOfType<string>(argument, customMessage);

        // then
        action
            .Should()
            .ThrowExactly<ArgumentException>()
            .WithMessage("The argument \"argument\" must NOT be of type <System.String>. (Parameter 'argument')");

        actionWithCustomMessage
            .Should()
            .ThrowExactly<ArgumentException>()
            .WithMessage($"{customMessage} (Parameter 'argument')");
    }
}
