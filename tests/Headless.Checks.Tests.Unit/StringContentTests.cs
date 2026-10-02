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

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("acme")]
    [InlineData("ac me")]
    public void should_return_value_when_has_no_surrounding_white_space(string? value)
    {
        Argument.HasNoSurroundingWhiteSpace(value).Should().Be(value);
    }

    [Theory]
    [InlineData("acme ")]
    [InlineData(" acme")]
    [InlineData("acme\t")]
    [InlineData("\nacme")]
    [InlineData("acme\u00A0")]
    [InlineData(" ")]
    public void should_throw_when_value_starts_or_ends_with_white_space(string value)
    {
        var action = () => Argument.HasNoSurroundingWhiteSpace(value);

        action
            .Should()
            .ThrowExactly<ArgumentException>()
            .WithMessage("The argument \"value\" must not start or end with white space. (Parameter 'value')");
    }

    [Fact]
    public void should_return_value_when_every_provider_keeps_it_as_a_key()
    {
        // Case, composition, other control characters, and surrogate pairs all round-trip ordinally.
        string?[] portable =
        [
            null,
            "",
            "Acme",
            "acme",
            "e\u0301",
            "\u00E9",
            "a\u200Bb",
            "a\tb",
            "a\u0001b",
            "\uD83D\uDE00",
            "ac me",
        ];

        foreach (var value in portable)
        {
            Argument.IsPortableKey(value).Should().Be(value);
        }
    }

    public static TheoryData<string, string> UnportableKeys =>
        new()
        {
            { "acme ", "*starts or ends with white space*" },
            { "\u00A0acme", "*starts or ends with white space*" },
            { "ac\0me", "*NUL character*" },
            // Built in code: a lone surrogate in an attribute argument does not survive UTF-8 metadata encoding.
            { "a" + (char)0xD800, "*unpaired UTF-16 surrogate*" },
            { (char)0xDC00 + "a", "*unpaired UTF-16 surrogate*" },
            { "a" + (char)0xDBFF + "b", "*unpaired UTF-16 surrogate*" },
            { "\uD83D\uDE00" + (char)0xDE00, "*unpaired UTF-16 surrogate*" },
        };

    [Theory]
    [MemberData(nameof(UnportableKeys))]
    public void should_throw_when_value_is_not_portable_key_text(string value, string reason)
    {
        var action = () => Argument.IsPortableKey(value);

        action
            .Should()
            .ThrowExactly<ArgumentException>()
            .WithMessage(
                $"The argument \"value\" {reason}, so storage providers would disagree about the key it names.*"
            )
            .Which.ParamName.Should()
            .Be("value");
    }

    [Fact]
    public void should_use_the_custom_message_for_a_key_that_is_not_portable()
    {
        var action = () => Argument.IsPortableKey("a\0", "custom");

        action.Should().ThrowExactly<ArgumentException>().WithMessage("custom*");
    }
}
