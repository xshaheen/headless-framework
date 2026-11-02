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

    [Flags]
    private enum Permissions
    {
        None = 0,
        Read = 1,
        Write = 2,
        Execute = 4,
    }

    [Flags]
    private enum ByteFlags : byte
    {
        None = 0,
        A = 1,
        B = 2,
    }

    [Fact]
    public void should_return_argument_when_is_in_enum_generic_valid()
    {
        // given
        const SampleEnum validEnumValue = SampleEnum.Value2;

        // when & then
        Argument.IsInEnum(validEnumValue).Should().Be(validEnumValue);
    }

    [Fact]
    public void should_throw_when_is_in_enum_generic()
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
                "The argument \"argument\" = 99 is not a valid value for Enum type <SampleEnum>. (Parameter: 'argument')"
            );

        actionWithCustomMessage.Should().ThrowExactly<InvalidEnumArgumentException>().WithMessage($"{customMessage}");
    }

    [Fact]
    public void should_return_argument_when_is_in_enum_int_valid()
    {
        // given
        const int validEnumValue = (int)SampleEnum.Value1;

        // when & then
        Argument.IsInEnum<SampleEnum>(validEnumValue).Should().Be(validEnumValue);
    }

    [Fact]
    public void should_throw_when_is_in_enum_int_invalid()
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
                "The argument \"argument\" = 99 is not a valid value for Enum type <SampleEnum>. (Parameter: 'argument')"
            );

        actionWithCustomMessage.Should().ThrowExactly<InvalidEnumArgumentException>().WithMessage($"{customMessage}");
    }

    [Fact]
    public void should_accept_composite_of_defined_flags_when_is_in_enum_flags()
    {
        // given - Read | Write (3) is a valid combination but not itself a named member
        const Permissions composite = Permissions.Read | Permissions.Write;

        // when & then
        Argument.IsInEnum(composite).Should().Be(composite);
#pragma warning disable RCS1257 // Use enum field explicitly
        Argument.IsInEnum(Permissions.Read | Permissions.Write | Permissions.Execute).Should().Be((Permissions)7);
#pragma warning restore RCS1257
    }

    [Fact]
    public void should_throw_when_is_in_enum_flags_value_has_undefined_bits()
    {
        // given - bit 8 is not a defined flag
        const Permissions argument = (Permissions)8;

        // when
        Action action = () => Argument.IsInEnum(argument);

        // then
        action.Should().ThrowExactly<InvalidEnumArgumentException>();
    }

    [Fact]
    public void should_accept_composite_of_defined_flags_when_is_in_enum_int()
    {
        // when & then - 3 is Read | Write, a valid combination that is not a named member
        Argument.IsInEnum<Permissions>(3).Should().Be(3);
    }

    [Fact]
    public void should_throw_when_is_in_enum_int_flags_value_has_undefined_bits()
    {
        // when
        Action action = () => Argument.IsInEnum<Permissions>(9);

        // then
        action.Should().ThrowExactly<InvalidEnumArgumentException>();
    }

    [Fact]
    public void should_validate_flags_combination_when_enum_is_not_int_backed()
    {
        // when & then
        Argument.IsInEnum(ByteFlags.A | ByteFlags.B).Should().Be(ByteFlags.A | ByteFlags.B);

        Action action = () => Argument.IsInEnum((ByteFlags)4);
        action.Should().ThrowExactly<InvalidEnumArgumentException>();
    }
}
