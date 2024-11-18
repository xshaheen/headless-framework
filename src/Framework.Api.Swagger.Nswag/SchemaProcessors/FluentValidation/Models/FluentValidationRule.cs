// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation.Validators;
using Framework.BuildingBlocks.Helpers.Reflection;
using NJsonSchema;

namespace Framework.Api.Swagger.Nswag.SchemaProcessors.FluentValidation.Models;

public sealed class FluentValidationRule
{
    /// <summary>Rule name.</summary>
    public required string RuleName { get; init; }

    /// <summary>Predicate to match property validator.</summary>
    public required Func<IPropertyValidator, bool> Matches { get; init; }

    /// <summary>Modify Swagger schema action.</summary>
    public required Action<RuleContext> Apply { get; init; }

    #region Rules

    public static readonly FluentValidationRule RequiredRule =
        new()
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

    public static readonly FluentValidationRule NotNullRule =
        new()
        {
            RuleName = "NotNull",
            Matches = propertyValidator => propertyValidator is INotNullValidator,
            Apply = context =>
            {
                var propertySchema = context.PropertySchema;

                propertySchema.IsNullableRaw = false;

                if (propertySchema.Type.HasFlag(JsonObjectType.Null))
                {
                    propertySchema.Type &= ~JsonObjectType.Null; // Remove nullable
                }

                var oneOfsWithReference = propertySchema.OneOf.Where(x => x.Reference is not null).ToList();

                if (oneOfsWithReference.Count == 1)
                {
                    // Set the Reference directly instead and clear the OneOf collection
                    propertySchema.Reference = oneOfsWithReference.Single();
                    propertySchema.OneOf.Clear();
                }
            },
        };

    public static readonly FluentValidationRule NotEmptyRule =
        new()
        {
            RuleName = "NotEmpty",
            Matches = propertyValidator => propertyValidator is INotEmptyValidator,
            Apply = context =>
            {
                var propertySchema = context.PropertySchema;

                propertySchema.IsNullableRaw = false;

                if (propertySchema.Type.HasFlag(JsonObjectType.Null))
                {
                    propertySchema.Type &= ~JsonObjectType.Null; // Remove nullable
                }

                var oneOfsWithReference = propertySchema.OneOf.Where(x => x.Reference is not null).ToList();

                if (oneOfsWithReference.Count == 1)
                {
                    // Set the Reference directly instead and clear the OneOf collection
                    propertySchema.Reference = oneOfsWithReference.Single();
                    propertySchema.OneOf.Clear();
                }

                propertySchema.MinLength = 1;
            },
        };

    public static readonly FluentValidationRule LengthRule =
        new()
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

    public static readonly FluentValidationRule PatternRule =
        new()
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

    public static readonly FluentValidationRule ComparisonRule =
        new()
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

                var valueToCompare = Convert.ToDecimal(
                    comparisonValidator.ValueToCompare,
                    CultureInfo.InvariantCulture
                );
                var propertySchema = context.PropertySchema;

                switch (comparisonValidator.Comparison)
                {
                    case Comparison.GreaterThanOrEqual:
                        propertySchema.Minimum = valueToCompare;

                        break;
                    case Comparison.GreaterThan:
                        propertySchema.Minimum = valueToCompare;

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

    public static readonly FluentValidationRule BetweenRule =
        new()
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

    public static readonly FluentValidationRule EmailRule =
        new()
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
