// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Tests;

public sealed class TypeTests
{
    [Fact]
    public void is_of_type_with_type_parameter_valid_does_not_throw()
    {
        // given
        object testArgument = 123;

        // when & then
        Argument.IsOfType<int>(testArgument);
        Argument.IsOfType<int>(testArgument);
    }

    [Fact]
    public void is_of_type_with_type_parameter_invalid_throws_exception()
    {
        // given
        object testArgument = "string value";

        // when
        var genericAction = () => Argument.IsOfType<int>(testArgument);
        var typeAction = () => Argument.IsOfType<int>(testArgument);

        // then
        const string message =
            "The argument \"testArgument\" must be of type <System.Int32>. (Actual type <System.String>) (Parameter 'testArgument')";

        genericAction.Should().ThrowExactly<ArgumentException>().WithMessage(message);
        typeAction.Should().ThrowExactly<ArgumentException>().WithMessage(message);
    }

    [Fact]
    public void is_not_of_type_with_type_parameter_valid_does_not_throw()
    {
        // given
        object testArgument = "string value";

        // when & then
        Argument.IsNotOfType<int>(testArgument);
        Argument.IsNotOfType<int>(testArgument);
    }

    [Fact]
    public void is_not_of_type_with_type_parameter_invalid_throws_exception()
    {
        // given
        object testArgument = 123;

        // when
        var genericAction = () => Argument.IsNotOfType<int>(testArgument);
        var typeAction = () => Argument.IsNotOfType<int>(testArgument);

        // then
        const string message =
            "The argument \"testArgument\" must NOT be of type <System.Int32>. (Parameter 'testArgument')";

        genericAction.Should().ThrowExactly<ArgumentException>().WithMessage(message);
        typeAction.Should().ThrowExactly<ArgumentException>().WithMessage(message);
    }

    [Fact]
    public void is_assignable_to_type_valid_cast_does_not_throw()
    {
        // given
        object testArgument = "string value";

        // when & then
        Argument.IsAssignableToType<string>(testArgument);
        Argument.IsAssignableToType<string>(testArgument);
    }

    [Fact]
    public void is_assignable_to_type_invalid_cast_throws_exception()
    {
        // given
        object testArgument = 123;

        // when
        var genericAction = () => Argument.IsAssignableToType<string>(testArgument);
        var typeAction = () => Argument.IsAssignableToType<string>(testArgument);

        // then
        const string message =
            "The argument \"testArgument\" must be assignable to <System.String>. (Actual type <System.Int32>) (Parameter 'testArgument')";

        genericAction.Should().ThrowExactly<ArgumentException>().WithMessage(message);
        typeAction.Should().ThrowExactly<ArgumentException>().WithMessage(message);
    }

    [Fact]
    public void is_not_assignable_to_type_valid_does_not_throw()
    {
        // given
        object testArgument = 123;

        // when & then
        Argument.IsNotAssignableToType<string>(testArgument);
        Argument.IsNotAssignableToType<string>(testArgument);
    }

    [Fact]
    public void is_not_assignable_to_type_invalid_throws_exception()
    {
        // given
        object testArgument = "string value";

        // when
        var genericAction = () => Argument.IsNotAssignableToType<string>(testArgument);
        var typeAction = () => Argument.IsNotAssignableToType<string>(testArgument);

        // then
        const string message =
            "The argument \"testArgument\" must NOT be assignable to <System.String>. (Parameter 'testArgument')";

        genericAction.Should().ThrowExactly<ArgumentException>().WithMessage(message);
        typeAction.Should().ThrowExactly<ArgumentException>().WithMessage(message);
    }
}
