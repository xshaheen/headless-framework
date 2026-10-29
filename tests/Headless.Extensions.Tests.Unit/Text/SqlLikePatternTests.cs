// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless;

namespace Tests.Text;

public sealed class SqlLikePatternTests
{
    [Theory]
    [InlineData("", "")]
    [InlineData("order.created", "order.created")]
    [InlineData("50%", "50\\%")]
    [InlineData("user_id", "user\\_id")]
    [InlineData("[draft]", "\\[draft]")]
    [InlineData("C:\\temp", "C:\\\\temp")]
    [InlineData("\\%", "\\\\\\%")]
    public void should_escape_wildcards_and_escape_character_when_escape(string value, string expected)
    {
        // when
        var result = SqlLikePattern.Escape(value);

        // then
        result.Should().Be(expected);
    }

    [Fact]
    public void should_append_trailing_wildcard_to_escaped_value_when_starts_with()
    {
        // when
        var result = SqlLikePattern.StartsWith("user_");

        // then
        result.Should().Be("user\\_%");
    }

    [Fact]
    public void should_wrap_escaped_value_in_wildcards_when_contains()
    {
        // when
        var result = SqlLikePattern.Contains("50%");

        // then
        result.Should().Be("%50\\%%");
    }

    [Fact]
    public void should_use_backslash_as_escape_character()
    {
        SqlLikePattern.EscapeCharacter.Should().Be("\\");
    }

    [Fact]
    public void should_throw_when_value_is_null()
    {
        // when
        var escape = () => SqlLikePattern.Escape(null!);
        var startsWith = () => SqlLikePattern.StartsWith(null!);
        var contains = () => SqlLikePattern.Contains(null!);

        // then
        escape.Should().Throw<ArgumentNullException>();
        startsWith.Should().Throw<ArgumentNullException>();
        contains.Should().Throw<ArgumentNullException>();
    }
}
