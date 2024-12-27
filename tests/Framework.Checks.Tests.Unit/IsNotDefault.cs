// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Checks;

namespace Tests;

public class IsNotDefault
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

        // when
        var action = () => Argument.IsDefault(argument);

        // then
        action
            .Should()
            .ThrowExactly<ArgumentException>()
            .WithMessage("The argument \"argument\" must be default. (Parameter 'argument')");
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
        const int defaultValue = 0;

        // when
        Action action = () => Argument.IsNotDefault(defaultValue);

        // then
        action
            .Should()
            .ThrowExactly<ArgumentException>()
            .WithMessage(
                "The argument \"defaultValue\" can NOT be the default value of <System.Int32>. (Parameter 'defaultValue')"
            );
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

        // when
        Action action = () => Argument.IsNotDefaultOrNull(argument);

        // then
        action
            .Should()
            .ThrowExactly<ArgumentNullException>()
            .WithMessage("Required argument \"argument\" was null. (Parameter 'argument')");
    }
}
