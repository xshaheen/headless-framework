// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using FluentValidation.TestHelper;

namespace Tests;

public sealed class StringsFluentValidationExtensionsBase64Tests
{
    private sealed class Model
    {
        public string? Value { get; init; }
    }

    private sealed class ModelValidator : AbstractValidator<Model>
    {
        public ModelValidator()
        {
            RuleFor(x => x.Value).Base64();
        }
    }

    #region Valid Base64

    [Theory]
    [InlineData("aGVsbG8=")]
    [InlineData("YWJj")]
    [InlineData("AAAA")]
    [InlineData("Zm9vYmE=")]
    // Base64.IsValid ignores whitespace, so the empty and whitespace-only strings are valid base64.
    // Combine .Base64() with .NotEmpty() when a non-empty value is required.
    [InlineData("")]
    [InlineData(" ")]
    public void should_not_have_error_when_string_is_base64(string value)
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
    [InlineData("a")]
    [InlineData("ab")]
    [InlineData("abc")]
    [InlineData("!!!!")]
    [InlineData("====")]
    [InlineData("abcde")]
    public void should_have_error_when_string_is_not_base64(string value)
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

        result.ShouldHaveValidationErrorFor(x => x.Value).WithErrorCode("g:invalid_base64");
    }

    #endregion
}
