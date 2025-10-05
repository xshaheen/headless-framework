// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using FluentValidation.TestHelper;
using Framework.FluentValidation;

namespace Tests;

public sealed class PhoneNumberValidatorsInternationalPhoneNumberTests
{
    private readonly TestModelValidator _sut = new();

    private sealed record TestModel(string? PhoneNumber);

    private sealed class TestModelValidator : AbstractValidator<TestModel>
    {
        public TestModelValidator() => RuleFor(x => x.PhoneNumber).InternationalPhoneNumber();
    }

    [Fact]
    public void should_not_have_error_when_phone_number_is_null()
    {
        var model = new TestModel(PhoneNumber: null);
        var result = _sut.TestValidate(model);
        result.ShouldNotHaveValidationErrorFor(x => x.PhoneNumber);
    }

    [Theory]
    [InlineData("+201061534567")]
    [InlineData("+20 1061534567")]
    [InlineData("+20-1061534567")]
    [InlineData("+20 (106) 1534567")]
    [InlineData("+20 10615 34567")]
    public void should_not_have_error_when_phone_number_is_valid(string phoneNumber)
    {
        var model = new TestModel(phoneNumber);
        var result = _sut.TestValidate(model);
        result.ShouldNotHaveValidationErrorFor(x => x.PhoneNumber);
    }

    [Theory]
    [InlineData("invalid-phone-number")]
    [InlineData("20 10615 34567")]
    [InlineData("133333333613567")]
    [InlineData("161534333567")]
    [InlineData("01061534567")]
    [InlineData("1061534567")]
    [InlineData("10-615-34567")]
    [InlineData("10 615 34567")]
    [InlineData("(10) 615 34567")]
    [InlineData("10AA-6153456")]
    public void should_have_error_when_phone_number_is_invalid(string phoneNumber)
    {
        var model = new TestModel(phoneNumber);
        var result = _sut.TestValidate(model);
        result.ShouldHaveValidationErrorFor(x => x.PhoneNumber);
    }
}
