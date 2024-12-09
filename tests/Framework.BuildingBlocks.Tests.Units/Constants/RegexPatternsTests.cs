// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.BuildingBlocks;

namespace Tests.Constants;

public sealed class RegexPatternsTests
{
     [Theory]
        [InlineData("test@example.com", true)]
        [InlineData("invalid-email@", false)]
        [InlineData("user@domain.co.uk", true)]
        [InlineData("user@", false)]
        public void email_address_should_match_expected_results(string input, bool isMatch)
        {
            // given
            var result = RegexPatterns.EmailAddress().IsMatch(input);

            // when
            result.Should().Be(isMatch);
        }

        [Theory]
        [InlineData("مرحبا", true)]
        [InlineData("hello", false)]
        public void arabic_characters_should_match_expected_results(string input, bool isMatch)
        {
            // given
            var result = RegexPatterns.ArabicCharacters().IsMatch(input);

            // when
            result.Should().Be(isMatch);
        }

        [Theory]
        [InlineData("سلام", true)]
        [InlineData("world", false)]
        public void rtl_characters_should_match_expected_results(string input, bool isMatch)
        {
            // given
            var result = RegexPatterns.RtlCharacters().IsMatch(input);

            // when
            result.Should().Be(isMatch);
        }

        [Theory]
        [InlineData("12345678901234", true)]
        [InlineData("123456", false)]
        [InlineData("1234567890123X", false)]
        public void egyptian_national_id_should_match_expected_results(string input, bool isMatch)
        {
            // given
            var result = RegexPatterns.EgyptianNationalId().IsMatch(input);

            // when
            result.Should().Be(isMatch);
        }

        [Theory]
        [InlineData("   ", true)]
        [InlineData("text", false)]
        [InlineData("\t", true)]
        public void spaces_should_match_expected_results(string input, bool isMatch)
        {
            // given
            var result = RegexPatterns.Spaces().IsMatch(input);

            // when
            result.Should().Be(isMatch);
        }

        [Theory]
        [InlineData("\u001A", true)]
        [InlineData("A", false)]
        public void hidden_chars_should_match_expected_results(string input, bool isMatch)
        {
            // given
            var result = RegexPatterns.HiddenChars().IsMatch(input);

            // when
            result.Should().Be(isMatch);
        }

        [Theory]
        [InlineData("'Quoted Text'", true)]
        [InlineData("\"Another Quoted Text\"", true)]
        [InlineData("NoQuotes", false)]
        public void quotes_should_match_expected_results(string input, bool isMatch)
        {
            // given
            var result = RegexPatterns.Quotes().IsMatch(input);

            // when
            result.Should().Be(isMatch);
        }

        [Theory]
        [InlineData("192.168.1.1", true)]
        [InlineData("256.256.256.256", false)]
        [InlineData("10.0.0.1", true)]
        public void ip4_should_match_expected_results(string input, bool isMatch)
        {
            // given
            var result = RegexPatterns.Ip4().IsMatch(input);

            // when
            result.Should().Be(isMatch);
        }

        [Theory]
        [InlineData("2001:0db8:85a3:0000:0000:8a2e:0370:7334", true)]
        [InlineData("::1", true)]
        [InlineData("invalid-ip6", false)]
        public void ip6_should_match_expected_results(string input, bool isMatch)
        {
            // given
            var result = RegexPatterns.Ip6().IsMatch(input);

            // when
            result.Should().Be(isMatch);
        }

        [Theory]
        [InlineData("255.255.255.255", true)]
        [InlineData("192.168.0.1", true)]
        [InlineData("99999", false)]
        public void ip_address_range_should_match_expected_results(string input, bool isMatch)
        {
            // when
            var result = RegexPatterns.IpAddressRange().IsMatch(input);

            // then
            result.Should().Be(isMatch);
        }
}
