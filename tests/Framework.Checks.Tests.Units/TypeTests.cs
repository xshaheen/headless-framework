// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Checks;

namespace Tests;

public class TypeTests
{
    [Fact]
    public void is_of_type_valid_type_does_not_throw()
    {
        // given
        object testArgument = "string value";

        // when & then
        Argument.IsOfType<string>(testArgument);
    }

    [Fact]
    public void is_of_type_invalid_type_throws_exception()
    {
        // given
        object testArgument = 123;

        // when & then
        Assert.Throws<ArgumentException>(() => Argument.IsOfType<string>(testArgument))
            .Message.Should().Contain("must be of type");
    }

    [Fact]
    public void is_not_of_type_valid_type_throws_exception()
    {
        // given
        object testArgument = "string value";

        // when & then
        Assert.Throws<ArgumentException>(() => Argument.IsNotOfType<string>(testArgument))
            .Message.Should().Contain("must not be of type");
    }

    [Fact]
    public void is_not_of_type_invalid_type_does_not_throw()
    {
        // given
        object testArgument = 123;

        // when & then
        Argument.IsNotOfType<string>(testArgument);
    }

    [Fact]
    public void is_assignable_to_type_valid_cast_does_not_throw()
    {
        // given
        object testArgument = "string value";

        // when & then
        Argument.IsAssignableToType<string>(testArgument);
    }

    [Fact]
    public void is_assignable_to_type_invalid_cast_throws_exception()
    {
        // given
        object testArgument = 123;

        // when & then
        Assert.Throws<ArgumentException>(() => Argument.IsAssignableToType<string>(testArgument))
            .Message.Should().Contain($"must be assignable to {typeof(string)}");
    }

    [Fact]
    public void is_not_assignable_to_type_valid_does_not_throw()
    {
        // given
        object testArgument = 123;

        // when & then
        Argument.IsNotAssignableToType<string>(testArgument);
    }

    [Fact]
    public void is_not_assignable_to_type_invalid_throws_exception()
    {
        // given
        object testArgument = "string value";

        // when & Assert
        Assert.Throws<ArgumentException>(() => Argument.IsNotAssignableToType<string>(testArgument))
            .Message.Should().Contain($"must not be assignable to {typeof(string)}");
    }

    [Fact]
    public void is_of_type_with_type_parameter_valid_does_not_throw()
    {
        // given
        object testArgument = 123;

        // when & then
        Argument.IsOfType(testArgument, typeof(int));
    }

    [Fact]
    public void is_of_type_with_type_parameter_invalid_throws_exception()
    {
        // given
        object testArgument = "string value";
        // when & then
        Assert.Throws<ArgumentException>(() => Argument.IsOfType(testArgument, typeof(int)))
            .Message.Should().Contain($"must be of type {typeof(int)}");
    }

    [Fact]
    public void is_not_of_type_with_type_parameter_valid_does_not_throw()
    {
        // given
        object testArgument = "string value";

        // when & then
        Argument.IsNotOfType(testArgument, typeof(int));
    }

    [Fact]
    public void is_not_of_type_with_type_parameter_invalid_throws_exception()
    {
        // given
        object testArgument = 123;

        // when & then
        Assert.Throws<ArgumentException>(() => Argument.IsNotOfType(testArgument, typeof(int)))
            .Message.Should().Contain($"must not be of type {typeof(int)}");
    }

    [Fact]
    public void is_assignable_to_type_with_type_parameter_valid_does_not_throw()
    {
        // given
        object testArgument = "string value";

        // when & then
        Argument.IsAssignableToType(testArgument, typeof(string));
    }

    [Fact]
    public void is_assignable_to_type_with_type_parameter_invalid_throws_exception()
    {
        // given
        object testArgument = 123;

        // when & then
        Assert.Throws<ArgumentException>(() => Argument.IsAssignableToType(testArgument, typeof(string)))
            .Message.Should().Contain($"must be assignable to {typeof(string)}");
    }

    [Fact]
    public void is_not_assignable_to_type_with_type_parameter_valid_does_not_throw()
    {
        // given
        object testArgument = 123;

        // when & then
        Argument.IsNotAssignableToType(testArgument, typeof(string));
    }

    [Fact]
    public void is_not_assignable_to_type_with_type_parameter_invalid_throws_exception()
    {
        // given
        object testArgument = "string value";

        // when & then
        Assert.Throws<ArgumentException>(() => Argument.IsNotAssignableToType(testArgument, typeof(string)))
            .Message.Should().Contain($"must not be assignable to {typeof(string)}");
    }
}
