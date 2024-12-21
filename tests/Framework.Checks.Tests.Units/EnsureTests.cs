// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Checks;

namespace Tests;

public sealed class EnsureTests
{
    [Fact]
    public void ensure_true_and_false()
    {
        // given
        const bool condition = true;

        // when & then
        Ensure.True(condition);
        Ensure.False(!condition);
    }

    [Fact]
    public void should_throw_when_condition_is_reverse()
    {
        const bool trueCondition = false;
        var trueAction = () => Ensure.True(trueCondition);

        trueAction
            .Should()
            .ThrowExactly<InvalidOperationException>()
            .WithMessage("The condition \"trueCondition\" must be true.");

        const bool falseCondition = true;
        var falseAction = () => Ensure.False(falseCondition);

        falseAction
            .Should()
            .ThrowExactly<InvalidOperationException>()
            .WithMessage("The condition \"falseCondition\" must be false.");
    }
}
