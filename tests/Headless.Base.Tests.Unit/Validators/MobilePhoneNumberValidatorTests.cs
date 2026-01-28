// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Validators;

namespace Tests.Validators;

public sealed class MobilePhoneNumberValidatorTests
{
    [Theory]
    [InlineData("01001234567", 20)]
    [InlineData("01112345678", 20)]
    [InlineData("01212345678", 20)]
    [InlineData("01512345678", 20)]
    public void should_return_true_for_valid_egypt_mobile(string phone, int countryCode)
    {
        var result = MobilePhoneNumberValidator.IsValid(phone, countryCode);
        result.Should().BeTrue();
    }

    [Theory]
    [InlineData("2025551234", 1)]
    [InlineData("4155551234", 1)]
    public void should_return_true_for_valid_us_mobile(string phone, int countryCode)
    {
        var result = MobilePhoneNumberValidator.IsValid(phone, countryCode);
        result.Should().BeTrue();
    }

    [Theory]
    [InlineData(null, 20)]
    [InlineData(null, 1)]
    public void should_return_false_for_null(string? phone, int countryCode)
    {
        var result = MobilePhoneNumberValidator.IsValid(phone!, countryCode);
        result.Should().BeFalse();
    }

    [Theory]
    [InlineData("", 20)]
    [InlineData("", 1)]
    public void should_return_false_for_empty(string phone, int countryCode)
    {
        var result = MobilePhoneNumberValidator.IsValid(phone, countryCode);
        result.Should().BeFalse();
    }

    [Theory]
    [InlineData("   ", 20)]
    [InlineData("\t", 20)]
    public void should_return_false_for_whitespace(string phone, int countryCode)
    {
        var result = MobilePhoneNumberValidator.IsValid(phone, countryCode);
        result.Should().BeFalse();
    }

    [Fact]
    public void should_return_false_for_landline()
    {
        // Egyptian landline number (Cairo)
        var result = MobilePhoneNumberValidator.IsValid("0223456789", 20);
        result.Should().BeFalse();
    }

    [Theory]
    [InlineData("abc", 20)]
    [InlineData("123", 20)]
    [InlineData("notanumber", 1)]
    public void should_return_false_for_invalid_format(string phone, int countryCode)
    {
        var result = MobilePhoneNumberValidator.IsValid(phone, countryCode);
        result.Should().BeFalse();
    }

    [Theory]
    [InlineData("01001234567", "EG")]
    [InlineData("2025551234", "US")]
    public void should_validate_with_region_code(string phone, string regionCode)
    {
        var result = MobilePhoneNumberValidator.IsValid(phone, regionCode);
        result.Should().BeTrue();
    }

    [Fact]
    public void should_return_normalized_number()
    {
        var result = MobilePhoneNumberValidator.IsValid("+201001234567", out var normalized);

        result.Should().BeTrue();
        normalized.Should().Be("+201001234567");
    }

    [Fact]
    public void should_throw_for_international_without_plus()
    {
        var act = () => MobilePhoneNumberValidator.IsValid("201001234567", out _);
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void should_return_false_for_null_empty_whitespace_international(string? phone)
    {
        var result = MobilePhoneNumberValidator.IsValid(phone, out var normalized);

        result.Should().BeFalse();
        normalized.Should().BeNull();
    }

    [Fact]
    public void should_return_false_for_invalid_international_number()
    {
        var result = MobilePhoneNumberValidator.IsValid("+999999999999999", out var normalized);

        result.Should().BeFalse();
        normalized.Should().BeNull();
    }
}
