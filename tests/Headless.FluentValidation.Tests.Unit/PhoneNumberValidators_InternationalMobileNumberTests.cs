// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using FluentValidation.TestHelper;

namespace Tests;

public sealed class PhoneNumberValidatorsInternationalMobileNumberTests
{
    private readonly TestModelValidator _sut = new();

    private sealed record TestModel(string? PhoneNumber);

    private sealed class TestModelValidator : AbstractValidator<TestModel>
    {
        public TestModelValidator() => RuleFor(x => x.PhoneNumber).InternationalMobileNumber();
    }

    [Theory]
    [InlineData("+201061534567")]
    [InlineData("+20 1061534567")]
    [InlineData("+20-1061534567")]
    public void should_not_have_error_when_international_mobile_number_is_valid(string phoneNumber)
    {
        var model = new TestModel(phoneNumber);

        var result = _sut.TestValidate(model);

        result.ShouldNotHaveValidationErrorFor(x => x.PhoneNumber);
    }

    [Theory]
    [InlineData("201061534567")] // missing leading '+'
    [InlineData("01061534567")] // local form, no country code
    [InlineData("+123")] // too short
    [InlineData("+abc")]
    [InlineData("abc")]
    public void should_have_error_when_international_mobile_number_is_invalid(string phoneNumber)
    {
        var model = new TestModel(phoneNumber);

        var result = _sut.TestValidate(model);

        result.ShouldHaveValidationErrorFor(x => x.PhoneNumber);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void should_not_have_error_when_value_is_null_or_whitespace(string? phoneNumber)
    {
        var model = new TestModel(phoneNumber);

        var result = _sut.TestValidate(model);

        result.ShouldNotHaveValidationErrorFor(x => x.PhoneNumber);
    }

    [Fact]
    public void should_have_correct_error_code()
    {
        var model = new TestModel("201061534567");

        var result = _sut.TestValidate(model);

        result.ShouldHaveValidationErrorFor(x => x.PhoneNumber).WithErrorCode("g:invalid_mobile_number");
    }
}
