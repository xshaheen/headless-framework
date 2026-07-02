// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using FluentValidation.TestHelper;

namespace Tests;

public sealed class StringsFluentValidationExtensionsTrimmedTests
{
    private sealed class Model
    {
        public string? Value { get; init; }
    }

    private sealed class ModelValidator : AbstractValidator<Model>
    {
        public ModelValidator()
        {
            RuleFor(x => x.Value).Trimmed();
        }
    }

    #region Valid Trimmed Values

    [Theory]
    [InlineData("abc")]
    [InlineData("hello world")]
    [InlineData("123")]
    [InlineData("")]
    public void should_not_have_error_when_string_is_trimmed(string value)
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
    [InlineData(" abc")]
    [InlineData("abc ")]
    [InlineData("  x  ")]
    [InlineData("\tabc")]
    [InlineData("abc\n")]
    public void should_have_error_when_string_is_not_trimmed(string value)
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
        var model = new Model { Value = " abc" };

        var result = validator.TestValidate(model);

        result.ShouldHaveValidationErrorFor(x => x.Value).WithErrorCode("strings:not_trimmed");
    }

    #endregion
}
