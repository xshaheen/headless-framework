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
        // given - Arabic-Indic digits (٠١٢٣٤٥٦٧٨٩)
        const string phoneNumber = "١٢٣٤٥٦٧٨٩";
        const string expected = "123456789";

        // when
        var result = LookupNormalizer.NormalizePhoneNumber(phoneNumber);

        // then
        result.Should().Be(expected);
    }

    [Fact]
    public void should_convert_persian_digits_to_invariant_when_normalize_phone_number()
    {
        // given - Persian digits (۱۲۳۴۵۶۷۸۹)
        const string phoneNumber = "۱۲۳۴۵۶۷۸۹";
        const string expected = "123456789";

        // when
        var result = LookupNormalizer.NormalizePhoneNumber(phoneNumber);

        // then
        result.Should().Be(expected);
    }

    [Fact]
    public void should_keep_trailing_zero_when_normalize_phone_number()
    {
        // given - a final 0 is a digit of the number, not formatting
        const string phoneNumber = "1234567890";
        const string expected = "1234567890";

        // when
        var result = LookupNormalizer.NormalizePhoneNumber(phoneNumber);

        // then
        result.Should().Be(expected);
    }

    [Fact]
    public void should_keep_distinct_keys_when_numbers_differ_only_by_trailing_zero()
    {
        // when
        var withZero = LookupNormalizer.NormalizePhoneNumber("+201200000910");
        var withoutZero = LookupNormalizer.NormalizePhoneNumber("+20120000091");

        // then
        withZero.Should().Be("+201200000910");
        withZero.Should().NotBe(withoutZero);
    }

    [Fact]
    public void should_preserve_leading_and_trailing_zeros_when_normalize_phone_number()
    {
        // given
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
        // given - plus preserved, spaces removed
        const string phoneNumber = "+1 234 567 80";
        const string expected = "+123456780";

        // when
        var result = LookupNormalizer.NormalizePhoneNumber(phoneNumber);

        // then
        result.Should().Be(expected);
    }

    [Fact]
    public void should_return_digits_unchanged_when_normalize_phone_number_has_no_formatting()
    {
        // given
        const string phoneNumber = "123456789";
        const string expected = "123456789";

        // when
        var result = LookupNormalizer.NormalizePhoneNumber(phoneNumber);

        // then
        result.Should().Be(expected);
    }
}
