// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Text;

namespace Tests.Text;

public sealed class LookupNormalizerTests
{
    private readonly Faker _faker = new();

    // NormalizeUserName tests

    [Fact]
    public void should_return_uppercase_trimmed_name_when_normalize_username()
    {
        // given
        const string input = "  John Doe  ";
        const string expected = "JOHN DOE";

        // when
        var result = LookupNormalizer.NormalizeUserName(input);

        // then
        result.Should().Be(expected);
    }

    [Fact]
    public void should_return_null_when_normalize_username_input_is_null()
    {
        // given
        const string? input = null;

        // when
        var result = LookupNormalizer.NormalizeUserName(input);

        // then
        result.Should().BeNull();
    }

    [Fact]
    public void should_normalize_unicode_characters_when_normalize_username()
    {
        // given - composed vs decomposed unicode (e.g., é can be single char or e + combining accent)
        const string input = "café";

        // when
        var result = LookupNormalizer.NormalizeUserName(input);

        // then
        result.Should().Be("CAFÉ");
        result.Should().Be(input.Normalize().ToUpperInvariant());
    }

    [Fact]
    public void should_return_null_when_normalize_username_input_is_whitespace_only()
    {
        // given
        const string input = "   ";

        // when
        var result = LookupNormalizer.NormalizeUserName(input);

        // then
        result.Should().BeNull();
    }

    [Fact]
    public void should_return_null_for_empty_string_when_normalize_username()
    {
        // given - NullableTrim returns null for empty strings
        const string input = "";

        // when
        var result = LookupNormalizer.NormalizeUserName(input);

        // then
        result.Should().BeNull();
    }

    // NormalizeEmail tests

    [Fact]
    public void should_return_uppercase_email_when_normalize_email()
    {
        // given
        var email = _faker.Internet.Email();
        var expected = email.ToUpperInvariant();

        // when
        var result = LookupNormalizer.NormalizeEmail(email);

        // then
        result.Should().Be(expected);
    }

    [Fact]
    public void should_return_null_when_normalize_email_email_is_null()
    {
        // given
        const string? email = null;

        // when
        var result = LookupNormalizer.NormalizeEmail(email);

        // then
        result.Should().BeNull();
    }

    [Fact]
    public void should_trim_whitespace_when_normalize_email()
    {
        // given
        const string email = "  Test@Example.Com  ";

        // when
        var result = LookupNormalizer.NormalizeEmail(email);

        // then
        result.Should().Be("TEST@EXAMPLE.COM");
    }

    // NormalizePhoneNumber tests

    [Fact]
    public void should_remove_spaces_and_trim_when_normalize_phone_number()
    {
        // given
        const string phoneNumber = " 123 456 789 ";
        const string expected = "123456789";

        // when
        var result = LookupNormalizer.NormalizePhoneNumber(phoneNumber);

        // then
        result.Should().Be(expected);
    }

    [Fact]
    public void should_return_null_when_normalize_phone_number_input_is_null()
    {
        // given
        const string? phoneNumber = null;

        // when
        var result = LookupNormalizer.NormalizePhoneNumber(phoneNumber);

        // then
        result.Should().BeNull();
    }

    [Fact]
    public void should_convert_arabic_digits_to_invariant_when_normalize_phone_number()
    {
        // given - Arabic-Indic digits, including a trailing ٠ that must survive as 0
        const string phoneNumber = "١٢٣٤٥٦٧٨٩٠";
        const string expected = "1234567890";

        // when
        var result = LookupNormalizer.NormalizePhoneNumber(phoneNumber);

        // then
        result.Should().Be(expected);
    }

    [Fact]
    public void should_convert_persian_digits_to_invariant_when_normalize_phone_number()
    {
        // given - Persian digits, including a trailing ۰ that must survive as 0
        const string phoneNumber = "۱۲۳۴۵۶۷۸۹۰";
        const string expected = "1234567890";

        // when
        var result = LookupNormalizer.NormalizePhoneNumber(phoneNumber);

        // then
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("+201200000100")]
    [InlineData("+201012345670")]
    [InlineData("1234567890")]
    public void should_keep_every_trailing_zero_when_normalize_phone_number(string phoneNumber)
    {
        // when
        var result = LookupNormalizer.NormalizePhoneNumber(phoneNumber);

        // then: dropping a trailing zero would dial a different subscriber
        result.Should().Be(phoneNumber);
    }

    [Fact]
    public void should_preserve_leading_zeros_when_normalize_phone_number()
    {
        // given - the normalizer has no region, so it cannot tell a trunk prefix from a significant leading zero
        const string phoneNumber = "0012345670";
        const string expected = "0012345670";

        // when
        var result = LookupNormalizer.NormalizePhoneNumber(phoneNumber);

        // then
        result.Should().Be(expected);
    }

    [Fact]
    public void should_handle_plus_prefix_when_normalize_phone_number()
    {
        // given - plus preserved, spaces removed, trailing 0 kept
        const string phoneNumber = "+1 234 567 80";
        const string expected = "+123456780";

        // when
        var result = LookupNormalizer.NormalizePhoneNumber(phoneNumber);

        // then
        result.Should().Be(expected);
    }

    [Fact]
    public void should_not_remove_non_trailing_zero_when_normalize_phone_number()
    {
        // given - no trailing 0 to remove
        const string phoneNumber = "123456789";
        const string expected = "123456789";

        // when
        var result = LookupNormalizer.NormalizePhoneNumber(phoneNumber);

        // then
        result.Should().Be(expected);
    }
}
