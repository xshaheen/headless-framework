// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Tests;

public sealed class IsConditionTests
{
    [Fact]
    public void is_should_not_throw_when_condition_is_true()
    {
        Argument.Is(1 < 2);
    }

    [Fact]
    public void is_should_throw_when_condition_is_false()
    {
        var value = 5;
        var action = () => Argument.Is(value > 10);
        var actionWithMessage = () => Argument.Is(value > 10, "value too small");

        action
            .Should()
            .ThrowExactly<ArgumentException>()
            .WithMessage("The condition \"value > 10\" must be true. (Parameter 'value > 10')");

        actionWithMessage.Should().ThrowExactly<ArgumentException>().WithMessage("value too small*");
    }

    [Fact]
    public void is_false_should_not_throw_when_condition_is_false()
    {
        Argument.IsFalse(1 > 2);
    }

    [Fact]
    public void is_false_should_throw_when_condition_is_true()
    {
        var value = 5;
        var action = () => Argument.IsFalse(value < 10);
        var actionWithMessage = () => Argument.IsFalse(value < 10, "value too large");

        action
            .Should()
            .ThrowExactly<ArgumentException>()
            .WithMessage("The condition \"value < 10\" must be false. (Parameter 'value < 10')");

        actionWithMessage.Should().ThrowExactly<ArgumentException>().WithMessage("value too large*");
    }
}
