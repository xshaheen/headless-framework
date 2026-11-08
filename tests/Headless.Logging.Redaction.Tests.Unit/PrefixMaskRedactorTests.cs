// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Logging;
using Headless.Testing.Tests;

namespace Tests;

public sealed class PrefixMaskRedactorTests : TestBase
{
    [Theory]
    [InlineData("abcdefghijkl", "abcdefgh***")]
    [InlineData("abcdefgh", "***")]
    [InlineData("abc", "***")]
    public void should_keep_eight_characters_by_default_and_hide_short_values_whole(string input, string expected)
    {
        new PrefixMaskRedactor().Redact(input).Should().Be(expected);
    }

    [Fact]
    public void should_leave_an_empty_value_empty()
    {
        // Microsoft's Redactor returns an empty input as is, before the subclass runs; empty reveals nothing.
        new PrefixMaskRedactor()
            .Redact(string.Empty)
            .Should()
            .BeEmpty();
    }

    [Fact]
    public void should_keep_the_requested_number_of_characters()
    {
        new PrefixMaskRedactor(visibleLength: 3).Redact("token-value").Should().Be("tok***");
    }

    [Fact]
    public void should_report_the_redacted_length_it_writes()
    {
        // given
        var redactor = new PrefixMaskRedactor(visibleLength: 4);
        Span<char> destination = stackalloc char[32];

        // when
        var written = redactor.Redact("0123456789", destination);

        // then
        written.Should().Be(redactor.GetRedactedLength("0123456789"));
        destination[..written].ToString().Should().Be("0123***");
    }

    [Fact]
    public void should_throw_when_the_visible_length_is_negative()
    {
        var act = () => new PrefixMaskRedactor(visibleLength: -1);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
