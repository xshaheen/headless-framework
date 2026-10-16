// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using FluentValidation.Validators;
using Headless.Text;
using Microsoft.OpenApi;

namespace Headless.OpenApi;

/// <summary>Reads FluentValidation rules and writes their bounds onto OpenAPI schemas.</summary>
internal static class ValidationExtensions
{
    /// <summary>
    /// Returns every unconditional property validator registered for <paramref name="name"/>, paired with whether it
    /// came from a collection rule (<c>RuleForEach</c>) and so constrains the collection's elements.
    /// </summary>
    public static IEnumerable<(
        IPropertyValidator Validator,
        bool IsCollectionRule
    )> GetValidatorsByPropertyNameIgnoreCase(this IValidator validator, string name)
    {
        return (validator as IEnumerable<IValidationRule>)
            .EmptyIfNull()
            .Where(rule => rule.HasNoCondition() && IgnoreCaseStringComparer.Instance.Equals(rule.PropertyName, name))
            .SelectMany(rule =>
                rule.Components
                    // A When() clause conditions the components it follows, not the rule.
                    .Where(component => component is { HasCondition: false, HasAsyncCondition: false })
                    .Select(component => (component.Validator, rule.IsCollectionRule()))
            );
    }

    /// <summary>
    /// Whether the rule was declared with <c>RuleForEach</c>. FluentValidation reports such a rule under the
    /// collection property's own name, so the caller must redirect its constraints to the item schema.
    /// </summary>
    public static bool IsCollectionRule(this IValidationRule rule)
    {
        return Array.Exists(
            rule.GetType().GetInterfaces(),
            i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICollectionRule<,>)
        );
    }

    public static bool HasNoCondition(this IValidationRule rule)
    {
        return rule is { HasCondition: false, HasAsyncCondition: false };
    }

    public static bool IsSupportedOpenApiNumeric(this object value)
    {
        return value is int or long or float or double or decimal;
    }

    /// <summary>
    /// Writes a lower bound. OpenAPI 3.1 follows JSON Schema 2020-12, where <c>exclusiveMinimum</c> is itself the
    /// numeric bound, so an exclusive bound clears <c>minimum</c> rather than pairing with it.
    /// </summary>
    public static void SetMinimum(this OpenApiSchema schema, decimal value, bool exclusive)
    {
        var bound = value.ToString(CultureInfo.InvariantCulture);

        if (exclusive)
        {
            schema.Minimum = null;
            schema.ExclusiveMinimum = bound;
        }
        else
        {
            schema.ExclusiveMinimum = null;
            schema.Minimum = bound;
        }
    }

    /// <summary>Writes an upper bound; see <see cref="SetMinimum"/> for the exclusive form.</summary>
    public static void SetMaximum(this OpenApiSchema schema, decimal value, bool exclusive)
    {
        var bound = value.ToString(CultureInfo.InvariantCulture);

        if (exclusive)
        {
            schema.Maximum = null;
            schema.ExclusiveMaximum = bound;
        }
        else
        {
            schema.ExclusiveMaximum = null;
            schema.Maximum = bound;
        }
    }
}
