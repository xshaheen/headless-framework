// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using FluentValidation.TestHelper;

namespace Tests;

public sealed class StringsFluentValidationExtensionsHexColorTests
{
    private sealed class Model
    {
        public string? Value { get; init; }
    }

    private sealed class ModelValidator : AbstractValidator<Model>
    {
        public ModelValidator()
        {
            RuleFor(x => x.Value).HexColor();
        }
    }

    #region Valid Hex Colors

    [Theory]
    [InlineData("#fff")]
    [InlineData("#FFF")]
    [InlineData("fff")]
    [InlineData("#1a2b3c")]
    [InlineData("1A2B3C")]
    [InlineData("abc")]
    [InlineData("ABCDEF")]
    public void should_not_have_error_when_string_is_a_hex_color(string value)
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
    [InlineData("#ff")]
    [InlineData("#ffff")]
    [InlineData("#1234567")]
    [InlineData("#gggggg")]
    [InlineData("#")]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("red")]
    public void should_have_error_when_string_is_not_a_hex_color(string value)
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
        var model = new Model { Value = "#gggggg" };

        var result = validator.TestValidate(model);

        result.ShouldHaveValidationErrorFor(x => x.Value).WithErrorCode("strings:invalid_hex_color");
    }

    #endregion
}
