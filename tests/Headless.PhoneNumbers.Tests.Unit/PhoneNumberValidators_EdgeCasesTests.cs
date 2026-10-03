// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using FluentValidation.TestHelper;

namespace Tests;

public sealed class PhoneNumberValidatorsEdgeCasesTests
{
    private readonly TestModelValidator _sut = new();

    private sealed record TestModel(string? PhoneNumber);

    private sealed class TestModelValidator : AbstractValidator<TestModel>
    {
        public TestModelValidator() => RuleFor(x => x.PhoneNumber).InternationalPhoneNumber();
    }

    [Fact]
    public void should_not_have_error_when_whitespace_only()
    {
        // Validator returns early for null/whitespace - no validation error
        var model = new TestModel(PhoneNumber: "   ");
        var result = _sut.TestValidate(model);
        result.ShouldNotHaveValidationErrorFor(x => x.PhoneNumber);
    }

    [Fact]
    public void should_have_error_when_plus_sign_only()
    {
        var model = new TestModel(PhoneNumber: "+");
        var result = _sut.TestValidate(model);
        result.ShouldHaveValidationErrorFor(x => x.PhoneNumber);
    }

    [Fact]
    public void should_have_error_when_too_short()
    {
        var model = new TestModel(PhoneNumber: "+1234");
        var result = _sut.TestValidate(model);
        result.ShouldHaveValidationErrorFor(x => x.PhoneNumber);
    }

    [Fact]
    public void should_have_error_when_too_long()
    {
        var model = new TestModel(PhoneNumber: "+12345678901234567890");
        var result = _sut.TestValidate(model);
        result.ShouldHaveValidationErrorFor(x => x.PhoneNumber);
    }

    [Fact]
    public void should_have_error_when_not_a_number()
    {
        // libphonenumber converts letters to keypad digits, so use non-numeric input
        var model = new TestModel(PhoneNumber: "not-a-phone-number");
        var result = _sut.TestValidate(model);
        result.ShouldHaveValidationErrorFor(x => x.PhoneNumber);
    }

    [Fact]
    public void should_not_have_error_when_valid_international_format()
    {
        var model = new TestModel(PhoneNumber: "+14155552671");
        var result = _sut.TestValidate(model);
        result.ShouldNotHaveValidationErrorFor(x => x.PhoneNumber);
    }
}
