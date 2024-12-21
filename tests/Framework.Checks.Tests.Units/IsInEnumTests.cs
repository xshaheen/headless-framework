// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel;
using Framework.Checks;

namespace Tests;

public sealed class IsInEnumTests
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
        const SampleEnum argument = (SampleEnum)99;

        // when
        Action action = () => Argument.IsInEnum(argument);

        // then
        action
            .Should()
            .ThrowExactly<InvalidEnumArgumentException>()
            .WithMessage(
                "The argument \"argument\" = 99 is NOT invalid for Enum type <SampleEnum>. (Parameter: 'argument')"
            );
    }

    [Fact]
    public void is_in_enum_generic_should_throw_with_custom_message()
    {
        // given
        const SampleEnum invalidEnumValue = (SampleEnum)99;
        const string customMessage = "Test custom message";

        // when
        Action action = () => Argument.IsInEnum(invalidEnumValue, customMessage);

        // then
        action.Should().ThrowExactly<InvalidEnumArgumentException>().WithMessage("Test custom message");
    }

    [Fact]
    public void is_in_enum_int_should_return_argument_when_valid()
    {
        // given
        const int validEnumValue = (int)SampleEnum.Value1;

        // when & then
        Argument.IsInEnum<SampleEnum>(validEnumValue).Should().Be(validEnumValue);
    }

    [Fact]
    public void is_in_enum_int_should_throw_when_invalid_without_custom_message()
    {
        // given
        const int invalidEnumValue = 99;

        // when
        Action action = () => Argument.IsInEnum<SampleEnum>(invalidEnumValue);

        // then
        action
            .Should()
            .ThrowExactly<InvalidEnumArgumentException>()
            .WithMessage(
                "The argument \"invalidEnumValue\" = 99 is NOT invalid for Enum type <SampleEnum>. (Parameter: 'invalidEnumValue')"
            );
    }

    [Fact]
    public void is_in_enum_int_should_throw_with_custom_message()
    {
        // given
        const int invalidEnumValue = 99;
        const string customMessage = "Test custom message";

        // when
        Action action = () => Argument.IsInEnum<SampleEnum>(invalidEnumValue, customMessage);

        // then
        action.Should().ThrowExactly<InvalidEnumArgumentException>().WithMessage("Test custom message");
    }
}
