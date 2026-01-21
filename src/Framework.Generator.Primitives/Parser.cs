// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Immutable;
using Framework.Generator.Primitives.Extensions;
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

        // Extract all needed attributes in single pass using cheap Name property
        AttributeData? supportedOpsAttr = null;
        AttributeData? serializationAttr = null;
        AttributeData? stringLengthAttr = null;

        foreach (var attr in typeSymbol.GetAttributes())
        {
            var name = attr.AttributeClass?.Name;

            switch (name)
            {
                case "SupportedOperationsAttribute":
                    supportedOpsAttr = attr;
                    break;
                case "SerializationFormatAttribute":
                    serializationAttr = attr;
                    break;
                case "StringLengthAttribute":
                    stringLengthAttr = attr;
                    break;
            }
        }

        // SupportedOperationsAttribute
        SupportedOperationsAttributeData? supportedOps = null;
        var isNumeric = underlyingType.IsNumeric();

        if (isNumeric)
        {
            supportedOps = _GetCombinedSupportedOperations(typeSymbol, underlyingType, parentSymbols, supportedOpsAttr);
        }

        // SerializationFormatAttribute
        string? serializationFormat = null;

        if (serializationAttr is not null && serializationAttr.ConstructorArguments.Length != 0)
        {
            serializationFormat = serializationAttr.ConstructorArguments[0].Value?.ToString();
        }

        // StringLengthAttribute
        StringLengthInfo? stringLengthInfo = null;

        if (
            underlyingType == PrimitiveUnderlyingType.String
            && stringLengthAttr is not null
            && stringLengthAttr.ConstructorArguments.Length >= 3
        )
        {
            var minValue = (int)stringLengthAttr.ConstructorArguments[0].Value!;
            var maxValue = (int)stringLengthAttr.ConstructorArguments[1].Value!;
            var validate = (bool)stringLengthAttr.ConstructorArguments[2].Value!;

            stringLengthInfo = new StringLengthInfo(minValue, maxValue, validate);
        }

        // Check interface implementations in single pass
        var (
            implementsIParsable,
            implementsIAdditionOperators,
            implementsISubtractionOperators,
            implementsIDivisionOperators,
            implementsIMultiplyOperators,
            implementsIModulusOperators,
            implementsIComparisonOperators,
            implementsISpanFormattable,
            implementsIUtf8SpanFormattable,
            underlyingImplementsIUtf8SpanFormattable
        ) = _ExtractInterfaceFlags(typeSymbol, primitiveType);

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
        catch (OperationCanceledException)
        {
            throw;
        }
#pragma warning disable ERP022
        catch
        {
            // Documentation extraction can fail for external types - acceptable
        }
#pragma warning restore ERP022

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
        List<INamedTypeSymbol> parentSymbols,
        AttributeData? preExtractedAttr
    )
    {
        var cache = new Dictionary<INamedTypeSymbol, SupportedOperationsAttributeData>(SymbolEqualityComparer.Default);

        return createCombinedAttribute(typeSymbol, underlyingType, parentSymbols.Count, preExtractedAttr, cache);

        static SupportedOperationsAttributeData createCombinedAttribute(
            INamedTypeSymbol @class,
            PrimitiveUnderlyingType underlyingType,
            int parentCount,
            AttributeData? attrData,
            Dictionary<INamedTypeSymbol, SupportedOperationsAttributeData> cache
        )
        {
            if (cache.TryGetValue(@class, out var cachedAttribute))
            {
                return cachedAttribute;
            }

            var attribute = attrData is null ? null : _GetAttributeFromData(attrData);

            if (parentCount == 0)
            {
                attribute ??= _GetDefaultAttributeData(underlyingType);
                cache[@class] = attribute;

                return attribute;
            }

            var parentType = @class.Interfaces.First(x => x.IsImplementIPrimitive());
            var parentSymbol = (parentType.TypeArguments[0] as INamedTypeSymbol)!;

            // Extract SupportedOperationsAttribute from parent using cheap Name property
            AttributeData? parentAttrData = null;

            foreach (var a in parentSymbol.GetAttributes())
            {
                if (a.AttributeClass?.Name == "SupportedOperationsAttribute")
                {
                    parentAttrData = a;
                    break;
                }
            }

            var combined = combineAttribute(
                attribute,
                createCombinedAttribute(parentSymbol, underlyingType, parentCount - 1, parentAttrData, cache)
            );

            cache[@class] = combined;

            return combined;
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
    /// <param name="provider">The AnalyzerConfigOptionsProvider to access analyzer options.</param>
    /// <param name="ct">CancellationToken</param>
    /// <returns>The PrimitiveGlobalOptions for the generator.</returns>
    internal static PrimitiveGlobalOptions ParseGlobalOptions(
        AnalyzerConfigOptionsProvider provider,
        CancellationToken ct
    )
    {
        ct.ThrowIfCancellationRequested();

        return new PrimitiveGlobalOptions
        {
            GenerateJsonConverters = parseBool(provider, "PrimitiveJsonConverters"),
            GenerateTypeConverters = parseBool(provider, "PrimitiveTypeConverters"),
            GenerateSwashbuckleSwaggerConverters = parseBool(provider, "PrimitiveSwashbuckleSwaggerConverters"),
            GenerateNswagSwaggerConverters = parseBool(provider, "PrimitiveNswagSwaggerConverters"),
            GenerateXmlConverters = parseBool(provider, "PrimitiveXmlConverters"),
            GenerateEntityFrameworkValueConverters = parseBool(provider, "PrimitiveEntityFrameworkValueConverters"),
            GenerateDapperConverters = parseBool(provider, "PrimitiveDapperConverters"),
        };

        static bool parseBool(AnalyzerConfigOptionsProvider p, string key)
        {
            return p.GlobalOptions.TryGetValue($"build_property.{key}", out var value)
                && bool.TryParse(value, out var result)
                && result;
        }
    }

    /// <summary>Extracts interface flags in single pass over AllInterfaces.</summary>
    private static (
        bool IParsable,
        bool IAdditionOperators,
        bool ISubtractionOperators,
        bool IDivisionOperators,
        bool IMultiplyOperators,
        bool IModulusOperators,
        bool IComparisonOperators,
        bool ISpanFormattable,
        bool IUtf8SpanFormattable,
        bool UnderlyingIUtf8SpanFormattable
    ) _ExtractInterfaceFlags(INamedTypeSymbol type, INamedTypeSymbol primitiveType)
    {
        var flags = (false, false, false, false, false, false, false, false, false, false);

        foreach (var iface in type.AllInterfaces)
        {
            var name = iface.Name;
            var ns = iface.ContainingNamespace;

            if (_IsSystemNamespace(ns))
            {
                if (name == "IParsable")
                {
                    flags.Item1 = true;
                }
                else if (name == "ISpanFormattable")
                {
                    flags.Item8 = true;
                }
                else if (name == "IUtf8SpanFormattable")
                {
                    flags.Item9 = true;
                }
            }
            else if (_IsSystemNumericsNamespace(ns))
            {
                switch (name)
                {
                    case "IAdditionOperators":
                        flags.Item2 = true;
                        break;
                    case "ISubtractionOperators":
                        flags.Item3 = true;
                        break;
                    case "IDivisionOperators":
                        flags.Item4 = true;
                        break;
                    case "IMultiplyOperators":
                        flags.Item5 = true;
                        break;
                    case "IModulusOperators":
                        flags.Item6 = true;
                        break;
                    case "IComparisonOperators":
                        flags.Item7 = true;
                        break;
                }
            }
        }

        // Check primitiveType for IUtf8SpanFormattable
        foreach (var iface in primitiveType.AllInterfaces)
        {
            if (iface.Name == "IUtf8SpanFormattable" && _IsSystemNamespace(iface.ContainingNamespace))
            {
                flags.Item10 = true;
                break;
            }
        }

        return flags;
    }

    private static bool _IsSystemNamespace(INamespaceSymbol ns) =>
        ns is { Name: "System", ContainingNamespace.IsGlobalNamespace: true };

    private static bool _IsSystemNumericsNamespace(INamespaceSymbol ns) =>
        ns
            is {
                Name: "Numerics",
                ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true }
            };
}
