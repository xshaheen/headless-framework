// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using System.Runtime.CompilerServices;
using FluentValidation;
using FluentValidation.Internal;
using FluentValidation.Validators;
using Headless.Text;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi;

namespace Headless.OpenApi;

/// <summary>Reads FluentValidation rules and writes their bounds onto OpenAPI schemas.</summary>
internal static class ValidationExtensions
{
    private static readonly ConditionalWeakTable<Type, MethodInfo?> _GetValidatorMethods = [];

    /// <summary>
    /// Resolves the <c>IValidator&lt;T&gt;</c> of <paramref name="type"/>, or <see langword="null"/> when none is
    /// registered, the type cannot close the generic, or resolving it throws, which is logged.
    /// </summary>
    public static IValidator? GetFluentValidator(this IServiceProvider services, Type type, ILogger logger)
    {
        if (type.IsGenericTypeDefinition || type.IsPointer || type.IsByRef)
        {
            return null;
        }

        try
        {
            return services.GetService(typeof(IValidator<>).MakeGenericType(type)) as IValidator;
        }
        catch (Exception e)
        {
            logger.LogGetValidatorFailed(e, type.Name);

            return null;
        }
    }

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
    /// Returns the validators an unconditional rule hands a value to through <c>Include()</c> (when
    /// <paramref name="propertyName"/> is <see langword="null"/>) or through <c>SetValidator()</c> on the named property.
    /// </summary>
    /// <remarks>
    /// <c>IValidatorDescriptor</c> exposes neither, so they are read from the rule list. Each is resolved without an
    /// instance, the way <c>new ValidationContext&lt;T&gt;(null)</c> would, so a validator chosen from the instance is
    /// skipped.
    /// </remarks>
    public static IEnumerable<IValidator> GetChildValidators(this IValidator validator, string? propertyName)
    {
        var adapters = (validator as IEnumerable<IValidationRule>)
            .EmptyIfNull()
            .Where(rule =>
                rule.HasNoCondition()
                && (
                    propertyName is null
                        ? rule is IIncludeRule
                        : rule is not IIncludeRule
                            && IgnoreCaseStringComparer.Instance.Equals(rule.PropertyName, propertyName)
                )
            )
            .SelectMany(rule => rule.Components)
            .Where(component => component is { HasCondition: false, HasAsyncCondition: false })
            .Select(component => component.Validator)
            .Where(adapter =>
                adapter.GetType().IsGenericType
                && adapter.GetType().GetGenericTypeDefinition() == typeof(ChildValidatorAdaptor<,>)
            );

        foreach (var adapter in adapters)
        {
            var getValidator = _GetValidatorMethods.GetValue(
                adapter.GetType(),
                static type => type.GetMethod(nameof(ChildValidatorAdaptor<,>.GetValidator))
            );

            if (getValidator is null)
            {
                continue;
            }

            var validationContext = Activator.CreateInstance(getValidator.GetParameters()[0].ParameterType, [null]);

            if (getValidator.Invoke(adapter, [validationContext, null]) is IValidator child)
            {
                yield return child;
            }
        }
    }

    /// <summary>Returns a validator and its unconditional Include chain, visiting each instance once.</summary>
    public static List<IValidator> GetValidatorsIncludingIncludes(this IValidator validator)
    {
        var validators = new List<IValidator> { validator };
        var seen = new HashSet<IValidator>(ReferenceEqualityComparer.Instance) { validator };

        // Breadth-first, and each validator once, so an include cycle ends.
        for (var i = 0; i < validators.Count; i++)
        {
            foreach (var included in validators[i].GetChildValidators(propertyName: null))
            {
                if (seen.Add(included))
                {
                    validators.Add(included);
                }
            }
        }

        return validators;
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
