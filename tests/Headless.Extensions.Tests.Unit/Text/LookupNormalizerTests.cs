// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Text;

namespace Tests.Text;

public sealed class LookupNormalizerTests
{
    private readonly Faker _faker = new();

    // NormalizeUserName tests

    [Fact]
    public void normalize_username_should_return_uppercase_trimmed_name()
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
    public void normalize_username_should_return_null_when_input_is_null()
    {
        // given
        const string? input = null;

        // when
        var result = LookupNormalizer.NormalizeUserName(input);

        // then
        result.Should().BeNull();
    }

    [Fact]
    public void normalize_username_should_normalize_unicode_characters()
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
    public void normalize_username_should_return_null_when_input_is_whitespace_only()
    {
        // given
        const string input = "   ";

        // when
        var result = LookupNormalizer.NormalizeUserName(input);

        // then
        result.Should().BeNull();
    }

    [Fact]
    public void normalize_username_should_return_null_for_empty_string()
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
    public void normalize_email_should_return_uppercase_email()
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
    public void normalize_email_should_return_null_when_email_is_null()
    {
        // given
        const string? email = null;

        // when
        var result = LookupNormalizer.NormalizeEmail(email);

        // then
        result.Should().BeNull();
    }

    [Fact]
    public void normalize_email_should_trim_whitespace()
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
    public void normalize_phone_number_should_remove_spaces_and_trim()
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
    public void normalize_phone_number_should_return_null_when_input_is_null()
    {
        // given
        const string? phoneNumber = null;

        // when
        var result = LookupNormalizer.NormalizePhoneNumber(phoneNumber);

        // then
        result.Should().BeNull();
    }

    [Fact]
    public void normalize_phone_number_should_convert_arabic_digits_to_invariant()
    {
        // given - Arabic-Indic digits (٠١٢٣٤٥٦٧٨٩) - note: ٠ = 0, trailing 0 removed
        const string phoneNumber = "١٢٣٤٥٦٧٨٩";
        const string expected = "123456789";

        // when
        var result = LookupNormalizer.NormalizePhoneNumber(phoneNumber);

        // then
        result.Should().Be(expected);
    }

    [Fact]
    public void normalize_phone_number_should_convert_persian_digits_to_invariant()
    {
        // given - Persian digits (۱۲۳۴۵۶۷۸۹) - note: ۰ = 0, trailing 0 removed
        const string phoneNumber = "۱۲۳۴۵۶۷۸۹";
        const string expected = "123456789";

        // when
        var result = LookupNormalizer.NormalizePhoneNumber(phoneNumber);

        // then
        result.Should().Be(expected);
    }

    [Fact]
    public void normalize_phone_number_should_remove_single_trailing_zero()
    {
        // given - RemovePostfix removes only one trailing "0"
        const string phoneNumber = "1234567890";
        const string expected = "123456789";

        // when
        var result = LookupNormalizer.NormalizePhoneNumber(phoneNumber);

        // then
        result.Should().Be(expected);
    }

    [Fact]
    public void normalize_phone_number_should_preserve_leading_zeros()
    {
        // given - leading zeros preserved, only trailing "0" removed
        const string phoneNumber = "0012345670";
        const string expected = "001234567";

        // when
        var result = LookupNormalizer.NormalizePhoneNumber(phoneNumber);

        // then
        result.Should().Be(expected);
    }

    [Fact]
    public void normalize_phone_number_should_handle_plus_prefix()
    {
        // given - plus preserved, spaces removed, trailing 0 removed
        const string phoneNumber = "+1 234 567 80";
        const string expected = "+12345678";

        // when
        var result = LookupNormalizer.NormalizePhoneNumber(phoneNumber);

        // then
        result.Should().Be(expected);
    }

    [Fact]
    public void normalize_phone_number_should_not_remove_non_trailing_zero()
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
