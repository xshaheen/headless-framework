// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

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

    [Fact]
    public void not_disposed_should_not_throw_when_not_disposed()
    {
        // given
        const bool disposed = false;
        object obj = new();

        // when & then
        Ensure.NotDisposed(disposed, obj);
    }

    [Fact]
    public void not_disposed_should_throw_when_disposed_with_null_value()
    {
        // given
        const bool disposed = true;
        object? obj = null;

        // when
        Action action = () => Ensure.NotDisposed(disposed, obj);

        // then
        action.Should().ThrowExactly<ObjectDisposedException>().WithMessage("Cannot access a disposed object.*");
    }

    [Fact]
    public void not_disposed_should_throw_when_disposed_with_object()
    {
        // given
        const bool disposed = true;
        object obj = "test string";

        // when
        Action action = () => Ensure.NotDisposed(disposed, obj);

        // then
        action
            .Should()
            .ThrowExactly<ObjectDisposedException>()
            .WithMessage("Cannot access a disposed object.*System.String*");
    }

    [Fact]
    public void not_disposed_should_throw_with_custom_message()
    {
        // given
        const bool disposed = true;
        object obj = new();
        const string customMessage = "Custom disposal message";

        // when
        Action action = () => Ensure.NotDisposed(disposed, obj, customMessage);

        // then
        action.Should().ThrowExactly<ObjectDisposedException>().WithMessage($"*{customMessage}*");
    }

    [Fact]
    public void not_disposed_should_include_type_name_in_exception()
    {
        // given
        const bool disposed = true;
        var obj = new TestDisposableObject();

        // when
        Action action = () => Ensure.NotDisposed(disposed, obj);

        // then
        action
            .Should()
            .ThrowExactly<ObjectDisposedException>()
            .WithMessage("*Tests.EnsureTests+TestDisposableObject*");
    }

    private sealed class TestDisposableObject;
}
