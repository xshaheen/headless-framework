// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using FluentValidation.TestHelper;

namespace Tests;

public sealed class NetworkValidatorsIpv6Tests
{
    private sealed class Model
    {
        public string? Value { get; init; }
    }

    private sealed class ModelValidator : AbstractValidator<Model>
    {
        public ModelValidator()
        {
            RuleFor(x => x.Value).Ipv6();
        }
    }

    #region Valid IPv6

    [Theory]
    [InlineData("::1")]
    [InlineData("2001:db8::1")]
    [InlineData("fe80::1")]
    [InlineData("::")]
    [InlineData("2001:0db8:0000:0000:0000:ff00:0042:8329")]
    public void should_not_have_error_when_string_is_valid_ipv6(string value)
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
    [InlineData("192.168.0.1")]
    [InlineData("abc")]
    [InlineData("12345::")]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("1.2.3.4")]
    public void should_have_error_when_string_is_not_valid_ipv6(string value)
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
        var model = new Model { Value = "192.168.0.1" };

        var result = validator.TestValidate(model);

        result.ShouldHaveValidationErrorFor(x => x.Value).WithErrorCode("g:invalid_ipv6");
    }

    #endregion
}
