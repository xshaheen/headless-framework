// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Checks;

namespace Tests;

public class IsNotNullOrWhiteSpace
{
    [Fact]
    public void is_not_null_or_white_space_should_throw_argument_null_exception_if_argument_is_null_or_empty()
    {
        // given
        const string? nullArgument = null;
        var customMessage = "Error argument is null";

        // when
        Action nullAction = () => Argument.IsNotNullOrWhiteSpace(nullArgument);
        Action nullActionWithCustomMessage = () => Argument.IsNotNullOrWhiteSpace(nullArgument, customMessage);

        // then
        nullAction
            .Should()
            .ThrowExactly<ArgumentNullException>()
            .WithMessage(
                $"Required argument \"{nameof(nullArgument)}\" was null. (Parameter '{nameof(nullArgument)}')"
            );

        nullActionWithCustomMessage
            .Should()
            .ThrowExactly<ArgumentNullException>()
            .WithMessage(
                $"{customMessage} (Parameter '{nameof(nullArgument)}')"
            );

        // given
        const string emptyArgument = "";
        customMessage = $"Error argument is empty";

        // when
        Action emptyAction = () => Argument.IsNotNullOrWhiteSpace(emptyArgument);
        Action emptyActionWithCustomMessage = () => Argument.IsNotNullOrWhiteSpace(emptyArgument, customMessage);

        // then
        emptyAction
            .Should()
            .ThrowExactly<ArgumentException>()
            .WithMessage(
                $"Required argument \"{nameof(emptyArgument)}\" was empty. (Parameter '{nameof(emptyArgument)}')"
            );

        emptyActionWithCustomMessage
            .Should()
            .ThrowExactly<ArgumentException>()
            .WithMessage(
                $"{customMessage} (Parameter '{nameof(emptyArgument)}')"
            );
    }

    [Fact]
    public void is_not_null_or_white_space_should_throw_argument_exception_if_argument_is_white_space()
    {
        // given
        const string argument = "   ";
        const string customMessage = "Error argument is empty";

        // when
        Action action = () => Argument.IsNotNullOrWhiteSpace(argument);
        Action actionWithCustomMessage = () => Argument.IsNotNullOrWhiteSpace(argument, customMessage);

        // then
        action
            .Should()
            .ThrowExactly<ArgumentException>()
            .WithMessage($"Required argument \"{nameof(argument)}\" was empty. (Parameter '{nameof(argument)}')");

        actionWithCustomMessage
            .Should()
            .ThrowExactly<ArgumentException>()
            .WithMessage($"{customMessage} (Parameter '{nameof(argument)}')");
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
