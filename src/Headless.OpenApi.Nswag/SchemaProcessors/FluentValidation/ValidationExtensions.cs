// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using FluentValidation.Validators;
using Headless.OpenApi.Nswag.SchemaProcessors.FluentValidation.Models;
using Headless.Text;
using NJsonSchema;

namespace Headless.OpenApi.Nswag.SchemaProcessors.FluentValidation;

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

    /// <summary>
    /// Returns every property validator registered for <paramref name="name"/>, paired with whether it
    /// came from a collection rule (<c>RuleForEach</c>) and therefore constrains the collection's
    /// elements rather than the collection itself.
    /// </summary>
    public static IEnumerable<PropertyValidatorContext> GetValidatorsByPropertyNameIgnoreCase(
        this IValidator validator,
        string name
    )
    {
        return validator
            .GetValidationRulesByPropertyNameIgnoreCase(name)
            .SelectMany(ctx =>
                ctx.ValidationRule.Components.Select(c => new PropertyValidatorContext(
                    c.Validator,
                    ctx.ValidationRule.IsCollectionRule()
                ))
            );
    }

    /// <summary>
    /// Whether the rule was declared with <c>RuleForEach</c>. FluentValidation reports such a rule under
    /// the collection property's own name, so the caller must redirect its constraints to the item schema.
    /// </summary>
    public static bool IsCollectionRule(this IValidationRule rule)
    {
        return Array.Exists(
            rule.GetType().GetInterfaces(),
            i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICollectionRule<,>)
        );
    }

    public static IEnumerable<ValidationRuleContext> GetPropertyRules(this IEnumerable<IValidationRule> validationRules)
    {
        return validationRules.Select(r => new ValidationRuleContext(r));
    }

    public static bool HasNoCondition(this IValidationRule rule)
    {
        return rule is { HasCondition: false, HasAsyncCondition: false };
    }

    public static bool IsSupportedSwaggerNumericNumeric(this object value)
    {
        return value is int or long or float or double or decimal;
    }

    /// <summary>
    /// Writes a lower bound in the OpenAPI 3.0 form: <c>minimum</c> carries the value and
    /// <c>exclusiveMinimum</c> is the boolean modifier.
    /// </summary>
    /// <remarks>
    /// NJsonSchema exposes both dialects side by side: <see cref="JsonSchema.ExclusiveMinimum"/> is the
    /// JSON Schema draft-6 numeric form, while <see cref="JsonSchema.IsExclusiveMinimum"/> is the draft-4
    /// boolean that OpenAPI 3.0 adopted. NSwag emits <c>openapi: 3.0.0</c>, so only the boolean form is
    /// valid; writing the draft-6 numeric leaves the document without a <c>minimum</c> at all and 3.0
    /// tooling silently drops the bound.
    /// </remarks>
    public static void SetMinimum(this JsonSchema schema, decimal value, bool exclusive)
    {
        schema.Minimum = value;
        schema.IsExclusiveMinimum = exclusive;
        schema.ExclusiveMinimum = null;
    }

    /// <summary>
    /// Writes an upper bound in the OpenAPI 3.0 form: <c>maximum</c> carries the value and
    /// <c>exclusiveMaximum</c> is the boolean modifier. See <see cref="SetMinimum"/> for why the
    /// draft-6 numeric form is not used.
    /// </summary>
    public static void SetMaximum(this JsonSchema schema, decimal value, bool exclusive)
    {
        schema.Maximum = value;
        schema.IsExclusiveMaximum = exclusive;
        schema.ExclusiveMaximum = null;
    }
}
