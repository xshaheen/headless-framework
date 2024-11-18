// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using FluentValidation.Validators;
using Framework.Api.Swagger.Nswag.SchemaProcessors.FluentValidation.Models;
using Framework.BuildingBlocks.Helpers.System;

namespace Framework.Api.Swagger.Nswag.SchemaProcessors.FluentValidation;

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
                HasNoCondition(ctx.ValidationRule)
                && IgnoreAllStringComparer.Instance.Equals(ctx.ValidationRule.PropertyName, name)
            );
    }

    public static IEnumerable<IPropertyValidator> GetValidatorsByPropertyNameIgnoreCase(
        this IValidator validator,
        string name
    )
    {
        return GetValidationRulesByPropertyNameIgnoreCase(validator, name)
            .SelectMany(ctx => ctx.ValidationRule.Components.Select(c => c.Validator));
    }

    /// <summary>Returns all IValidationRules that are PropertyRule. If rule is CollectionPropertyRule then isCollectionRule set to true.</summary>
    public static IEnumerable<ValidationRuleContext> GetPropertyRules(this IEnumerable<IValidationRule> validationRules)
    {
        return from validationRule in validationRules
            let isCollectionRule = validationRule.GetType() == typeof(ICollectionRule<,>)
            select new ValidationRuleContext(validationRule, isCollectionRule);
    }

    public static bool HasNoCondition(this IValidationRule rule) =>
        rule is { HasCondition: false, HasAsyncCondition: false };

    public static IEnumerable<TValue> EmptyIfNull<TValue>(this IEnumerable<TValue>? collection) =>
        collection ?? Array.Empty<TValue>();

    public static bool IsSupportedSwaggerNumericNumeric(this object value) =>
        value is int or long or float or double or decimal;
}
