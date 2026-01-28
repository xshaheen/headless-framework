// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Tests;

public sealed class IsNotDefault
{
    [Fact]
    public void is_default_should_not_throw_if_argument_is_default()
    {
        // given
        const int defaultValue = 0;

        // when & then
        Argument.IsDefault(defaultValue);
    }

    [Fact]
    public void is_default_should_throw_if_argument_is_not_default()
    {
        // given
        const int argument = 05;
        var customMessage = $"Error {nameof(argument)} = {argument} is not default for int.";
        // when
        var action = () => Argument.IsDefault(argument);
        var actionWithCustomMessage = () => Argument.IsDefault(argument, customMessage);

        // then
        action
            .Should()
            .ThrowExactly<ArgumentException>()
            .WithMessage("The argument \"argument\" must be default. (Parameter 'argument')");

        actionWithCustomMessage
            .Should()
            .ThrowExactly<ArgumentException>()
            .WithMessage($"{customMessage} (Parameter 'argument')");
    }

    [Fact]
    public void is_not_default_should_return_argument_if_not_default()
    {
        // given
        const int nonDefaultValue = 10;

        // when & then
        Argument.IsNotDefault(nonDefaultValue).Should().Be(nonDefaultValue);
    }

    [Fact]
    public void is_not_default_should_throw_if_argument_is_default()
    {
        // given
        const int argument = 0;
        var customMessage = $"Error {nameof(argument)} = {argument} is default for int.";

        // when
        Action action = () => Argument.IsNotDefault(argument);
        Action actionWithCustomMessage = () => Argument.IsNotDefault(argument, customMessage);

        // then
        action
            .Should()
            .ThrowExactly<ArgumentException>()
            .WithMessage(
                "The argument \"argument\" can NOT be the default value of <System.Int32>. (Parameter 'argument')"
            );

        actionWithCustomMessage
            .Should()
            .ThrowExactly<ArgumentException>()
            .WithMessage($"{customMessage} (Parameter 'argument')");
    }

    [Fact]
    public void is_not_default_should_return_nullable_argument_if_not_default()
    {
        // given
        int? nonDefaultValue = 15;

        // when & then
        Argument.IsNotDefault(nonDefaultValue).Should().Be(nonDefaultValue);
    }

    [Fact]
    public void is_not_default_or_null_should_return_argument_if_not_default_or_null()
    {
        // given
        int? nonDefaultValue = 20;

        // when & then
        Argument.IsNotDefaultOrNull(nonDefaultValue).Should().Be(nonDefaultValue);
    }

    [Fact]
    public void is_not_default_or_null_should_throw_if_argument_is_null()
    {
        // given
        int? argument = null;
        var customMessage = $"Error {nameof(argument)} = {argument} is default null for nullable type.";

        // when
        Action action = () => Argument.IsNotDefaultOrNull(argument);
        Action actionWithCustomMessage = () => Argument.IsNotDefaultOrNull(argument, customMessage);

        // then
        action
            .Should()
            .ThrowExactly<ArgumentNullException>()
            .WithMessage("Required argument \"argument\" was null. (Parameter 'argument')");

        actionWithCustomMessage
            .Should()
            .ThrowExactly<ArgumentNullException>()
            .WithMessage($"{customMessage} (Parameter 'argument')");
    }
}
