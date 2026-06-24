// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Tests;

public sealed class StringContentTests
{
    [Fact]
    public void starts_with_should_return_value_when_matches()
    {
        Argument.StartsWith("hello world", "hello").Should().Be("hello world");
        Argument.StartsWith("HELLO", "hello", StringComparison.OrdinalIgnoreCase).Should().Be("HELLO");
    }

    [Fact]
    public void starts_with_should_throw_when_not_matching()
    {
        var value = "hello world";
        var action = () => Argument.StartsWith(value, "world");

        action
            .Should()
            .ThrowExactly<ArgumentException>()
            .WithMessage("The argument \"value\" must start with \"world\". (Parameter 'value')");
    }

    [Fact]
    public void ends_with_should_return_value_when_matches()
    {
        Argument.EndsWith("hello world", "world").Should().Be("hello world");
    }

    [Fact]
    public void ends_with_should_throw_when_not_matching()
    {
        var value = "hello world";
        var action = () => Argument.EndsWith(value, "hello");
        action.Should().ThrowExactly<ArgumentException>().WithMessage("*must end with \"hello\"*");
    }

    [Fact]
    public void contains_should_return_value_when_matches()
    {
        Argument.Contains("hello world", "o w").Should().Be("hello world");
    }

    [Fact]
    public void contains_should_throw_when_not_matching()
    {
        var value = "hello world";
        var action = () => Argument.Contains(value, "xyz");
        action.Should().ThrowExactly<ArgumentException>().WithMessage("*must contain \"xyz\"*");
    }

    [Fact]
    public void string_content_should_throw_argument_null_when_argument_null()
    {
        var startsAction = () => Argument.StartsWith(null, "x");
        var endsAction = () => Argument.EndsWith(null, "x");
        var containsAction = () => Argument.Contains(null, "x");

        startsAction.Should().ThrowExactly<ArgumentNullException>();
        endsAction.Should().ThrowExactly<ArgumentNullException>();
        containsAction.Should().ThrowExactly<ArgumentNullException>();
    }
}
