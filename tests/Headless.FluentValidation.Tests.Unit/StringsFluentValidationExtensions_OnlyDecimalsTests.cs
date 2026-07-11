// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using FluentValidation.TestHelper;

namespace Tests;

public sealed class StringsFluentValidationExtensionsOnlyDecimalsTests
{
    private sealed class Model
    {
        public string? Value { get; init; }
    }

    private sealed class ModelValidator : AbstractValidator<Model>
    {
        public ModelValidator()
        {
            RuleFor(x => x.Value).OnlyDecimals();
        }
    }

    #region Valid Decimals

    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("123")]
    [InlineData("-1")]
    [InlineData("-1.5")]
    [InlineData("1,5")]
    [InlineData("1.0")]
    [InlineData("-999.999")]
    public void should_not_have_error_when_string_is_a_decimal(string value)
    {
        var validator = new ModelValidator();
        var model = new Model { Value = value };

        var result = validator.TestValidate(model);

        result.ShouldNotHaveValidationErrorFor(x => x.Value);
    }

    #endregion

    #region Null Value

    [Fact]
    public void should_not_have_error_when_value_is_null()
    {
        var validator = new ModelValidator();
        var model = new Model { Value = null };

        var result = validator.TestValidate(model);

        result.ShouldNotHaveValidationErrorFor(x => x.Value);
    }

    #endregion

    #region Invalid Values

    [Theory]
    [InlineData("abc")]
    [InlineData("1a")]
    [InlineData("1.")]
    [InlineData(" ")]
    [InlineData("")]
    [InlineData("+1")]
    [InlineData("1 2")]
    [InlineData(".5")]
    public void should_have_error_when_string_is_not_a_decimal(string value)
    {
        var validator = new ModelValidator();
        var model = new Model { Value = value };

        var result = validator.TestValidate(model);

        result.ShouldHaveValidationErrorFor(x => x.Value);
    }

    #endregion

    #region Error Code

    [Fact]
    public void should_have_correct_error_code()
    {
        var validator = new ModelValidator();
        var model = new Model { Value = "abc" };

        var result = validator.TestValidate(model);

        result.ShouldHaveValidationErrorFor(x => x.Value).WithErrorCode("g:only_decimals");
    }

    #endregion
}
