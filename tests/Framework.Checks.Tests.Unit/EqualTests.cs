// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Checks;
using Tests.Helpers;

namespace Tests;

public sealed class EqualTests
{
    [Fact]
    public void is_reference_equal_to_should_not_throw_when_instances_are_equal()
    {
        // given
        var obj1 = new InputsTestArgument();
        // ReSharper disable once InlineTemporaryVariable
        var obj2 = obj1;

        // when & then
        Argument.IsReferenceEqualTo(obj1, obj2);
    }

    [Fact]
    public void is_reference_equal_to_should_throw_when_instances_are_not_equal()
    {
        // given
        var obj1 = new InputsTestArgument();
        var obj2 = new InputsTestArgument();
        var customMessage = $"Error {nameof(obj1)} not the same {nameof(obj2)}.";
        // when
        var action = () => Argument.IsReferenceEqualTo(obj1, obj2);
        var actionWithCustomMessage = () => Argument.IsReferenceEqualTo(obj1, obj2,customMessage);

        // then
        action
            .Should()
            .ThrowExactly<ArgumentException>()
            .WithMessage("The argument \"obj1\" must be the same instance as \"obj2\". (Parameter 'obj1')");

        actionWithCustomMessage
            .Should()
            .ThrowExactly<ArgumentException>()
            .WithMessage($"{customMessage} (Parameter 'obj1')");
    }

    [Fact]
    public void is_reference_not_equal_to_should_not_throw_when_instances_are_not_equal()
    {
        // given
        var obj1 = new InputsTestArgument();
        var obj2 = new InputsTestArgument();

        // when & then
        Argument.IsReferenceNotEqualTo(obj1, obj2);
    }

    [Fact]
    public void is_reference_not_equal_to_should_throw_when_instances_are_equal()
    {
        // given
        var obj1 = new InputsTestArgument();
        // ReSharper disable once InlineTemporaryVariable
        var obj2 = obj1;
        var customMessage = $"Error {nameof(obj1)} is the same {nameof(obj2)}.";

        // when
        var action = () => Argument.IsReferenceNotEqualTo(obj1, obj2);
        var actionWithCustomMessage = () => Argument.IsReferenceNotEqualTo(obj1, obj2,customMessage);

        // then
        action
            .Should()
            .ThrowExactly<ArgumentException>()
            .WithMessage("The argument \"obj1\" must not be the same instance as \"obj2\". (Parameter 'obj1')");

        actionWithCustomMessage
            .Should()
            .ThrowExactly<ArgumentException>()
            .WithMessage($"{customMessage} (Parameter 'obj1')");
    }
}
