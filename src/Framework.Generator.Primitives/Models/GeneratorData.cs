// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Immutable;
using Framework.Generator.Primitives.Extensions;

namespace Framework.Generator.Primitives.Models;

/// <summary>Represents data used by the code generator for generating Primitive types.</summary>
internal sealed class GeneratorData
{
    /// <summary>The field name for the underlying value.</summary>
    public required string FieldName { get; init; }

    /// <summary>Whether to generate GetHashCode method.</summary>
    public required bool GenerateHashCode { get; init; }

    /// <summary>The friendly name of the underlying primitive type (e.g., "int", "string").</summary>
    public required string PrimitiveTypeFriendlyName { get; init; }

    /// <summary>The underlying primitive type enum.</summary>
    public required PrimitiveUnderlyingType UnderlyingType { get; init; }

    /// <summary>Whether the underlying primitive type is a value type.</summary>
    public required bool PrimitiveTypeIsValueType { get; init; }

    /// <summary>The namespace of the underlying primitive type.</summary>
    public required string PrimitiveTypeNamespace { get; init; }

    /// <summary>Parent primitive info for nested primitives.</summary>
    public required ImmutableArray<ParentPrimitiveInfo> ParentPrimitives { get; init; }

    /// <summary>The namespace containing the type.</summary>
    public required string Namespace { get; init; }

    /// <summary>The class name.</summary>
    public required string ClassName { get; init; }

    /// <summary>Whether the type is a value type (struct).</summary>
    public required bool IsValueType { get; init; }

    /// <summary>The modifiers of the type (e.g., "public partial").</summary>
    public required string Modifiers { get; init; }

    /// <summary>Whether to generate subtraction operators.</summary>
    public required bool GenerateSubtractionOperators { get; init; }

    /// <summary>Whether to generate addition operators.</summary>
    public required bool GenerateAdditionOperators { get; init; }

    /// <summary>Whether to generate division operators.</summary>
    public required bool GenerateDivisionOperators { get; init; }

    /// <summary>Whether to generate multiplication operators.</summary>
    public required bool GenerateMultiplyOperators { get; init; }

    /// <summary>Whether to generate modulus operator.</summary>
    public required bool GenerateModulusOperator { get; init; }

    /// <summary>Whether to generate comparison methods.</summary>
    public required bool GenerateComparison { get; init; }

    /// <summary>Whether to generate IParsable methods.</summary>
    public required bool GenerateParsable { get; init; }

    /// <summary>Whether to generate implicit operators.</summary>
    public required bool GenerateImplicitOperators { get; init; }

    /// <summary>The serialization format (if applicable).</summary>
    public required string? SerializationFormat { get; init; }

    /// <summary>Whether to generate ISpanFormattable methods.</summary>
    public required bool GenerateSpanFormattable { get; init; }

    /// <summary>Whether to generate IConvertible methods.</summary>
    public required bool GenerateConvertibles { get; init; }

    /// <summary>Whether to generate IUtf8SpanFormattable methods.</summary>
    public required bool GenerateUtf8SpanFormattable { get; init; }

    /// <summary>Whether to generate IXmlSerializable methods.</summary>
    public required bool GenerateXmlSerializableMethods { get; init; }

    /// <summary>Whether there's an explicit ToString method.</summary>
    public required bool HasExplicitToStringMethod { get; init; }

    /// <summary>String length validation info.</summary>
    public (int minLength, int maxLength)? StringLengthAttributeValidation { get; set; }

    /// <summary>XML documentation comment for Swagger.</summary>
    public required string? XmlDocumentation { get; init; }

    /// <summary>Location file path for diagnostics.</summary>
    public required string LocationFilePath { get; init; }

    /// <summary>Location line start for diagnostics.</summary>
    public required int LocationLineStart { get; init; }

    public bool HasMathOperators()
    {
        return GenerateAdditionOperators
            || GenerateSubtractionOperators
            || GenerateMultiplyOperators
            || GenerateDivisionOperators
            || GenerateModulusOperator;
    }

    public bool IsPrimitiveUnderlyingTypString()
    {
        return ParentPrimitives.Length == 0 && UnderlyingType is PrimitiveUnderlyingType.String;
    }

    public bool IsPrimitiveUnderlyingTypeChar()
    {
        return ParentPrimitives.Length == 0 && UnderlyingType is PrimitiveUnderlyingType.Char;
    }

    public bool IsPrimitiveUnderlyingTypeBool()
    {
        return ParentPrimitives.Length == 0 && UnderlyingType is PrimitiveUnderlyingType.Boolean;
    }

    /// <summary>Creates GeneratorData from PrimitiveTypeInfo and global options.</summary>
    public static GeneratorData FromTypeInfo(PrimitiveTypeInfo info, PrimitiveGlobalOptions globalOptions)
    {
        var isNumeric = info.UnderlyingType.IsNumeric();
        var isDateOrTime = info.UnderlyingType.IsDateOrTime();

        // Determine generation flags based on info and supported operations
        var supportedOps = info.SupportedOperations;

        var generateAddition = isNumeric && supportedOps?.Addition == true && !info.ImplementsIAdditionOperators;

        var generateSubtraction =
            isNumeric && supportedOps?.Subtraction == true && !info.ImplementsISubtractionOperators;

        var generateDivision = isNumeric && supportedOps?.Division == true && !info.ImplementsIDivisionOperators;

        var generateMultiply = isNumeric && supportedOps?.Multiplication == true && !info.ImplementsIMultiplyOperators;

        var generateModulus = isNumeric && supportedOps?.Modulus == true && !info.ImplementsIModulusOperators;

        var generateParsable = !info.ImplementsIParsable;

        var generateComparison =
            (isNumeric || info.UnderlyingType == PrimitiveUnderlyingType.Char || isDateOrTime)
            && !info.ImplementsIComparisonOperators;

        var generateSpanFormattable =
            (info.UnderlyingType == PrimitiveUnderlyingType.Guid || isDateOrTime) && !info.ImplementsISpanFormattable;

        var generateUtf8SpanFormattable =
            info.UnderlyingImplementsIUtf8SpanFormattable && !info.ImplementsIUtf8SpanFormattable;

        var generateConvertibles = info.UnderlyingType != PrimitiveUnderlyingType.Guid;

        // Extract string length validation if present
        (int, int)? stringLengthValidation = null;
        if (info.StringLengthValidation is { } strLen && strLen.ShouldValidate)
        {
            var hasMinValue = strLen.MinLength >= 0;
            var hasMaxValue = strLen.MaxLength != int.MaxValue;

            if (hasMinValue || hasMaxValue)
            {
                stringLengthValidation = (strLen.MinLength, strLen.MaxLength);
            }
        }

        return new GeneratorData
        {
            FieldName = "_valueOrThrow",
            GenerateHashCode = !info.HasOverriddenHashCode,
            PrimitiveTypeFriendlyName = info.UnderlyingTypeFriendlyName,
            UnderlyingType = info.UnderlyingType,
            PrimitiveTypeIsValueType = info.UnderlyingTypeIsValueType,
            PrimitiveTypeNamespace = info.UnderlyingTypeNamespace,
            ParentPrimitives = info.ParentPrimitives,
            Namespace = info.Namespace,
            ClassName = info.ClassName,
            IsValueType = info.IsValueType,
            Modifiers = info.Modifiers,
            GenerateSubtractionOperators = generateSubtraction,
            GenerateAdditionOperators = generateAddition,
            GenerateDivisionOperators = generateDivision,
            GenerateMultiplyOperators = generateMultiply,
            GenerateModulusOperator = generateModulus,
            GenerateComparison = generateComparison,
            GenerateParsable = generateParsable,
            GenerateImplicitOperators = true,
            SerializationFormat = info.SerializationFormat,
            GenerateSpanFormattable = generateSpanFormattable,
            GenerateConvertibles = generateConvertibles,
            GenerateUtf8SpanFormattable = generateUtf8SpanFormattable,
            GenerateXmlSerializableMethods = globalOptions.GenerateXmlConverters,
            HasExplicitToStringMethod = info.HasExplicitToStringMethod,
            StringLengthAttributeValidation = stringLengthValidation,
            XmlDocumentation = info.XmlDocumentation,
            LocationFilePath = info.LocationFilePath,
            LocationLineStart = info.LocationLineStart,
        };
    }
}
