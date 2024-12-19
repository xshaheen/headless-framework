// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Checks;

namespace Tests;

public class IsNotNullOrWhiteSpace
{
    [Fact]
    public void is_not_null_or_white_space_should_throw_argument_null_exception_if_argument_is_null_or_empty()
    {
        // given
        string? argumentNull = null;
        string argumentEmpty = "";

        // when & then
        Assert.Throws<ArgumentNullException>(
                () =>
                    Argument.IsNotNullOrWhiteSpace(argumentNull)
            )
            .Message.Should().Contain($"\"{nameof(argumentNull)}\" was null.");

        Assert.Throws<ArgumentException>(
                () =>
                    Argument.IsNotNullOrWhiteSpace(argumentEmpty)
            )
            .Message.Should().Contain($"\"{nameof(argumentEmpty)}\" was empty.");
    }

    [Fact]
    public void is_not_null_or_white_space_should_throw_argument_exception_if_argument_is_white_space()
    {
        // given
        string argument = "   ";

        // when & then
        Assert.Throws<ArgumentException>(
                () =>
                    Argument.IsNotNullOrWhiteSpace(argument)
            )
            .Message.Should().Contain($"\"{nameof(argument)}\" was empty.");
    }

    [Fact]
    public void is_not_null_or_white_space_should_return_argument_if_argument_is_valid()
    {
        // given
        string argument = "valid";

        // when & then
        Argument.IsNotNullOrWhiteSpace(argument).Should().Be(argument);
    }
}
