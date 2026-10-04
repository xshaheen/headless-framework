// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.InteropServices;
using Headless.SourceGenerators;

namespace Headless.Generator.Primitives.Models;

/// <summary>
/// Equatable data extracted from INamedTypeSymbol for incremental generation caching.
/// This struct captures all data needed for code emission so that the generator pipeline
/// can properly compare values and skip regeneration when nothing has changed.
/// </summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct PrimitiveTypeInfo(
    string FullyQualifiedName,
    string Namespace,
    string ClassName,
    bool IsValueType,
    bool IsRecord,
    string Modifiers,
    PrimitiveUnderlyingType UnderlyingType,
    string UnderlyingTypeFriendlyName,
    bool UnderlyingTypeIsValueType,
    string UnderlyingTypeNamespace,
    bool HasOverriddenHashCode,
    bool HasExplicitToStringMethod,
    EquatableArray<ParentPrimitiveInfo> ParentPrimitives,
    SupportedOperationsAttributeData? SupportedOperations,
    string? SerializationFormat,
    StringLengthInfo? StringLengthValidation,
    bool ImplementsIParsable,
    bool ImplementsIAdditionOperators,
    bool ImplementsISubtractionOperators,
    bool ImplementsIDivisionOperators,
    bool ImplementsIMultiplyOperators,
    bool ImplementsIModulusOperators,
    bool ImplementsIComparisonOperators,
    bool ImplementsISpanFormattable,
    bool ImplementsIUtf8SpanFormattable,
    bool UnderlyingImplementsIUtf8SpanFormattable,
    string? XmlDocumentation
);

/// <summary>
/// What parsing one primitive declaration produced: the data to emit, when the declaration can be generated,
/// and the diagnostics
/// found while reading it. Diagnostics travel beside the model so reporting them never forces source to be re-emitted.
/// </summary>
internal sealed record PrimitiveParseResult(PrimitiveTypeInfo? Info, EquatableArray<DiagnosticInfo> Diagnostics);

/// <summary>Info about a parent primitive type in the inheritance chain.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct ParentPrimitiveInfo(
    string Name,
    string Namespace,
    string FriendlyName,
    bool IsValueType
);

/// <summary>String length validation info.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct StringLengthInfo(int MinLength, int MaxLength, bool ShouldValidate);
