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
    public void should_not_format_interpolated_message_when_is_true_condition_holds()
    {
        var formatted = 0;

        Argument.IsTrue(condition: true, $"never built {_CountFormat(ref formatted)}", "value");

        formatted.Should().Be(0);
    }

    [Fact]
    public void should_throw_with_interpolated_message_and_param_name_when_is_true_condition_fails()
    {
        var value = 5;
        var minimum = 10;
        var action = () => Argument.IsTrue(value > minimum, $"{value} is below {minimum}.", nameof(value));
        var actionWithCapturedName = () => Argument.IsTrue(value > minimum, $"{value} is below {minimum}.");

        action
            .Should()
            .ThrowExactly<ArgumentException>()
            .WithMessage("5 is below 10. (Parameter 'value')")
            .Which.ParamName.Should()
            .Be("value");

        actionWithCapturedName
            .Should()
            .ThrowExactly<ArgumentException>()
            .Which.ParamName.Should()
            .Be("value > minimum");
    }

    private static int _CountFormat(ref int formatted) => ++formatted;

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
