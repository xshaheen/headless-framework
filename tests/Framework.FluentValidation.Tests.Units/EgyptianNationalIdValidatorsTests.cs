// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using FluentValidation.TestHelper;
using Framework.FluentValidation;

namespace Tests;

public sealed class EgyptianNationalIdValidatorsTests
{
    private readonly TestModelValidator _sut = new();

    private sealed record TestModel(string? NationalId);

    private sealed class TestModelValidator : AbstractValidator<TestModel>
    {
        public TestModelValidator() => RuleFor(x => x.NationalId).EgyptianNationalId();
    }

    [Fact]
    public void should_not_have_error_when_national_id_is_null()
    {
        var model = new TestModel(NationalId: null);
        var result = _sut.TestValidate(model);
        result.ShouldNotHaveValidationErrorFor(x => x.NationalId);
    }

    [Theory]
    [InlineData("1234567890")]
    public void should_have_error_when_national_id_is_invalid(string nationalId)
    {
        var model = new TestModel(nationalId);
        var result = _sut.TestValidate(model);
        result.ShouldHaveValidationErrorFor(x => x.NationalId);
    }

    [Theory]
    [InlineData("29809291702345")]
    public void should_not_have_error_when_national_id_is_valid(string nationalId)
    {
        var model = new TestModel(nationalId);
        var result = _sut.TestValidate(model);
        result.ShouldNotHaveValidationErrorFor(x => x.NationalId);
    }
}
