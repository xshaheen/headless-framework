// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace FluentValidation;

/// <summary>
/// Maps FluentValidation built-in error codes to the framework's <c>g:snake_case</c> error code shape.
/// </summary>
[PublicAPI]
public static class FluentValidationErrorCodeMapper
{
    /// <summary>
    /// Maps a FluentValidation built-in error code (for example <c>"EmailValidator"</c>) to the
    /// equivalent Headless error code (for example <c>"g:invalid_email"</c>).
    /// </summary>
    /// <param name="errorCode">The FluentValidation error code to map, or <see langword="null"/>.</param>
    /// <returns>
    /// The mapped Headless error code when a known mapping exists; the original
    /// <paramref name="errorCode"/> when no mapping is defined; or <see langword="null"/> when
    /// <paramref name="errorCode"/> is <see langword="null"/>.
    /// </returns>
    [return: NotNullIfNotNull(nameof(errorCode))]
    public static string? MapToHeadlessErrorCode(string? errorCode)
    {
        return errorCode switch
        {
            null => null,
            "EmailValidator" => "g:invalid_email",
            "CreditCardValidator" => "g:invalid_credit_card",
            "GreaterThanOrEqualValidator" => "g:greater_than_or_equal",
            "GreaterThanValidator" => "g:greater_than",
            "LessThanOrEqualValidator" => "g:less_than_or_equal",
            "LessThanValidator" => "g:less_than",
            "MinimumLengthValidator" => "g:minimum_length",
            "MaximumLengthValidator" => "g:maximum_length",
            "LengthValidator" => "g:invalid_length",
            "ExactLengthValidator" => "g:exact_length",
            "InclusiveBetweenValidator" => "g:inclusive_between",
            "ExclusiveBetweenValidator" => "g:exclusive_between",
            "ScalePrecisionValidator" => "g:invalid_scale_precision",
            "EmptyValidator" => "g:must_be_empty",
            "NotEmptyValidator" => "g:must_be_not_empty",
            "NullValidator" => "g:must_be_null",
            "NotNullValidator" => "g:must_be_not_null",
            "EqualValidator" => "g:must_equal",
            "NotEqualValidator" => "g:must_not_equal",
            "EnumValidator" => "g:enum_out_of_range",
            "RegularExpressionValidator" => "g:invalid_pattern",
            "PredicateValidator" or "AsyncPredicateValidator" => "g:invalid_condition",
            _ => errorCode,
        };
    }
}
