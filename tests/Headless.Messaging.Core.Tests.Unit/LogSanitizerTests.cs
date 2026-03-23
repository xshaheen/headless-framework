// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Internal;

namespace Tests;

public sealed class LogSanitizerTests
{
    [Fact]
    public void returns_null_for_null_input()
    {
        LogSanitizer.Sanitize(null).Should().BeNull();
    }

    [Fact]
    public void returns_same_string_when_no_sanitization_needed()
    {
        const string clean = "hello world 123";
        LogSanitizer.Sanitize(clean).Should().BeSameAs(clean);
    }

    [Theory]
    [InlineData("\u2028", "")]
    [InlineData("\u2029", "")]
    [InlineData("before\u2028after", "beforeafter")]
    [InlineData("before\u2029after", "beforeafter")]
    [InlineData("a\u2028b\u2029c", "abc")]
    public void strips_unicode_line_and_paragraph_separators(string input, string expected)
    {
        LogSanitizer.Sanitize(input).Should().Be(expected);
    }

    [Theory]
    [InlineData('\uD800')]
    [InlineData('\uDBFF')]
    [InlineData('\uDC00')]
    [InlineData('\uDFFF')]
    public void strips_lone_surrogates(char surrogate)
    {
        var input = $"before{surrogate}after";
        LogSanitizer.Sanitize(input).Should().Be("beforeafter");
    }

    [Fact]
    public void strips_control_characters()
    {
        LogSanitizer.Sanitize("line1\nline2\r\ttab").Should().Be("line1line2tab");
    }

    [Theory]
    [InlineData('\u202A')]
    [InlineData('\u202E')]
    [InlineData('\u2066')]
    [InlineData('\u2069')]
    public void strips_bidi_overrides_and_isolates(char bidi)
    {
        var input = $"text{bidi}more";
        LogSanitizer.Sanitize(input).Should().Be("textmore");
    }

    [Fact]
    public void strips_mixed_dangerous_characters()
    {
        // control + line sep + paragraph sep + lone surrogate + bidi
        var input = $"a\nb\u2028c\u2029d\uD800e\u202Af";
        LogSanitizer.Sanitize(input).Should().Be("abcdef");
    }

    [Fact]
    public void truncates_clean_string_to_max_length()
    {
        LogSanitizer.Sanitize("abcdefghij", maxLength: 7).Should().Be("abcd...");
    }

    [Fact]
    public void does_not_truncate_when_within_max_length()
    {
        const string input = "short";
        LogSanitizer.Sanitize(input, maxLength: 100).Should().BeSameAs(input);
    }

    [Fact]
    public void truncation_with_sanitization_strips_then_truncates()
    {
        // 'a\nb\u2028c' -> after strip: 'abc' (3 chars), maxLength=6 -> no truncation needed
        LogSanitizer.Sanitize("a\nb\u2028c", maxLength: 6).Should().Be("abc");
    }

    [Fact]
    public void truncation_with_sanitization_when_result_exceeds_max()
    {
        // 10 clean chars + dirty chars, maxLength=7 -> should get 4 clean chars + "..."
        LogSanitizer.Sanitize("abcdefghij\u2028", maxLength: 7).Should().Be("abcd...");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void truncation_with_max_length_less_than_suffix_does_not_throw(int maxLength)
    {
        // Should not throw ArgumentOutOfRangeException
        var result = LogSanitizer.Sanitize("abcdef", maxLength: maxLength);
        result.Should().Be("...");
    }

    [Fact]
    public void truncation_with_max_length_equal_to_suffix_returns_suffix_only()
    {
        LogSanitizer.Sanitize("abcdef", maxLength: 3).Should().Be("...");
    }

    [Fact]
    public void max_length_less_than_suffix_with_sanitization_does_not_throw()
    {
        var result = LogSanitizer.Sanitize("a\nb\u2028cdef", maxLength: 1);
        result.Should().Be("...");
    }
}
