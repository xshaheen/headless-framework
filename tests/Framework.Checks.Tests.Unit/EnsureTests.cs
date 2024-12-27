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
        // given
        const bool trueCondition = false;
        var customMessageEnsureTrue = $"Error {nameof(trueCondition)} not true.";
        var trueAction = () => Ensure.True(trueCondition);

        // when
        var trueActionWithCustomMessage = () => Ensure.True(trueCondition, customMessageEnsureTrue);

        // then
        trueAction
            .Should()
            .ThrowExactly<InvalidOperationException>()
            .WithMessage("The condition \"trueCondition\" must be true.");

        trueActionWithCustomMessage
            .Should()
            .ThrowExactly<InvalidOperationException>()
            .WithMessage(customMessageEnsureTrue);

        // Ensure false condition

        // given
        const bool falseCondition = true;
        var customMessageEnsureFalse = $"Error {nameof(trueCondition)} not false.";

        // when
        var falseAction = () => Ensure.False(falseCondition);
        var falseActionWithCustomMessage = () => Ensure.False(falseCondition, customMessageEnsureFalse);


        // then
        falseAction
            .Should()
            .ThrowExactly<InvalidOperationException>()
            .WithMessage("The condition \"falseCondition\" must be false.");

        falseActionWithCustomMessage
            .Should()
            .ThrowExactly<InvalidOperationException>()
            .WithMessage(customMessageEnsureFalse);
    }
}
