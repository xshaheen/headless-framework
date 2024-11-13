// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using FluentValidation.TestHelper;

namespace Framework.FluentValidation.Tests.Unit;

public sealed class PhoneNumberValidators_BasicPhoneNumberTests
{
    private readonly TestModelValidator _sut = new();

    private sealed record TestModel(string? PhoneNumber);

    private sealed class TestModelValidator : AbstractValidator<TestModel>
    {
        public TestModelValidator() => RuleFor(x => x.PhoneNumber).BasicPhoneNumber();
    }

    [Fact]
    public void should_not_have_error_when_phone_number_is_null()
    {
        var model = new TestModel(PhoneNumber: null);
        var result = _sut.TestValidate(model);
        result.ShouldNotHaveValidationErrorFor(x => x.PhoneNumber);
    }

    [Theory]
    [InlineData("1234567890")]
    [InlineData("123-456-7890")]
    [InlineData("+1234567890")]
    public void should_not_have_error_when_phone_number_is_valid(string phoneNumber)
    {
        var model = new TestModel(phoneNumber);
        var result = _sut.TestValidate(model);
        result.ShouldNotHaveValidationErrorFor(x => x.PhoneNumber);
    }

    [Theory]
    [InlineData("invalid-phone-number")]
    [InlineData("i2p434")]
    public void should_have_error_when_phone_number_is_invalid(string phoneNumber)
    {
        var model = new TestModel(phoneNumber);
        var result = _sut.TestValidate(model);
        result.ShouldHaveValidationErrorFor(x => x.PhoneNumber);
    }
}
