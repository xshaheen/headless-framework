// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Tests;

public sealed class IsConditionTests
{
    [Fact]
    public void should_not_throw_when_is_condition_is_true()
    {
        Argument.IsTrue(1 < 2);
    }

    [Fact]
    public void should_throw_when_is_condition_is_false()
    {
        const int value = 5;
        var action = () => Argument.IsTrue(value > 10);
        var actionWithMessage = () => Argument.IsTrue(value > 10, "value too small");

        action
            .Should()
            .ThrowExactly<ArgumentException>()
            .WithMessage("The condition \"value > 10\" must be true. (Parameter 'value > 10')");

        actionWithMessage.Should().ThrowExactly<ArgumentException>().WithMessage("value too small*");
    }

    [Fact]
    public void should_not_throw_when_is_false_condition_is_false()
    {
        Argument.IsFalse(1 > 2);
    }

    [Fact]
    public void should_throw_when_is_false_condition_is_true()
    {
        const int value = 5;
        var action = () => Argument.IsFalse(value < 10);
        var actionWithMessage = () => Argument.IsFalse(value < 10, "value too large");

        action
            .Should()
            .ThrowExactly<ArgumentException>()
            .WithMessage("The condition \"value < 10\" must be false. (Parameter 'value < 10')");

        actionWithMessage.Should().ThrowExactly<ArgumentException>().WithMessage("value too large*");
    }
}
