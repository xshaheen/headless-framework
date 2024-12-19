// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Checks;
using Tests.Helpers;

namespace Tests;

public class EquailTests
{
    [Fact]
    public void is_reference_equal_to_should_not_throw_when_instances_are_equal()
    {
        // given
        var obj1 = new InputsTestArgument();
        var obj2 = obj1;

        // Act & Assert
        Argument.IsReferenceEqualTo(obj1, obj2);
    }

    [Fact]
    public void is_reference_equal_to_should_throw_when_instances_are_not_equal()
    {
        // given
        var obj1 = new InputsTestArgument();
        var obj2 = new InputsTestArgument();

        // when & then
        Assert.Throws<ArgumentException>(() =>
            Argument.IsReferenceEqualTo(obj1, obj2)
        ).Message.Should().Contain("The argument \"obj1\" must be the same instance as \"obj2\".");
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
        var obj2 = obj1;

        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
            Argument.IsReferenceNotEqualTo(obj1, obj2)
        ).Message.Should().Contain("The argument \"obj1\" must not be the same instance as \"obj2\"");
    }
}
