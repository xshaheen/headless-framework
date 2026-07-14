// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using FluentValidation.TestHelper;

namespace Tests;

public sealed class EnumValidatorsEnumNameTests
{
    private enum Color
    {
        Red,
        Green,
        Blue,
    }

    private sealed class Model
    {
        public string? Value { get; init; }
    }

    private sealed class ModelValidator : AbstractValidator<Model>
    {
        public ModelValidator()
        {
            RuleFor(x => x.Value).EnumName(typeof(Color));
        }
    }

    private sealed class IgnoreCaseModelValidator : AbstractValidator<Model>
    {
        public IgnoreCaseModelValidator()
        {
            RuleFor(x => x.Value).EnumName(typeof(Color), ignoreCase: true);
        }
    }

    #region Valid Names (case-sensitive)

    [Theory]
    [InlineData("Red")]
    [InlineData("Green")]
    [InlineData("Blue")]
    public void should_not_have_error_when_string_is_valid_enum_name(string value)
    {
        var validator = new ModelValidator();
        var model = new Model { Value = value };

        var result = validator.TestValidate(model);

        result.ShouldNotHaveValidationErrorFor(x => x.Value);
    }

    #endregion

    #region Invalid Names (case-sensitive)

    [Theory]
    [InlineData("red")]
    [InlineData("RED")]
    [InlineData("Yellow")]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("")]
    [InlineData(" ")]
    public void should_have_error_when_string_is_not_valid_enum_name(string value)
    {
        var validator = new ModelValidator();
        var model = new Model { Value = value };

        var result = validator.TestValidate(model);

        result.ShouldHaveValidationErrorFor(x => x.Value);
    }

    #endregion

    #region Valid Names (case-insensitive)

    [Theory]
    [InlineData("red")]
    [InlineData("RED")]
    [InlineData("green")]
    [InlineData("Blue")]
    public void should_not_have_error_when_string_is_valid_enum_name_ignoring_case(string value)
    {
        var validator = new IgnoreCaseModelValidator();
        var model = new Model { Value = value };

        var result = validator.TestValidate(model);

        result.ShouldNotHaveValidationErrorFor(x => x.Value);
    }

    #endregion

    #region Invalid Names (case-insensitive)

    [Theory]
    [InlineData("Yellow")]
    [InlineData("purple")]
    [InlineData("0")]
    [InlineData("")]
    public void should_have_error_when_string_is_not_valid_enum_name_ignoring_case(string value)
    {
        var validator = new IgnoreCaseModelValidator();
        var model = new Model { Value = value };

        var result = validator.TestValidate(model);

        result.ShouldHaveValidationErrorFor(x => x.Value);
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

    #region Error Code

    [Fact]
    public void should_have_correct_error_code()
    {
        var validator = new ModelValidator();
        var model = new Model { Value = "Yellow" };

        var result = validator.TestValidate(model);

        result.ShouldHaveValidationErrorFor(x => x.Value).WithErrorCode("g:invalid_enum_name");
    }

    #endregion
}
