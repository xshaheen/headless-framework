// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using FluentValidation.TestHelper;

namespace Tests;

public sealed class PhoneNumberValidatorsCountryCodeTests
{
    #region Nullable int (int?)

    private readonly NullableTestModelValidator _nullableSut = new();

    private sealed record NullableTestModel(int? CountryCode);

    private sealed class NullableTestModelValidator : AbstractValidator<NullableTestModel>
    {
        public NullableTestModelValidator() => RuleFor(x => x.CountryCode).PhoneCountryCode();
    }

    [Fact]
    public void should_not_have_error_when_nullable_country_code_is_null()
    {
        var model = new NullableTestModel(CountryCode: null);
        var result = _nullableSut.TestValidate(model);
        result.ShouldNotHaveValidationErrorFor(x => x.CountryCode);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(20)]
    [InlineData(44)]
    public void should_not_have_error_when_nullable_country_code_is_positive(int countryCode)
    {
        var model = new NullableTestModel(countryCode);
        var result = _nullableSut.TestValidate(model);
        result.ShouldNotHaveValidationErrorFor(x => x.CountryCode);
    }

    [Fact]
    public void should_have_error_when_nullable_country_code_is_zero()
    {
        var model = new NullableTestModel(CountryCode: 0);
        var result = _nullableSut.TestValidate(model);
        result.ShouldHaveValidationErrorFor(x => x.CountryCode);
    }

    [Fact]
    public void should_have_error_when_nullable_country_code_is_negative()
    {
        var model = new NullableTestModel(CountryCode: -1);
        var result = _nullableSut.TestValidate(model);
        result.ShouldHaveValidationErrorFor(x => x.CountryCode);
    }

    #endregion

    #region Non-nullable int

    private readonly NonNullableTestModelValidator _nonNullableSut = new();

    private sealed record NonNullableTestModel(int CountryCode);

    private sealed class NonNullableTestModelValidator : AbstractValidator<NonNullableTestModel>
    {
        public NonNullableTestModelValidator() => RuleFor(x => x.CountryCode).PhoneCountryCode();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(20)]
    [InlineData(44)]
    public void should_not_have_error_when_country_code_is_positive(int countryCode)
    {
        var model = new NonNullableTestModel(countryCode);
        var result = _nonNullableSut.TestValidate(model);
        result.ShouldNotHaveValidationErrorFor(x => x.CountryCode);
    }

    [Fact]
    public void should_have_error_when_country_code_is_zero()
    {
        var model = new NonNullableTestModel(CountryCode: 0);
        var result = _nonNullableSut.TestValidate(model);
        result.ShouldHaveValidationErrorFor(x => x.CountryCode);
    }

    [Fact]
    public void should_have_error_when_country_code_is_negative()
    {
        var model = new NonNullableTestModel(CountryCode: -1);
        var result = _nonNullableSut.TestValidate(model);
        result.ShouldHaveValidationErrorFor(x => x.CountryCode);
    }

    #endregion
}
