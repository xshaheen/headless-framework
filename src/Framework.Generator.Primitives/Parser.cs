// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Immutable;
using Framework.Generator.Primitives.Extensions;
using Framework.Generator.Primitives.Helpers;
using Framework.Generator.Primitives.Models;
using Framework.Generator.Primitives.Shared;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Framework.Generator.Primitives;

internal static class Parser
{
    /// <summary>Determines if a given syntax node is a valid target for code generation.</summary>
    /// <param name="syntaxNode">The syntax node to be evaluated.</param>
    /// <param name="ct">CancellationToken</param>
    /// <returns><see langword="true"/> if the syntax node is a valid target for code generation; otherwise, <see langword="false"/>.</returns>
    /// <remarks>
    /// This method checks if the provided syntax node represents a class, struct, or record declaration that has a
    /// non-null base list. Such declarations are considered valid targets for code generation as they can be extended
    /// with additional members.
    /// </remarks>
    /// <seealso cref="PrimitiveGenerator"/>
    internal static bool IsSyntaxTargetForGeneration(SyntaxNode syntaxNode, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        return syntaxNode
            is ClassDeclarationSyntax { BaseList: not null }
                or StructDeclarationSyntax { BaseList: not null }
                or RecordDeclarationSyntax { BaseList: not null };
    }

    /// <summary>Determines if a given syntax node represents a semantic target for code generation.</summary>
    /// <param name="ctx">The generator syntax context.</param>
    /// <param name="ct">CancellationToken</param>
    /// <returns>
    /// The <see cref="PrimitiveTypeInfo"/> if the syntax node is a semantic target; otherwise, <see langword="null"/>.
    /// </returns>
    /// <remarks>
    /// This method analyzes a <see cref="TypeDeclarationSyntax"/> node to determine if it represents a semantic target
    /// for code generation. A semantic target is typically a class, struct, or record declaration that is not abstract
    /// and implements one or more interfaces marked as domain value types.
    /// All data needed for emission is extracted here to enable proper incremental caching.
    /// </remarks>
    /// <seealso cref="PrimitiveGenerator"/>
    internal static PrimitiveTypeInfo? GetSemanticTargetForGeneration(GeneratorSyntaxContext ctx, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var typeSyntax = (TypeDeclarationSyntax)ctx.Node;

        var symbol = ctx.SemanticModel.GetDeclaredSymbol(typeSyntax, ct);

        if (symbol?.IsAbstract != false)
        {
            return null;
        }

        var primitiveInterface = symbol.AllInterfaces.FirstOrDefault(x => x.IsImplementIPrimitive());

        if (primitiveInterface is null)
        {
            return null;
        }

        // Extract all data needed for emission
        return _ExtractPrimitiveTypeInfo(symbol, primitiveInterface, ct);
    }

    /// <summary>Extracts all data from the symbol into an equatable PrimitiveTypeInfo struct.</summary>
    private static PrimitiveTypeInfo? _ExtractPrimitiveTypeInfo(
        INamedTypeSymbol typeSymbol,
        INamedTypeSymbol primitiveInterface,
        CancellationToken ct
    )
    {
        ct.ThrowIfCancellationRequested();

        if (primitiveInterface.TypeArguments[0] is not INamedTypeSymbol primitiveType)
        {
            return null;
        }

        var parentSymbols = new List<INamedTypeSymbol>(4);
        var (underlyingType, underlyingTypeSymbol) = primitiveType.GetUnderlyingPrimitiveType(parentSymbols);

        if (underlyingType == PrimitiveUnderlyingType.Other)
        {
            return null;
        }

        // Extract modifiers
        var modifiers = typeSymbol.GetModifiers() ?? "public partial";

        // Check for overridden GetHashCode
        var hasOverriddenHashCode = typeSymbol
            .GetMembersOfType<IMethodSymbol>()
            .Any(x => string.Equals(x.OverriddenMethod?.Name, "GetHashCode", StringComparison.Ordinal));

        // Check for explicit ToString method
        var baseType = parentSymbols.Count == 0 ? underlyingTypeSymbol : parentSymbols[0];
        var hasExplicitToStringMethod = typeSymbol
            .GetMembersOfType<IMethodSymbol>()
            .Any(x =>
                string.Equals(x.Name, "ToString", StringComparison.Ordinal)
                && x is { IsStatic: true, Parameters.Length: 1 }
                && x.Parameters[0].Type.Equals(baseType, SymbolEqualityComparer.Default)
            );

        // Extract parent primitives info
        var parentPrimitives = parentSymbols
            .Select(p => new ParentPrimitiveInfo(
                p.Name,
                p.ContainingNamespace.ToDisplayString(),
                p.GetFriendlyName(),
                p.IsValueType
            ))
            .ToImmutableArray();

        // Extract attributes
        var attributes = typeSymbol.GetAttributes();

        // SupportedOperationsAttribute
        SupportedOperationsAttributeData? supportedOps = null;
        var isNumeric = underlyingType.IsNumeric();

        if (isNumeric)
        {
            supportedOps = _GetCombinedSupportedOperations(typeSymbol, underlyingType, parentSymbols);
        }

        // SerializationFormatAttribute
        string? serializationFormat = null;
        var serializationAttr = attributes.FirstOrDefault(x =>
            string.Equals(
                x.AttributeClass?.ToDisplayString(),
                AbstractionConstants.SerializationFormatAttributeFullName,
                StringComparison.Ordinal
            )
        );

        if (serializationAttr is not null && serializationAttr.ConstructorArguments.Length != 0)
        {
            serializationFormat = serializationAttr.ConstructorArguments[0].Value?.ToString();
        }

        // StringLengthAttribute
        StringLengthInfo? stringLengthInfo = null;
        if (underlyingType == PrimitiveUnderlyingType.String)
        {
            var stringLengthAttr = attributes.FirstOrDefault(x =>
                string.Equals(
                    x.AttributeClass?.ToDisplayString(),
                    AbstractionConstants.StringLengthAttributeFullName,
                    StringComparison.Ordinal
                )
            );

            if (stringLengthAttr is not null && stringLengthAttr.ConstructorArguments.Length >= 3)
            {
                var minValue = (int)stringLengthAttr.ConstructorArguments[0].Value!;
                var maxValue = (int)stringLengthAttr.ConstructorArguments[1].Value!;
                var validate = (bool)stringLengthAttr.ConstructorArguments[2].Value!;

                stringLengthInfo = new StringLengthInfo(minValue, maxValue, validate);
            }
        }

        // Check interface implementations
        var implementsIParsable = typeSymbol.ImplementsInterface(TypeNames.IParsable);
        var implementsIAdditionOperators = typeSymbol.ImplementsInterface(TypeNames.IAdditionOperators);
        var implementsISubtractionOperators = typeSymbol.ImplementsInterface(TypeNames.ISubtractionOperators);
        var implementsIDivisionOperators = typeSymbol.ImplementsInterface(TypeNames.IDivisionOperators);
        var implementsIMultiplyOperators = typeSymbol.ImplementsInterface(TypeNames.IMultiplyOperators);
        var implementsIModulusOperators = typeSymbol.ImplementsInterface(TypeNames.IModulusOperators);
        var implementsIComparisonOperators = typeSymbol.ImplementsInterface(TypeNames.IComparisonOperators);
        var implementsISpanFormattable = typeSymbol.ImplementsInterface(TypeNames.ISpanFormattable);
        var implementsIUtf8SpanFormattable = typeSymbol.ImplementsInterface(TypeNames.IUtf8SpanFormattable);
        var underlyingImplementsIUtf8SpanFormattable =
            primitiveType.ImplementsInterface(TypeNames.IUtf8SpanFormattable);

        // Get location info for diagnostics
        var location = typeSymbol.Locations.FirstOrDefault();
        var locationFilePath = location?.SourceTree?.FilePath ?? "";
        var locationLineStart = location?.GetLineSpan().StartLinePosition.Line ?? 0;

        // Get XML documentation for Swagger
        string? xmlDocumentation = null;
        try
        {
            xmlDocumentation = typeSymbol.GetDocumentationCommentXml(cancellationToken: ct);
        }
        catch
        {
            // Ignore documentation extraction failures
        }

        return new PrimitiveTypeInfo(
            FullyQualifiedName: typeSymbol.ToDisplayString(),
            Namespace: typeSymbol.ContainingNamespace.ToDisplayString(),
            ClassName: typeSymbol.Name,
            IsValueType: typeSymbol.IsValueType,
            IsRecord: typeSymbol.IsRecord,
            Modifiers: modifiers,
            UnderlyingType: underlyingType,
            UnderlyingTypeFriendlyName: underlyingTypeSymbol.GetFriendlyName(),
            UnderlyingTypeIsValueType: underlyingTypeSymbol.IsValueType,
            UnderlyingTypeNamespace: underlyingTypeSymbol.ContainingNamespace.ToDisplayString(),
            HasOverriddenHashCode: hasOverriddenHashCode,
            HasExplicitToStringMethod: hasExplicitToStringMethod,
            ParentPrimitives: parentPrimitives,
            SupportedOperations: supportedOps,
            SerializationFormat: serializationFormat,
            StringLengthValidation: stringLengthInfo,
            ImplementsIParsable: implementsIParsable,
            ImplementsIAdditionOperators: implementsIAdditionOperators,
            ImplementsISubtractionOperators: implementsISubtractionOperators,
            ImplementsIDivisionOperators: implementsIDivisionOperators,
            ImplementsIMultiplyOperators: implementsIMultiplyOperators,
            ImplementsIModulusOperators: implementsIModulusOperators,
            ImplementsIComparisonOperators: implementsIComparisonOperators,
            ImplementsISpanFormattable: implementsISpanFormattable,
            ImplementsIUtf8SpanFormattable: implementsIUtf8SpanFormattable,
            UnderlyingImplementsIUtf8SpanFormattable: underlyingImplementsIUtf8SpanFormattable,
            LocationFilePath: locationFilePath,
            LocationLineStart: locationLineStart,
            XmlDocumentation: xmlDocumentation
        );
    }

    /// <summary>
    /// Retrieves the combined SupportedOperationsAttributeData for a type, considering inheritance.
    /// </summary>
    private static SupportedOperationsAttributeData _GetCombinedSupportedOperations(
        INamedTypeSymbol typeSymbol,
        PrimitiveUnderlyingType underlyingType,
        List<INamedTypeSymbol> parentSymbols
    )
    {
        var cache = new Dictionary<INamedTypeSymbol, SupportedOperationsAttributeData>(SymbolEqualityComparer.Default);

        return createCombinedAttribute(typeSymbol, underlyingType, parentSymbols.Count, cache);

        static SupportedOperationsAttributeData createCombinedAttribute(
            INamedTypeSymbol @class,
            PrimitiveUnderlyingType underlyingType,
            int parentCount,
            Dictionary<INamedTypeSymbol, SupportedOperationsAttributeData> cache
        )
        {
            if (cache.TryGetValue(@class, out var cachedAttribute))
            {
                return cachedAttribute;
            }

            var attributeData = @class
                .GetAttributes()
                .FirstOrDefault(x =>
                    string.Equals(
                        x.AttributeClass?.ToDisplayString(),
                        AbstractionConstants.SupportedOperationsAttributeFullName,
                        StringComparison.Ordinal
                    )
                );

            var attribute = attributeData is null ? null : _GetAttributeFromData(attributeData);

            if (parentCount == 0)
            {
                attribute ??= _GetDefaultAttributeData(underlyingType);
                cache[@class] = attribute;

                return attribute;
            }

            var parentType = @class.Interfaces.First(x => x.IsImplementIPrimitive());

            var attr = combineAttribute(
                attribute,
                createCombinedAttribute(
                    (parentType.TypeArguments[0] as INamedTypeSymbol)!,
                    underlyingType,
                    parentCount - 1,
                    cache
                )
            );

            cache[@class] = attr;

            return attr;
        }

        static SupportedOperationsAttributeData combineAttribute(
            SupportedOperationsAttributeData? attribute,
            SupportedOperationsAttributeData parentAttribute
        )
        {
            if (attribute is null)
            {
                return parentAttribute;
            }

            return new SupportedOperationsAttributeData
            {
                Addition = attribute.Addition && parentAttribute.Addition,
                Subtraction = attribute.Subtraction && parentAttribute.Subtraction,
                Multiplication = attribute.Multiplication && parentAttribute.Multiplication,
                Division = attribute.Division && parentAttribute.Division,
                Modulus = attribute.Modulus && parentAttribute.Modulus,
            };
        }
    }

    /// <summary>
    /// Gets the default SupportedOperationsAttributeData based on the given NumericType.
    /// </summary>
    private static SupportedOperationsAttributeData _GetDefaultAttributeData(PrimitiveUnderlyingType underlyingType)
    {
        var @default = underlyingType switch
        {
            PrimitiveUnderlyingType.Byte => false,
            PrimitiveUnderlyingType.SByte => false,
            PrimitiveUnderlyingType.Int16 => false,
            PrimitiveUnderlyingType.UInt16 => false,
            PrimitiveUnderlyingType.Int32 => true,
            PrimitiveUnderlyingType.UInt32 => true,
            PrimitiveUnderlyingType.Int64 => true,
            PrimitiveUnderlyingType.UInt64 => true,
            PrimitiveUnderlyingType.Decimal => true,
            PrimitiveUnderlyingType.Double => true,
            PrimitiveUnderlyingType.Single => true,
            _ => true,
        };

        return new SupportedOperationsAttributeData
        {
            Addition = @default,
            Subtraction = @default,
            Multiplication = @default,
            Division = @default,
            Modulus = @default,
        };
    }

    /// <summary>
    /// Creates a SupportedOperationsAttributeData from the provided AttributeData.
    /// </summary>
    private static SupportedOperationsAttributeData _GetAttributeFromData(AttributeData attributeData)
    {
        return new SupportedOperationsAttributeData
        {
            Addition = createAttributeValue(attributeData, nameof(SupportedOperationsAttributeData.Addition)),
            Subtraction = createAttributeValue(attributeData, nameof(SupportedOperationsAttributeData.Subtraction)),
            Multiplication = createAttributeValue(
                attributeData,
                nameof(SupportedOperationsAttributeData.Multiplication)
            ),
            Division = createAttributeValue(attributeData, nameof(SupportedOperationsAttributeData.Division)),
            Modulus = createAttributeValue(attributeData, nameof(SupportedOperationsAttributeData.Modulus)),
        };

        static bool createAttributeValue(AttributeData? parentAttributeData, string property)
        {
            return parentAttributeData!
                .NamedArguments.FirstOrDefault(x => string.Equals(x.Key, property, StringComparison.Ordinal))
                .Value.Value
                is true;
        }
    }

    /// <summary>Gets the global options for the PrimitiveGenerator generator.</summary>
    /// <param name="a">The AnalyzerConfigOptionsProvider to access analyzer options.</param>
    /// <param name="ct">CancellationToken</param>
    /// <returns>The PrimitiveGlobalOptions for the generator.</returns>
    internal static PrimitiveGlobalOptions ParseGlobalOptions(AnalyzerConfigOptionsProvider a, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var result = new PrimitiveGlobalOptions();

        if (
            a.GlobalOptions.TryGetValue("build_property.PrimitiveJsonConverters", out var value)
            && bool.TryParse(value, out var generateJsonConverters)
        )
        {
            result.GenerateJsonConverters = generateJsonConverters;
        }

        if (
            a.GlobalOptions.TryGetValue("build_property.PrimitiveTypeConverters", out value)
            && bool.TryParse(value, out var generateTypeConverter)
        )
        {
            result.GenerateTypeConverters = generateTypeConverter;
        }

        if (
            a.GlobalOptions.TryGetValue("build_property.PrimitiveSwashbuckleSwaggerConverters", out value)
            && bool.TryParse(value, out var generateSwashbuckleSwaggerConverters)
        )
        {
            result.GenerateSwashbuckleSwaggerConverters = generateSwashbuckleSwaggerConverters;
        }

        if (
            a.GlobalOptions.TryGetValue("build_property.PrimitiveNswagSwaggerConverters", out value)
            && bool.TryParse(value, out var generateNswagSwaggerConverters)
        )
        {
            result.GenerateNswagSwaggerConverters = generateNswagSwaggerConverters;
        }

        if (
            a.GlobalOptions.TryGetValue("build_property.PrimitiveXmlConverters", out value)
            && bool.TryParse(value, out var generateXmlSerialization)
        )
        {
            result.GenerateXmlConverters = generateXmlSerialization;
        }

        if (
            a.GlobalOptions.TryGetValue("build_property.PrimitiveEntityFrameworkValueConverters", out value)
            && bool.TryParse(value, out var generateEntityFrameworkValueConverters)
        )
        {
            result.GenerateEntityFrameworkValueConverters = generateEntityFrameworkValueConverters;
        }

        if (
            a.GlobalOptions.TryGetValue("build_property.PrimitiveDapperConverters", out value)
            && bool.TryParse(value, out var generateDapperConverters)
        )
        {
            result.GenerateDapperConverters = generateDapperConverters;
        }

        return result;
    }
}
