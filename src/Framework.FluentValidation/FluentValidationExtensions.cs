// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation.Results;
using Framework.FluentValidation;
using Framework.Primitives;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
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
                failure => new ErrorDescriptor(
                    code: string.IsNullOrEmpty(failure.ErrorCode)
                        ? failure.ErrorMessage
                        : FluentValidationErrorCodeMapper.MapToApplicationErrorCode(failure.ErrorCode),
                    description: failure.ErrorMessage,
                    paramsDictionary: failure.FormattedMessagePlaceholderValues
                ),
                StringComparer.Ordinal
            )
            .ToDictionary(
                failureGroup => failureGroup.Key.NormalizePropertyPath(),
                failureGroup => (List<ErrorDescriptor>)[.. failureGroup],
                StringComparer.Ordinal
            );
    }
}
