// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Generator.Primitives.Extensions;
using Framework.Generator.Primitives.Models;
using Microsoft.CodeAnalysis;
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
    /// The <see cref="TypeDeclarationSyntax"/> if the syntax node is a semantic target; otherwise, <see langword="null"/>.
    /// </returns>
    /// <remarks>
    /// This method analyzes a <see cref="TypeDeclarationSyntax"/> node to determine if it represents a semantic target
    /// for code generation. A semantic target is typically a class, struct, or record declaration that is not abstract
    /// and implements one or more interfaces marked as domain value types.
    /// </remarks>
    /// <seealso cref="PrimitiveGenerator"/>
    internal static INamedTypeSymbol? GetSemanticTargetForGeneration(GeneratorSyntaxContext ctx, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var typeSyntax = (TypeDeclarationSyntax)ctx.Node;

        var symbol = ctx.SemanticModel.GetDeclaredSymbol(typeSyntax, ct);

        if (symbol is not INamedTypeSymbol typeSymbol)
        {
            return null;
        }

        return !typeSymbol.IsAbstract && typeSymbol.AllInterfaces.Any(x => x.IsImplementIPrimitive())
            ? typeSymbol
            : null;
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
