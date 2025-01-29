// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;

namespace Framework.FluentValidation;

[PublicAPI]
public static class FluentValidationErrorCodeMapper
{
    [return: NotNullIfNotNull(nameof(errorCode))]
    public static string? MapToApplicationErrorCode(string? errorCode)
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
