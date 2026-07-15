// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Tests;

public sealed class StringContentTests
{
    [Fact]
    public void should_return_value_when_starts_with_matches()
    {
        Argument.StartsWith("hello world", "hello").Should().Be("hello world");
        Argument.StartsWith("HELLO", "hello", StringComparison.OrdinalIgnoreCase).Should().Be("HELLO");
    }

    [Fact]
    public void should_throw_when_starts_with_not_matching()
    {
        const string value = "hello world";
        var action = () => Argument.StartsWith(value, "world");

        action
            .Should()
            .ThrowExactly<ArgumentException>()
            .WithMessage("The argument \"value\" must start with \"world\". (Parameter 'value')");
    }

    [Fact]
    public void should_return_value_when_ends_with_matches()
    {
        Argument.EndsWith("hello world", "world").Should().Be("hello world");
    }

    [Fact]
    public void should_throw_when_ends_with_not_matching()
    {
        const string value = "hello world";
        var action = () => Argument.EndsWith(value, "hello");
        action.Should().ThrowExactly<ArgumentException>().WithMessage("*must end with \"hello\"*");
    }

    [Fact]
    public void should_return_value_when_contains_matches()
    {
        Argument.Contains("hello world", "o w").Should().Be("hello world");
    }

    [Fact]
    public void should_throw_when_contains_not_matching()
    {
        const string value = "hello world";
        var action = () => Argument.Contains(value, "xyz");
        action.Should().ThrowExactly<ArgumentException>().WithMessage("*must contain \"xyz\"*");
    }

    [Fact]
    public void should_throw_argument_null_when_string_content_argument_null()
    {
        var startsAction = () => Argument.StartsWith(null, "x");
        var endsAction = () => Argument.EndsWith(null, "x");
        var containsAction = () => Argument.Contains(null, "x");

        startsAction.Should().ThrowExactly<ArgumentNullException>();
        endsAction.Should().ThrowExactly<ArgumentNullException>();
        containsAction.Should().ThrowExactly<ArgumentNullException>();
    }
}
