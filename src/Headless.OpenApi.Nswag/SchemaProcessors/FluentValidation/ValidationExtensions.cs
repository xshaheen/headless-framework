// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using FluentValidation.Validators;
using Headless.Api.SchemaProcessors.FluentValidation.Models;
using Headless.Text;

namespace Headless.Api.SchemaProcessors.FluentValidation;

/// <summary>Extensions for some swagger-specific work.</summary>
internal static class ValidationExtensions
{
    public static IEnumerable<ValidationRuleContext> GetValidationRulesByPropertyNameIgnoreCase(
        this IValidator validator,
        string name
    )
    {
        return (validator as IEnumerable<IValidationRule>)
            .EmptyIfNull()
            .GetPropertyRules()
            .Where(ctx =>
                ctx.ValidationRule.HasNoCondition()
                && IgnoreCaseStringComparer.Instance.Equals(ctx.ValidationRule.PropertyName, name)
            );
    }

    public static IEnumerable<IPropertyValidator> GetValidatorsByPropertyNameIgnoreCase(
        this IValidator validator,
        string name
    )
    {
        return validator
            .GetValidationRulesByPropertyNameIgnoreCase(name)
            .SelectMany(ctx => ctx.ValidationRule.Components.Select(c => c.Validator));
    }

    public static IEnumerable<ValidationRuleContext> GetPropertyRules(this IEnumerable<IValidationRule> validationRules)
    {
        return validationRules.Select(r => new ValidationRuleContext(r));
    }

    public static bool HasNoCondition(this IValidationRule rule) =>
        rule is { HasCondition: false, HasAsyncCondition: false };

    public static IEnumerable<TValue> EmptyIfNull<TValue>(this IEnumerable<TValue>? collection) => collection ?? [];

    public static bool IsSupportedSwaggerNumericNumeric(this object value) =>
        value is int or long or float or double or decimal;
}
