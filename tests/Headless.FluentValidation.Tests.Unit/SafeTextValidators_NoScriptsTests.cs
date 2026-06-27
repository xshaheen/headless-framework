// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using FluentValidation.TestHelper;

namespace Tests;

public sealed class SafeTextValidatorsNoScriptsTests
{
    private sealed class Model
    {
        public string? Value { get; init; }
    }

    private sealed class ModelValidator : AbstractValidator<Model>
    {
        public ModelValidator()
        {
            RuleFor(x => x.Value).NoScripts();
        }
    }

    #region Valid (no script element)

    [Theory]
    [InlineData("hello")]
    [InlineData("<b>bold</b>")]
    [InlineData("<div>x</div>")]
    [InlineData("plain text")]
    public void should_not_have_error_when_string_contains_no_script_element(string value)
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

    #region Invalid (has script element)

    [Theory]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("<script src='x'></script>")]
    [InlineData("<script/>")]
    [InlineData("text<script>evil</script>more")]
    public void should_have_error_when_string_contains_script_element(string value)
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
        var model = new Model { Value = "<script>alert(1)</script>" };

        var result = validator.TestValidate(model);

        result.ShouldHaveValidationErrorFor(x => x.Value).WithErrorCode("strings:contains_scripts");
    }

    #endregion
}
