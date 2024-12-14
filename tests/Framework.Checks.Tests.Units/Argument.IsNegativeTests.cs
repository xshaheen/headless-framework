// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Checks;
using Tests.Helpers;

namespace Tests;

public class ArgumentIsNegativeTests
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

    public static readonly TheoryData<object> Data = new() { (short)5, 5, 5L, 5.5f, 7.5, TimeSpan.Parse("00:00:10") };

    [Theory]
    [MemberData(nameof(Data))]
    public void is_negative_should_throw_argument_out_of_range_exception_when_negative(object argument)
    {
        switch (argument)
        {
            case short:
                Assert.Throws<ArgumentOutOfRangeException>(() => Argument.IsNegative((short)argument));
                break;

            case int:
                Assert.Throws<ArgumentOutOfRangeException>(() => Argument.IsNegative((int)argument));
                break;

            case long:
                Assert.Throws<ArgumentOutOfRangeException>(() => Argument.IsNegative((long)argument));

                break;
            case float:
                Assert.Throws<ArgumentOutOfRangeException>(() => Argument.IsNegative((float)argument));

                break;
            case decimal:
                Assert.Throws<ArgumentOutOfRangeException>(() => Argument.IsNegative((decimal)argument));

                break;
            case TimeSpan:
                Assert.Throws<ArgumentOutOfRangeException>(() => Argument.IsNegative((TimeSpan)argument));
                Assert.Throws<ArgumentOutOfRangeException>(() => Argument.IsNegative((TimeSpan?)argument));

                break;
        }
    }

    [Fact]
    public void IsNotOfType_ShouldThrowArgumentException_WhenArgumentIsOfType()
    {
        // Arrange
        object argument = "test";

        // Act
        var exception = Assert.Throws<ArgumentException>(() => Argument.IsNotOfType<string>(argument));

        // Assert
        exception.Message.Should().NotBeNull();
    }
}
