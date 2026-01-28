// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation.Results;
using Headless.Primitives;

namespace FluentValidation;

[PublicAPI]
public static class FluentValidationExtensions
{
    public static IRuleBuilderOptions<T, TProperty> WithErrorDescriptor<T, TProperty>(
        this IRuleBuilderOptions<T, TProperty> rule,
        ErrorDescriptor errorDescriptor
    )
    {
        var description = string.IsNullOrWhiteSpace(errorDescriptor.Description)
            ? errorDescriptor.Code
            : errorDescriptor.Description;

        return rule.WithErrorCode(errorDescriptor.Code).WithMessage(description);
    }

    public static Severity ToSeverity(this ValidationSeverity severity)
    {
        return severity switch
        {
            ValidationSeverity.Information => Severity.Info,
            ValidationSeverity.Warning => Severity.Warning,
            ValidationSeverity.Error => Severity.Error,
            > ValidationSeverity.Error => Severity.Error,
            < ValidationSeverity.Information => Severity.Info,
        };
    }

    public static Dictionary<string, List<ErrorDescriptor>> ToErrorDescriptors(
        this IEnumerable<ValidationFailure> failures
    )
    {
        return failures
            .GroupBy(
                failure => failure.PropertyName,
                failure =>
                {
                    var paramsDictionary = failure.FormattedMessagePlaceholderValues;
                    string errorCode;

                    if (string.IsNullOrWhiteSpace(failure.ErrorCode))
                    {
                        errorCode = failure.ErrorMessage;
                    }
                    else
                    {
                        errorCode = FluentValidationErrorCodeMapper.MapToHeadlessErrorCode(failure.ErrorCode);

                        // Normalize the fluent validation property path
                        if (
                            // Is fluent validation error code
                            !string.Equals(errorCode, failure.ErrorCode, StringComparison.Ordinal)
                            && paramsDictionary.Count > 0
                            && paramsDictionary.TryGetValue("PropertyPath", out var value)
                            && value is string propertyPath
                        )
                        {
                            paramsDictionary["PropertyPath"] = propertyPath.CamelizePropertyPath();
                        }
                    }

                    return new ErrorDescriptor(
                        code: errorCode,
                        description: failure.ErrorMessage,
                        paramsDictionary: paramsDictionary
                    );
                },
                StringComparer.Ordinal
            )
            .ToDictionary(
                failureGroup => failureGroup.Key.CamelizePropertyPath(),
                failureGroup => failureGroup.ToList(),
                StringComparer.Ordinal
            );
    }
}
