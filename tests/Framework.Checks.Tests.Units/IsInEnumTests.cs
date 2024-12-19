// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel;
using Framework.Checks;

namespace Tests;

public class IsInEnumTests
{
    private enum SampleEnum
    {
        Value1 = 1,
        Value2 = 2,
    }

    [Fact]
    public void is_in_enum_generic_should_return_argument_when_valid()
    {
        // given
        var validEnumValue = SampleEnum.Value2;

        // when & then
        Argument.IsInEnum(validEnumValue).Should().Be(validEnumValue);
    }

    [Fact]
    public void is_in_enum_generic_should_throw_without_custom_message()
    {
        // given
        var invalidEnumValue = (SampleEnum)99;

        // when & then
        Assert.Throws<InvalidEnumArgumentException>(() =>
            Argument.IsInEnum(invalidEnumValue)
        ).Message.Should().Contain($"The value of argument 'invalidEnumValue' ({invalidEnumValue}) is invalid for Enum type 'SampleEnum'.");
    }

    [Fact]
    public void is_in_enum_generic_should_throw_with_custom_message()
    {
        // given
        var invalidEnumValue = (SampleEnum)99;
        var customMessage = "Test custom message";
        // when & then
        Assert.Throws<InvalidEnumArgumentException>(() =>
            Argument.IsInEnum(invalidEnumValue,customMessage)
        ).Message.Should().Be($"Test custom message (Parameter: invalidEnumValue, Value: {invalidEnumValue}, Enum Type: SampleEnum)");
    }

    [Fact]
    public void is_in_enum_int_should_return_argument_when_valid()
    {
        // given
        var validEnumValue = (int)SampleEnum.Value1;

        // when & then
        Argument.IsInEnum<SampleEnum>(validEnumValue).Should().Be(validEnumValue);
    }

    [Fact]
    public void is_in_enum_int_should_throw_when_invalid_without_custom_message()
    {
        // given
        var invalidEnumValue = 99;

        // when & then
        Assert.Throws<InvalidEnumArgumentException>(() =>
            Argument.IsInEnum<SampleEnum>(invalidEnumValue)
        ).Message.Should().Contain($"The value of argument 'invalidEnumValue' ({99}) is invalid for Enum type 'SampleEnum'.");
    }

    [Fact]
    public void is_in_enum_int_should_throw_with_custom_message()
    {
        // Arrange
        var invalidEnumValue = 99;
        var customMessage = "Test custom message";

        // Act & Assert
        Assert.Throws<InvalidEnumArgumentException>(() =>
            Argument.IsInEnum<SampleEnum>(invalidEnumValue,customMessage)
        ).Message.Should().Be($"Test custom message (Parameter: invalidEnumValue, Value: {invalidEnumValue}, Enum Type: SampleEnum)");
    }
}
