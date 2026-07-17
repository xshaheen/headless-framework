// Copyright (c) Mahmoud Shaheen. All rights reserved.

using RegexPatterns = Headless.Constants.RegexPatterns;

namespace Tests.Constants;

public sealed class RegexPatternsTests
{
    [Theory]
    [InlineData("test@example.com", true)]
    [InlineData("invalid-email@", false)]
    [InlineData("user@domain.co.uk", true)]
    [InlineData("user@", false)]
    public void should_match_expected_results_when_email_address(string input, bool isMatch)
    {
        // given
        var result = RegexPatterns.EmailAddress.IsMatch(input);

        // when
        result.Should().Be(isMatch);
    }

    [Theory]
    [InlineData("مرحبا", true)]
    [InlineData("ࢰ", true)] // Arabic Extended-A block (U+08A0-U+08FF), excluded by the previous en-dash bug
    [InlineData("hello", false)]
    public void arabic_characters_should_match_expected_results(string input, bool isMatch)
    {
        // given
        var result = RegexPatterns.ArabicCharacters.IsMatch(input);

        // when
        result.Should().Be(isMatch);
    }

    [Theory]
    [InlineData("سلام", true)]
    [InlineData("world", false)]
    public void should_match_expected_results_when_rtl_characters(string input, bool isMatch)
    {
        // given
        var result = RegexPatterns.RtlCharacters.IsMatch(input);

        // when
        result.Should().Be(isMatch);
    }

    [Theory]
    [InlineData("12345678901234", true)]
    [InlineData("123456", false)]
    [InlineData("1234567890123X", false)]
    public void should_match_expected_results_when_egyptian_national_id(string input, bool isMatch)
    {
        // given
        var result = RegexPatterns.EgyptianNationalId.IsMatch(input);

        // when
        result.Should().Be(isMatch);
    }

    [Theory]
    [InlineData("   ", true)]
    [InlineData("text", false)]
    [InlineData("\t", true)]
    public void should_match_expected_results_when_spaces(string input, bool isMatch)
    {
        // given
        var result = RegexPatterns.Spaces.IsMatch(input);

        // when
        result.Should().Be(isMatch);
    }

    [Theory]
    [InlineData("\u001A", true)]
    [InlineData("A", false)]
    public void should_match_expected_results_when_hidden_chars(string input, bool isMatch)
    {
        // given
        var result = RegexPatterns.HiddenChars.IsMatch(input);

        // when
        result.Should().Be(isMatch);
    }

    [Theory]
    [InlineData("'Quoted Text'", true)]
    [InlineData("\"Another Quoted Text\"", true)]
    [InlineData("NoQuotes", false)]
    public void should_match_expected_results_when_quotes(string input, bool isMatch)
    {
        // given
        var result = RegexPatterns.Quotes.IsMatch(input);

        // when
        result.Should().Be(isMatch);
    }

    [Theory]
    [InlineData("192.168.1.1", true)]
    [InlineData("256.256.256.256", false)]
    [InlineData("10.0.0.1", true)]
    public void should_match_expected_results_when_ip4(string input, bool isMatch)
    {
        // given
        var result = RegexPatterns.Ip4.IsMatch(input);

        // when
        result.Should().Be(isMatch);
    }

    [Theory]
    [InlineData("2001:0db8:85a3:0000:0000:8a2e:0370:7334", true)]
    [InlineData("::1", true)]
    [InlineData("invalid-ip6", false)]
    public void should_match_expected_results_when_ip6(string input, bool isMatch)
    {
        // given
        var result = RegexPatterns.Ip6.IsMatch(input);

        // when
        result.Should().Be(isMatch);
    }

    [Theory]
    [InlineData("255.255.255.255", true)]
    [InlineData("192.168.0.1", true)]
    [InlineData("99999", false)]
    public void should_match_expected_results_when_ip_address_range(string input, bool isMatch)
    {
        // when
        var result = RegexPatterns.IpAddressRange.IsMatch(input);

        // then
        result.Should().Be(isMatch);
    }

    [Theory]
    [InlineData("123", true)]
    [InlineData("0", true)]
    [InlineData("-5", true)]
    [InlineData("-10", true)] // regression: negatives containing a 0 were previously rejected
    [InlineData("-100", true)]
    [InlineData("-90", true)]
    [InlineData("-0", true)]
    [InlineData("12.5", false)]
    [InlineData("abc", false)]
    [InlineData("", false)]
    [InlineData("-", false)]
    public void integer_number_should_match_expected_results(string input, bool isMatch)
    {
        // when
        var result = RegexPatterns.IntegerNumber.IsMatch(input);

        // then
        result.Should().Be(isMatch);
    }

    [Theory]
    [InlineData("123", true)]
    [InlineData("0.5", true)]
    [InlineData("5,5", true)]
    [InlineData("-10", true)] // regression: negatives containing a 0 were previously rejected
    [InlineData("-0.5", true)] // regression
    [InlineData("-10.5", true)] // regression
    [InlineData("3.14", true)]
    [InlineData("5.", false)]
    [InlineData(".5", false)]
    [InlineData("abc", false)]
    [InlineData("", false)]
    public void decimal_number_should_match_expected_results(string input, bool isMatch)
    {
        // when
        var result = RegexPatterns.DecimalNumber.IsMatch(input);

        // then
        result.Should().Be(isMatch);
    }

    [Theory]
    [InlineData("https://example.com", true)]
    [InlineData("http://example.com/path/to/page", true)]
    [InlineData("example.com", true)]
    [InlineData("!!!", false)]
    public void should_match_expected_results_when_url(string input, bool isMatch)
    {
        // when
        var result = RegexPatterns.Url.IsMatch(input);

        // then
        result.Should().Be(isMatch);
    }

    [Theory]
    [InlineData("<div>hello</div>", true)]
    [InlineData("<br/>", true)]
    [InlineData("plain text", false)]
    public void should_match_expected_results_when_xml_tag(string input, bool isMatch)
    {
        // when
        var result = RegexPatterns.XmlTag.IsMatch(input);

        // then
        result.Should().Be(isMatch);
    }
}
