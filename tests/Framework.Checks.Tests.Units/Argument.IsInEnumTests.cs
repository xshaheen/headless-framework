// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel;
using Framework.Checks;

namespace Tests;

public class ArgumentIsInEnumTests
{
    private enum SampleEnum
    {
        Zero = 0,
        One = 1,
    }

    [Fact]
    public void is_in_enum_generic_should_return_valid_enum_value()
    {
        // given
        var argument = SampleEnum.One;

        // when & then
        Argument.IsInEnum(argument).Should().Be(argument);
    }

    [Fact]
    public void is_in_enum_generic_should_throw_for_invalid_enum_value()
    {
        // given
        var invalidValue = (SampleEnum)999;

        // when & then
        Assert.Throws<InvalidEnumArgumentException>(() => Argument.IsInEnum(invalidValue));
    }
}
