// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using FluentValidation;
using FluentValidation.TestHelper;

namespace Framework.FluentValidation.Tests.Unit;

public sealed class EgyptianNationalIdValidatorsTests
{
    private sealed class TestModel
    {
        public string? NationalId { get; init; }
    }

    private sealed class TestModelValidator : AbstractValidator<TestModel>
    {
        public TestModelValidator()
        {
            RuleFor(x => x.NationalId).EgyptianNationalId();
        }
    }

    [Fact]
    public void should_not_have_error_when_national_id_is_null()
    {
        var validator = new TestModelValidator();
        var model = new TestModel { NationalId = null };
        var result = validator.TestValidate(model);
        result.ShouldNotHaveValidationErrorFor(x => x.NationalId);
    }

    [Fact]
    public void Should_Have_Error_When_NationalId_Is_Invalid_Length()
    {
        var validator = new TestModelValidator();
        var model = new TestModel { NationalId = "1234567890" }; // 10 characters
        var result = validator.TestValidate(model);
        result.ShouldHaveValidationErrorFor(x => x.NationalId);
    }

    [Fact]
    public void Should_Have_Error_When_NationalId_Is_Invalid()
    {
        var validator = new TestModelValidator();
        var model = new TestModel { NationalId = "12345678901" }; // 11 characters but invalid
        var result = validator.TestValidate(model);
        result.ShouldHaveValidationErrorFor(x => x.NationalId);
    }

    [Fact]
    public void should_not_have_error_when_national_id_is_valid()
    {
        var validator = new TestModelValidator();
        var model = new TestModel { NationalId = "29809291702345" }; // Assuming this is a valid Id
        var result = validator.TestValidate(model);
        result.ShouldNotHaveValidationErrorFor(x => x.NationalId);
    }
}
