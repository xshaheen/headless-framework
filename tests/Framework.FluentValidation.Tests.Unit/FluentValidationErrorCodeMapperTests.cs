// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Tests;

public sealed class FluentValidationErrorCodeMapperTests
{
    #region Built-in Validators Mapping

    [Theory]
    [InlineData("EmailValidator", "g:invalid_email")]
    [InlineData("CreditCardValidator", "g:invalid_credit_card")]
    [InlineData("GreaterThanOrEqualValidator", "g:greater_than_or_equal")]
    [InlineData("GreaterThanValidator", "g:greater_than")]
    [InlineData("LessThanOrEqualValidator", "g:less_than_or_equal")]
    [InlineData("LessThanValidator", "g:less_than")]
    [InlineData("MinimumLengthValidator", "g:minimum_length")]
    [InlineData("MaximumLengthValidator", "g:maximum_length")]
    [InlineData("LengthValidator", "g:invalid_length")]
    [InlineData("ExactLengthValidator", "g:exact_length")]
    [InlineData("InclusiveBetweenValidator", "g:inclusive_between")]
    [InlineData("ExclusiveBetweenValidator", "g:exclusive_between")]
    [InlineData("ScalePrecisionValidator", "g:invalid_scale_precision")]
    [InlineData("EmptyValidator", "g:must_be_empty")]
    [InlineData("NotEmptyValidator", "g:must_be_not_empty")]
    [InlineData("NullValidator", "g:must_be_null")]
    [InlineData("NotNullValidator", "g:must_be_not_null")]
    [InlineData("EqualValidator", "g:must_equal")]
    [InlineData("NotEqualValidator", "g:must_not_equal")]
    [InlineData("EnumValidator", "g:enum_out_of_range")]
    [InlineData("RegularExpressionValidator", "g:invalid_pattern")]
    [InlineData("PredicateValidator", "g:invalid_condition")]
    [InlineData("AsyncPredicateValidator", "g:invalid_condition")]
    public void should_map_built_in_validators_to_headless_codes(string fluentCode, string expectedCode)
    {
        var result = FluentValidationErrorCodeMapper.MapToHeadlessErrorCode(fluentCode);

        result.Should().Be(expectedCode);
    }

    #endregion

    #region Null and Unknown Handling

    [Fact]
    public void should_return_null_when_error_code_is_null()
    {
        var result = FluentValidationErrorCodeMapper.MapToHeadlessErrorCode(null);

        result.Should().BeNull();
    }

    [Theory]
    [InlineData("CustomValidator")]
    [InlineData("MyCustomValidator")]
    [InlineData("UnknownValidator")]
    [InlineData("custom:my_error")]
    [InlineData("")]
    public void should_return_original_code_when_not_mapped(string unknownCode)
    {
        var result = FluentValidationErrorCodeMapper.MapToHeadlessErrorCode(unknownCode);

        result.Should().Be(unknownCode);
    }

    #endregion

    #region Case Sensitivity

    [Theory]
    [InlineData("emailvalidator")]
    [InlineData("EMAILVALIDATOR")]
    [InlineData("emailValidator")]
    public void should_not_map_when_case_differs(string wrongCaseCode)
    {
        // The mapper is case-sensitive, so wrong case returns original
        var result = FluentValidationErrorCodeMapper.MapToHeadlessErrorCode(wrongCaseCode);

        result.Should().Be(wrongCaseCode);
    }

    #endregion
}
