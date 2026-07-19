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
    public void should_not_throw_when_not_disposed_not_disposed()
    {
        // given
        const bool disposed = false;
        object obj = new();

        // when & then
        Ensure.NotDisposed(disposed, obj);
    }

    [Fact]
    public void should_throw_when_not_disposed_disposed_with_null_value()
    {
        // given
        const bool disposed = true;
        object? obj = null;

        // when
        var action = () => Ensure.NotDisposed(disposed, obj);

        // then
        action.Should().ThrowExactly<ObjectDisposedException>().WithMessage("Cannot access a disposed object.*");
    }

    [Fact]
    public void should_use_captured_expression_as_object_name_when_not_disposed_with_null_value()
    {
        // given
        var isShutDown = true;

        // when
        var action = () => Ensure.NotDisposed(isShutDown, disposedValue: null);

        // then - the captured condition text stands in for the missing instance
        action.Should().ThrowExactly<ObjectDisposedException>().Which.ObjectName.Should().Be("isShutDown");
    }

    [Fact]
    public void should_throw_when_not_disposed_disposed_with_object()
    {
        // given
        const bool disposed = true;
        object obj = "test string";

        // when
        var action = () => Ensure.NotDisposed(disposed, obj);

        // then
        action
            .Should()
            .ThrowExactly<ObjectDisposedException>()
            .WithMessage("Cannot access a disposed object.*System.String*");
    }

    [Fact]
    public void should_throw_with_custom_message_when_not_disposed()
    {
        // given
        const bool disposed = true;
        object obj = new();
        const string customMessage = "Custom disposal message";

        // when
        var action = () => Ensure.NotDisposed(disposed, obj, customMessage);

        // then
        action.Should().ThrowExactly<ObjectDisposedException>().WithMessage($"*{customMessage}*");
    }

    [Fact]
    public void should_include_type_name_in_exception_when_not_disposed()
    {
        // given
        const bool disposed = true;
        var obj = new TestDisposableObject();

        // when
        var action = () => Ensure.NotDisposed(disposed, obj);

        // then
        action
            .Should()
            .ThrowExactly<ObjectDisposedException>()
            .WithMessage("*Tests.EnsureTests+TestDisposableObject*");
    }

    [Fact]
    public void should_return_value_when_not_null_not_null()
    {
        const string reference = "value";
        Ensure.NotNull(reference).Should().Be("value");
        Ensure.NotNull((int?)5).Should().Be(5);
    }

    [Fact]
    public void should_throw_when_not_null_reference_is_null()
    {
        const string? reference = null;
        var action = () => Ensure.NotNull(reference);
        var actionWithMessage = () => Ensure.NotNull(reference, "must be set");

        action.Should().ThrowExactly<InvalidOperationException>().WithMessage("Expected \"reference\" to not be null.");

        actionWithMessage.Should().ThrowExactly<InvalidOperationException>().WithMessage("must be set");
    }

    [Fact]
    public void should_throw_when_not_null_nullable_struct_is_null()
    {
        int? value = null;
        var action = () => Ensure.NotNull(value);

        action.Should().ThrowExactly<InvalidOperationException>().WithMessage("Expected \"value\" to not be null.");
    }

    private sealed class TestDisposableObject;
}
