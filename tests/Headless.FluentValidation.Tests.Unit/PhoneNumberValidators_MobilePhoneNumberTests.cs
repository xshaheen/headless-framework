// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using FluentValidation.TestHelper;

namespace Tests;

public sealed class PhoneNumberValidatorsMobilePhoneNumberTests
{
    private sealed record CountryCodeModel(int CountryCode, string? PhoneNumber);

    private sealed class CountryCodeValidator : AbstractValidator<CountryCodeModel>
    {
        public CountryCodeValidator() => RuleFor(x => x.PhoneNumber).MobilePhoneNumber(x => x.CountryCode);
    }

    private sealed record RegionModel(string RegionCode, string? PhoneNumber);

    private sealed class RegionValidator : AbstractValidator<RegionModel>
    {
        public RegionValidator() => RuleFor(x => x.PhoneNumber).MobilePhoneNumber(x => x.RegionCode);
    }

    #region Country Code Overload

    [Theory]
    [InlineData(20, "01061534567")]
    [InlineData(20, "1061534567")]
    [InlineData(20, "10-615-34567")]
    public void should_not_have_error_when_mobile_number_is_valid_for_country_code(int countryCode, string phoneNumber)
    {
        var validator = new CountryCodeValidator();
        var model = new CountryCodeModel(countryCode, phoneNumber);

        var result = validator.TestValidate(model);

        result.ShouldNotHaveValidationErrorFor(x => x.PhoneNumber);
    }

    [Theory]
    [InlineData(20, "123")]
    [InlineData(20, "1")]
    [InlineData(20, "abc")]
    [InlineData(20, "00000000000000000")]
    public void should_have_error_when_mobile_number_is_invalid_for_country_code(int countryCode, string phoneNumber)
    {
        var validator = new CountryCodeValidator();
        var model = new CountryCodeModel(countryCode, phoneNumber);

        var result = validator.TestValidate(model);

        result.ShouldHaveValidationErrorFor(x => x.PhoneNumber);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void should_not_have_error_when_value_is_null_or_whitespace_for_country_code(string? phoneNumber)
    {
        var validator = new CountryCodeValidator();
        var model = new CountryCodeModel(20, phoneNumber);

        var result = validator.TestValidate(model);

        result.ShouldNotHaveValidationErrorFor(x => x.PhoneNumber);
    }

    #endregion

    #region Region Code Overload

    [Theory]
    [InlineData("EG", "01061534567")]
    [InlineData("EG", "1061534567")]
    public void should_not_have_error_when_mobile_number_is_valid_for_region(string regionCode, string phoneNumber)
    {
        var validator = new RegionValidator();
        var model = new RegionModel(regionCode, phoneNumber);

        var result = validator.TestValidate(model);

        result.ShouldNotHaveValidationErrorFor(x => x.PhoneNumber);
    }

    [Theory]
    [InlineData("EG", "123")]
    [InlineData("EG", "abc")]
    public void should_have_error_when_mobile_number_is_invalid_for_region(string regionCode, string phoneNumber)
    {
        var validator = new RegionValidator();
        var model = new RegionModel(regionCode, phoneNumber);

        var result = validator.TestValidate(model);

        result.ShouldHaveValidationErrorFor(x => x.PhoneNumber);
    }

    [Fact]
    public void should_not_have_error_when_value_is_null_for_region()
    {
        var validator = new RegionValidator();
        var model = new RegionModel("EG", PhoneNumber: null);

        var result = validator.TestValidate(model);

        result.ShouldNotHaveValidationErrorFor(x => x.PhoneNumber);
    }

    #endregion

    #region Error Code

    [Fact]
    public void should_have_correct_error_code()
    {
        var validator = new CountryCodeValidator();
        var model = new CountryCodeModel(20, "123");

        var result = validator.TestValidate(model);

        result.ShouldHaveValidationErrorFor(x => x.PhoneNumber).WithErrorCode("phone_number:invalid_mobile_number");
    }

    #endregion
}
