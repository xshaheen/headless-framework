// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation.Validators;
using Headless.Reflection;
using NJsonSchema;

namespace Headless.OpenApi.Nswag.SchemaProcessors.FluentValidation.Models;

/// <summary>
/// Describes a single mapping from a FluentValidation property-validator type to an OpenAPI schema mutation.
/// </summary>
/// <remarks>
/// The built-in set of rules is exposed as <see cref="DefaultRules"/>. To extend or replace individual
/// rules, pass a collection of <see cref="FluentValidationRule"/> instances to
/// <see cref="FluentValidationSchemaProcessor"/>; rules with the same <see cref="RuleName"/> replace the
/// corresponding default.
/// </remarks>
public sealed class FluentValidationRule
{
    /// <summary>
    /// Unique name used to identify and replace this rule in the merged rule set.
    /// </summary>
    public required string RuleName { get; init; }

    /// <summary>
    /// Predicate that returns <see langword="true"/> when this rule should be applied to the given
    /// FluentValidation property validator.
    /// </summary>
    public required Func<IPropertyValidator, bool> Matches { get; init; }

    /// <summary>
    /// Action that mutates the OpenAPI property schema to reflect the constraints expressed by the
    /// matched FluentValidation validator.
    /// </summary>
    public required Action<RuleContext> Apply { get; init; }

    #region Rules

    /// <summary>
    /// Adds the property to the schema's <c>required</c> list when a <c>NotNull</c> or <c>NotEmpty</c>
    /// validator is present.
    /// </summary>
    public static readonly FluentValidationRule RequiredRule = new()
    {
        RuleName = "Required",
        Matches = propertyValidator => propertyValidator is INotNullValidator or INotEmptyValidator,
        Apply = context =>
        {
            var schema = context.ProcessorContext.Schema;

            if (!schema.IsObject)
            {
                return;
            }

            if (!schema.RequiredProperties.Contains(context.PropertyKey, StringComparer.Ordinal))
            {
                schema.RequiredProperties.Add(context.PropertyKey);
            }
        },
    };

    /// <summary>
    /// Removes nullability from the property schema when a <c>NotNull</c> validator is present.
    /// </summary>
    public static readonly FluentValidationRule NotNullRule = new()
    {
        RuleName = "NotNull",
        Matches = propertyValidator => propertyValidator is INotNullValidator,
        Apply = context => _RemoveNullability(context.PropertySchema),
    };

    /// <summary>
    /// Removes nullability and sets <c>minLength = 1</c> when a <c>NotEmpty</c> validator is present.
    /// </summary>
    public static readonly FluentValidationRule NotEmptyRule = new()
    {
        RuleName = "NotEmpty",
        Matches = propertyValidator => propertyValidator is INotEmptyValidator,
        Apply = context =>
        {
            _RemoveNullability(context.PropertySchema);
            context.PropertySchema.MinLength = 1;
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
            var lengthValidator = (ILengthValidator)context.PropertyValidator;
            var propertySchema = context.PropertySchema;

            if (lengthValidator.Max > 0)
            {
                propertySchema.MaxLength = lengthValidator.Max;
            }

            if (
                lengthValidator.GetType() == typeof(MinimumLengthValidator<>)
                || lengthValidator.GetType() == typeof(ExactLengthValidator<>)
                || propertySchema.MinLength is null
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
            var regularExpressionValidator = (IRegularExpressionValidator)context.PropertyValidator;

            var propertySchema = context.PropertySchema;
            propertySchema.Pattern = regularExpressionValidator.Expression;
        },
    };

    /// <summary>
    /// Sets <c>minimum</c>, <c>exclusiveMinimum</c>, <c>maximum</c>, or <c>exclusiveMaximum</c> from
    /// a comparison validator (<c>GreaterThan</c>, <c>GreaterThanOrEqualTo</c>, <c>LessThan</c>,
    /// <c>LessThanOrEqualTo</c>). Only applies when the comparison value is a supported numeric type.
    /// </summary>
    public static readonly FluentValidationRule ComparisonRule = new()
    {
        RuleName = "Comparison",
        Matches = propertyValidator => propertyValidator is IComparisonValidator,
        Apply = context =>
        {
            var comparisonValidator = (IComparisonValidator)context.PropertyValidator;

            if (!comparisonValidator.ValueToCompare.IsSupportedSwaggerNumericNumeric())
            {
                return;
            }

            var valueToCompare = Convert.ToDecimal(comparisonValidator.ValueToCompare, CultureInfo.InvariantCulture);
            var propertySchema = context.PropertySchema;

            switch (comparisonValidator.Comparison)
            {
                case Comparison.GreaterThanOrEqual:
                    propertySchema.Minimum = valueToCompare;

                    break;
                case Comparison.GreaterThan:
                    propertySchema.ExclusiveMinimum = valueToCompare;

                    break;
                case Comparison.LessThanOrEqual:
                    propertySchema.Maximum = valueToCompare;

                    break;
                case Comparison.LessThan:
                    propertySchema.Maximum = valueToCompare;
                    propertySchema.IsExclusiveMaximum = true;

                    break;
            }
        },
    };

    /// <summary>
    /// Sets <c>minimum</c>/<c>exclusiveMinimum</c> and <c>maximum</c>/<c>exclusiveMaximum</c> from an
    /// <c>InclusiveBetween</c> or <c>ExclusiveBetween</c> validator.
    /// </summary>
    public static readonly FluentValidationRule BetweenRule = new()
    {
        RuleName = "Between",
        Matches = propertyValidator => propertyValidator is IBetweenValidator,
        Apply = context =>
        {
            var betweenValidator = (IBetweenValidator)context.PropertyValidator;
            var propertySchema = context.PropertySchema;

            if (betweenValidator.From.IsSupportedSwaggerNumericNumeric())
            {
                if (betweenValidator.GetType().IsSubClassOfGeneric(typeof(ExclusiveBetweenValidator<,>)))
                {
                    propertySchema.ExclusiveMinimum = Convert.ToDecimal(
                        betweenValidator.From,
                        CultureInfo.InvariantCulture
                    );
                }
                else
                {
                    propertySchema.Minimum = Convert.ToDecimal(betweenValidator.From, CultureInfo.InvariantCulture);
                }
            }

            if (betweenValidator.To.IsSupportedSwaggerNumericNumeric())
            {
                if (betweenValidator.GetType().IsSubClassOfGeneric(typeof(ExclusiveBetweenValidator<,>)))
                {
                    propertySchema.ExclusiveMaximum = Convert.ToDecimal(
                        betweenValidator.To,
                        CultureInfo.InvariantCulture
                    );
                }
                else
                {
                    propertySchema.Maximum = Convert.ToDecimal(betweenValidator.To, CultureInfo.InvariantCulture);
                }
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
            var propertySchema = context.PropertySchema;
            propertySchema.Pattern = "^[^@]+@[^@]+$"; // [^@] All chars except @
        },
    };

    /// <summary>
    /// The complete set of built-in rules applied by <see cref="FluentValidationSchemaProcessor"/> when no
    /// custom rule collection is provided.
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

    private static void _RemoveNullability(JsonSchema propertySchema)
    {
        propertySchema.IsNullableRaw = false;

        if (propertySchema.Type.HasFlag(JsonObjectType.Null))
        {
            propertySchema.Type &= ~JsonObjectType.Null;
        }

        var oneOfsWithReference = propertySchema.OneOf.Where(x => x.Reference is not null).ToList();

        if (oneOfsWithReference.Count == 1)
        {
            propertySchema.Reference = oneOfsWithReference.Single();
            propertySchema.OneOf.Clear();
        }
    }
}
