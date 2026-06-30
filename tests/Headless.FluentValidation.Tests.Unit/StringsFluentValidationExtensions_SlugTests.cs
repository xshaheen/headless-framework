// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using FluentValidation.TestHelper;

namespace Tests;

public sealed class StringsFluentValidationExtensionsSlugTests
{
    private sealed class Model
    {
        public string? Value { get; init; }
    }

    private sealed class ModelValidator : AbstractValidator<Model>
    {
        public ModelValidator()
        {
            RuleFor(x => x.Value).Slug();
        }
    }

    #region Valid Slugs

    [Theory]
    [InlineData("hello")]
    [InlineData("hello-world")]
    [InlineData("456")]
    [InlineData("a1-b2")]
    [InlineData("hello-456-world")]
    public void should_not_have_error_when_string_is_a_slug(string value)
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
    [InlineData("Hello")]
    [InlineData("hello_world")]
    [InlineData("-hello")]
    [InlineData("hello-")]
    [InlineData("hello--world")]
    [InlineData("hello world")]
    [InlineData("")]
    [InlineData(" ")]
    public void should_have_error_when_string_is_not_a_slug(string value)
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
        var model = new Model { Value = "Hello" };

        var result = validator.TestValidate(model);

        result.ShouldHaveValidationErrorFor(x => x.Value).WithErrorCode("strings:invalid_slug");
    }

    #endregion
}
