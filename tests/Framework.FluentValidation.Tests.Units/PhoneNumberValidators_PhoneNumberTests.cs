// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using FluentValidation.TestHelper;

namespace Framework.FluentValidation.Tests.Unit;

public sealed class PhoneNumberValidators_PhoneNumberTests
{
    private readonly TestModelValidator _sut = new();

    private sealed record TestModel(int CountryCode, string? PhoneNumber);

    private sealed class TestModelValidator : AbstractValidator<TestModel>
    {
        public TestModelValidator() => RuleFor(x => x.PhoneNumber).PhoneNumber(x => x.CountryCode);
    }

    [Fact]
    public void should_not_have_error_when_phone_number_is_null()
    {
        var model = new TestModel(CountryCode: 20, PhoneNumber: null);
        var result = _sut.TestValidate(model);
        result.ShouldNotHaveValidationErrorFor(x => x.PhoneNumber);
    }

    [Theory]
    [InlineData(20, "01061534567")]
    [InlineData(20, "1061534567")]
    [InlineData(20, "10-615-34567")]
    [InlineData(20, "10 615 34567")]
    [InlineData(20, "(10) 615 34567")]
    [InlineData(20, "10AA-6153456")]
    public void should_not_have_error_when_phone_number_is_valid(int countryCode, string phoneNumber)
    {
        var model = new TestModel(countryCode, phoneNumber);
        var result = _sut.TestValidate(model);
        result.ShouldNotHaveValidationErrorFor(x => x.PhoneNumber);
    }

    [Theory]
    [InlineData(20, "invalid-phone-number")]
    [InlineData(20, "133333333613567")]
    [InlineData(20, "161534333567")]
    public void should_have_error_when_phone_number_is_invalid(int countryCode, string phoneNumber)
    {
        var model = new TestModel(countryCode, phoneNumber);
        var result = _sut.TestValidate(model);
        result.ShouldHaveValidationErrorFor(x => x.PhoneNumber);
    }
}
