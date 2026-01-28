// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel;
using Headless.Checks;

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
        const SampleEnum validEnumValue = SampleEnum.Value2;

        // when & then
        Argument.IsInEnum(validEnumValue).Should().Be(validEnumValue);
    }

    [Fact]
    public void is_in_enum_generic_should_throw()
    {
        // given
        const SampleEnum argument = (SampleEnum)99;
        var customMessage = $"Error {nameof(argument)} = {argument} invalid for <SampleEnum>";
        // when
        Action action = () => Argument.IsInEnum(argument);
        Action actionWithCustomMessage = () => Argument.IsInEnum(argument, customMessage);

        // then
        action
            .Should()
            .ThrowExactly<InvalidEnumArgumentException>()
            .WithMessage(
                "The argument \"argument\" = 99 is NOT invalid for Enum type <SampleEnum>. (Parameter: 'argument')"
            );

        actionWithCustomMessage.Should().ThrowExactly<InvalidEnumArgumentException>().WithMessage($"{customMessage}");
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
    public void is_in_enum_int_should_throw_when_invalid()
    {
        // given
        const int argument = 99;
        var customMessage = $"Error {nameof(argument)} = {argument} invalid for {typeof(SampleEnum)}";

        // when
        Action action = () => Argument.IsInEnum<SampleEnum>(argument);
        Action actionWithCustomMessage = () => Argument.IsInEnum<SampleEnum>(argument, customMessage);

        // then
        action
            .Should()
            .ThrowExactly<InvalidEnumArgumentException>()
            .WithMessage(
                "The argument \"argument\" = 99 is NOT invalid for Enum type <SampleEnum>. (Parameter: 'argument')"
            );

        actionWithCustomMessage.Should().ThrowExactly<InvalidEnumArgumentException>().WithMessage($"{customMessage}");
    }
}
