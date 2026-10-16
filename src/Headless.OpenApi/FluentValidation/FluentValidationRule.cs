// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation.Validators;
using Headless.Reflection;
using Microsoft.OpenApi;

namespace Headless.OpenApi;

/// <summary>
/// Describes a single mapping from a FluentValidation property-validator type to an OpenAPI schema mutation.
/// </summary>
/// <remarks>
/// The built-in set of rules is exposed as <see cref="DefaultRules"/>. To extend or replace individual rules, add
/// them to <see cref="HeadlessOpenApiOptions.FluentValidationRules"/>; a rule with the same <see cref="RuleName"/>
/// replaces the corresponding default.
/// </remarks>
public sealed class FluentValidationRule
{
    /// <summary>Unique name used to identify and replace this rule in the merged rule set.</summary>
    public required string RuleName { get; init; }

    /// <summary>
    /// Predicate that returns <see langword="true"/> when this rule should be applied to the given FluentValidation
    /// property validator.
    /// </summary>
    public required Func<IPropertyValidator, bool> Matches { get; init; }

    /// <summary>
    /// Action that mutates the OpenAPI schema to reflect the constraints expressed by the matched FluentValidation
    /// validator.
    /// </summary>
    public required Action<RuleContext> Apply { get; init; }

    #region Rules

    /// <summary>
    /// Adds the property to the object schema's <c>"required"</c> set when a <c>NotNull</c> or <c>NotEmpty</c>
    /// validator is present.
    /// </summary>
    public static readonly FluentValidationRule RequiredRule = new()
    {
        RuleName = "Required",
        Matches = propertyValidator => propertyValidator is INotNullValidator or INotEmptyValidator,
        Apply = context =>
        {
            if (!context.IsObjectSchema)
            {
                return;
            }

            (context.Schema.Required ??= new HashSet<string>(StringComparer.Ordinal)).Add(context.PropertyKey);
        },
    };

    /// <summary>Removes nullability from the property schema when a <c>NotNull</c> validator is present.</summary>
    public static readonly FluentValidationRule NotNullRule = new()
    {
        RuleName = "NotNull",
        Matches = propertyValidator => propertyValidator is INotNullValidator,
        Apply = context => context.RemovePropertyNullability(),
    };

    /// <summary>
    /// Removes nullability and sets <c>minLength = 1</c> (<c>minItems = 1</c> on an array) when a <c>NotEmpty</c>
    /// validator is present.
    /// </summary>
    public static readonly FluentValidationRule NotEmptyRule = new()
    {
        RuleName = "NotEmpty",
        Matches = propertyValidator => propertyValidator is INotEmptyValidator,
        Apply = context =>
        {
            context.RemovePropertyNullability();

            if (context.PropertySchema is not { } propertySchema)
            {
                return;
            }

            // JSON Schema bounds a string with minLength and an array with minItems.
            if (propertySchema.Type?.HasFlag(JsonSchemaType.Array) == true)
            {
                propertySchema.MinItems = 1;
            }
            else
            {
                propertySchema.MinLength = 1;
            }
        },
    };

    /// <summary>
    /// Sets <c>maxLength</c> and/or <c>minLength</c> from a <c>Length</c>, <c>MinimumLength</c>, or
    /// <c>ExactLength</c> validator.
    /// </summary>
    public static readonly FluentValidationRule LengthRule = new()
    {
        RuleName = "Length",
        Matches = propertyValidator => propertyValidator is ILengthValidator,
        Apply = context =>
        {
            if (context.PropertySchema is not { } propertySchema)
            {
                return;
            }

            var lengthValidator = (ILengthValidator)context.PropertyValidator;

            if (lengthValidator.Max > 0)
            {
                propertySchema.MaxLength = lengthValidator.Max;
            }

            // A maximum-only rule reports a minimum of 0, which constrains nothing.
            if (
                lengthValidator.GetType().IsSubClassOfGeneric(typeof(MinimumLengthValidator<>))
                || lengthValidator.GetType().IsSubClassOfGeneric(typeof(ExactLengthValidator<>))
                || (propertySchema.MinLength is null && lengthValidator.Min > 0)
            )
            {
                propertySchema.MinLength = lengthValidator.Min;
            }
        },
    };

    /// <summary>Sets <c>pattern</c> from a <c>Matches</c> (regular expression) validator.</summary>
    public static readonly FluentValidationRule PatternRule = new()
    {
        RuleName = "Pattern",
        Matches = propertyValidator => propertyValidator is IRegularExpressionValidator,
        Apply = context =>
        {
            if (context.PropertySchema is { } propertySchema)
            {
                propertySchema.Pattern = ((IRegularExpressionValidator)context.PropertyValidator).Expression;
            }
        },
    };

    /// <summary>
    /// Sets <c>minimum</c>/<c>maximum</c>, or <c>exclusiveMinimum</c>/<c>exclusiveMaximum</c>, from a comparison
    /// validator (<c>GreaterThan</c>, <c>GreaterThanOrEqualTo</c>, <c>LessThan</c>, <c>LessThanOrEqualTo</c>). Only
    /// applies when the comparison value is a supported numeric type.
    /// </summary>
    public static readonly FluentValidationRule ComparisonRule = new()
    {
        RuleName = "Comparison",
        Matches = propertyValidator => propertyValidator is IComparisonValidator,
        Apply = context =>
        {
            var comparisonValidator = (IComparisonValidator)context.PropertyValidator;

            if (
                context.PropertySchema is not { } propertySchema
                || !comparisonValidator.ValueToCompare.IsSupportedOpenApiNumeric()
            )
            {
                return;
            }

            var valueToCompare = Convert.ToDecimal(comparisonValidator.ValueToCompare, CultureInfo.InvariantCulture);

            switch (comparisonValidator.Comparison)
            {
                case Comparison.GreaterThanOrEqual:
                    propertySchema.SetMinimum(valueToCompare, exclusive: false);

                    break;
                case Comparison.GreaterThan:
                    propertySchema.SetMinimum(valueToCompare, exclusive: true);

                    break;
                case Comparison.LessThanOrEqual:
                    propertySchema.SetMaximum(valueToCompare, exclusive: false);

                    break;
                case Comparison.LessThan:
                    propertySchema.SetMaximum(valueToCompare, exclusive: true);

                    break;
            }
        },
    };

    /// <summary>
    /// Sets <c>minimum</c> and <c>maximum</c> from an <c>InclusiveBetween</c> validator, or
    /// <c>exclusiveMinimum</c> and <c>exclusiveMaximum</c> from an <c>ExclusiveBetween</c> validator.
    /// </summary>
    public static readonly FluentValidationRule BetweenRule = new()
    {
        RuleName = "Between",
        Matches = propertyValidator => propertyValidator is IBetweenValidator,
        Apply = context =>
        {
            if (context.PropertySchema is not { } propertySchema)
            {
                return;
            }

            var betweenValidator = (IBetweenValidator)context.PropertyValidator;
            var exclusive = betweenValidator.GetType().IsSubClassOfGeneric(typeof(ExclusiveBetweenValidator<,>));

            if (betweenValidator.From.IsSupportedOpenApiNumeric())
            {
                propertySchema.SetMinimum(
                    Convert.ToDecimal(betweenValidator.From, CultureInfo.InvariantCulture),
                    exclusive
                );
            }

            if (betweenValidator.To.IsSupportedOpenApiNumeric())
            {
                propertySchema.SetMaximum(
                    Convert.ToDecimal(betweenValidator.To, CultureInfo.InvariantCulture),
                    exclusive
                );
            }
        },
    };

    /// <summary>
    /// Sets <c>pattern</c> to <c>^[^@]+@[^@]+$</c> from an ASP.NET Core-compatible email validator.
    /// </summary>
    public static readonly FluentValidationRule EmailRule = new()
    {
        RuleName = "AspNetCoreCompatibleEmail",
        Matches = propertyValidator =>
            propertyValidator.GetType().IsSubClassOfGeneric(typeof(AspNetCoreCompatibleEmailValidator<>)),
        Apply = context =>
        {
            if (context.PropertySchema is { } propertySchema)
            {
                propertySchema.Pattern = "^[^@]+@[^@]+$"; // [^@] All chars except @
            }
        },
    };

    /// <summary>
    /// The complete set of built-in rules applied when <see cref="HeadlessOpenApiOptions.FluentValidationRules"/>
    /// adds or replaces none.
    /// </summary>
    public static readonly FluentValidationRule[] DefaultRules =
    [
        RequiredRule,
        NotNullRule,
        NotEmptyRule,
        LengthRule,
        PatternRule,
        ComparisonRule,
        BetweenRule,
        EmailRule,
    ];

    #endregion
}
