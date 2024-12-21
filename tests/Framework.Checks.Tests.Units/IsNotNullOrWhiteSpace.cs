// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Checks;

namespace Tests;

public class IsNotNullOrWhiteSpace
{
    [Fact]
    public void is_not_null_or_white_space_should_throw_argument_null_exception_if_argument_is_null_or_empty()
    {
        const string? nullArgument = null;
        Action nullAction = () => Argument.IsNotNullOrWhiteSpace(nullArgument);

        nullAction
            .Should()
            .ThrowExactly<ArgumentNullException>()
            .WithMessage(
                $"Required argument \"{nameof(nullArgument)}\" was null. (Parameter '{nameof(nullArgument)}')"
            );

        const string emptyArgument = "";

        Action emptyAction = () => Argument.IsNotNullOrWhiteSpace(emptyArgument);

        emptyAction
            .Should()
            .ThrowExactly<ArgumentException>()
            .WithMessage(
                $"Required argument \"{nameof(emptyArgument)}\" was empty. (Parameter '{nameof(emptyArgument)}')"
            );
    }

    [Fact]
    public void is_not_null_or_white_space_should_throw_argument_exception_if_argument_is_white_space()
    {
        // given
        const string argument = "   ";

        // when & then
        Action action = () => Argument.IsNotNullOrWhiteSpace(argument);

        action
            .Should()
            .ThrowExactly<ArgumentException>()
            .WithMessage($"Required argument \"{nameof(argument)}\" was empty. (Parameter '{nameof(argument)}')");
    }

    [Fact]
    public void is_not_null_or_white_space_should_return_argument_if_argument_is_valid()
    {
        // given
        const string argument = "valid";

        // when & then
        Argument.IsNotNullOrWhiteSpace(argument).Should().Be(argument);
    }
}
