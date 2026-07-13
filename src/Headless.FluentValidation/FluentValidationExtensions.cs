// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation.Results;
using Headless.Primitives;

namespace FluentValidation;

/// <summary>FluentValidation integration helpers for the Headless error descriptor model.</summary>
[PublicAPI]
public static class FluentValidationExtensions
{
    /// <summary>
    /// Applies an <see cref="ErrorDescriptor"/> to the rule, setting the error code, error message,
    /// and severity in a single call.
    /// </summary>
    /// <typeparam name="T">The type being validated.</typeparam>
    /// <typeparam name="TProperty">The property type being validated.</typeparam>
    /// <param name="rule">The rule builder options to configure.</param>
    /// <param name="errorDescriptor">The descriptor whose code and description are applied to the rule.</param>
    /// <returns>The same rule builder options for chaining.</returns>
    public static IRuleBuilderOptions<T, TProperty> WithErrorDescriptor<T, TProperty>(
        this IRuleBuilderOptions<T, TProperty> rule,
        ErrorDescriptor errorDescriptor
    )
    {
        var description = string.IsNullOrWhiteSpace(errorDescriptor.Description)
            ? errorDescriptor.Code
            : errorDescriptor.Description;

        return rule.WithErrorCode(errorDescriptor.Code)
            .WithMessage(description)
            .WithSeverity(errorDescriptor.Severity.ToSeverity());
    }

    /// <summary>Converts a FluentValidation <see cref="ValidationSeverity"/> to the Headless <see cref="Severity"/> equivalent.</summary>
    /// <param name="severity">The FluentValidation severity to convert.</param>
    /// <returns>The corresponding <see cref="Severity"/> value.</returns>
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

    /// <summary>
    /// Groups <see cref="ValidationFailure"/> instances by property path and converts each into an
    /// <see cref="ErrorDescriptor"/>, mapping FluentValidation built-in codes via
    /// <see cref="FluentValidationErrorCodeMapper.MapToHeadlessErrorCode"/>.
    /// </summary>
    /// <param name="failures">The validation failures to convert.</param>
    /// <returns>
    /// A dictionary keyed by camelCase property path, with each value being the list of
    /// <see cref="ErrorDescriptor"/> instances for that property.
    /// </returns>
    public static IReadOnlyDictionary<string, IReadOnlyList<ErrorDescriptor>> ToErrorDescriptors(
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
                        paramsDictionary: paramsDictionary,
                        severity: _ToValidationSeverity(failure.Severity)
                    );
                },
                StringComparer.Ordinal
            )
            .ToDictionary(
                failureGroup => failureGroup.Key.CamelizePropertyPath(),
                IReadOnlyList<ErrorDescriptor> (failureGroup) => failureGroup.ToList(),
                StringComparer.Ordinal
            );
    }

    private static ValidationSeverity _ToValidationSeverity(Severity severity)
    {
        return severity switch
        {
            Severity.Info => ValidationSeverity.Information,
            Severity.Warning => ValidationSeverity.Warning,
            _ => ValidationSeverity.Error,
        };
    }
}
