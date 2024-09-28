// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Generator.Primitives.Extensions;
using Framework.Generator.Primitives.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Framework.Generator.Primitives;

/// <summary>
/// A custom source code generator responsible for generating code for primitive types
/// based on their declarations in the source code.
/// </summary>
[Generator]
public sealed class PrimitiveGenerator : IIncrementalGenerator
{
    /// <summary>Initializes the PrimitiveGenerator and registers it as a source code generator.</summary>
    /// <param name="context">The generator initialization context.</param>
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // #if DEBUG
        //         System.Diagnostics.Debugger.Launch();
        // #endif

        var primitivesToGenerate = context
            .SyntaxProvider.CreateSyntaxProvider(
                predicate: _IsSyntaxTargetForGeneration,
                transform: _GetSemanticTargetForGeneration
            )
            .Where(static x => x is not null);

        var assemblyNames = context.CompilationProvider.Select(
            (c, _) => c.AssemblyName ?? throw new InvalidOperationException("Assembly name must be provided")
        );

        var globalOptions = context.AnalyzerConfigOptionsProvider.Select((c, _) => _GetGlobalOptions(c));

        var allData = primitivesToGenerate.Collect().Combine(assemblyNames).Combine(globalOptions);

        context.RegisterSourceOutput(
            allData,
            static (context, pair) => Executor.Execute(in context, in pair.Left.Left, in pair.Left.Right, in pair.Right)
        );
    }

    /// <summary>Gets the global options for the PrimitiveGenerator generator.</summary>
    /// <param name="analyzerOptions">The AnalyzerConfigOptionsProvider to access analyzer options.</param>
    /// <returns>The PrimitiveGlobalOptions for the generator.</returns>
    private static PrimitiveGlobalOptions _GetGlobalOptions(AnalyzerConfigOptionsProvider analyzerOptions)
    {
        var result = new PrimitiveGlobalOptions();

        if (
            analyzerOptions.GlobalOptions.TryGetValue("build_property.PrimitiveJsonConverters", out var value)
            && bool.TryParse(value, out var generateJsonConverters)
        )
        {
            result.GenerateJsonConverters = generateJsonConverters;
        }

        if (
            analyzerOptions.GlobalOptions.TryGetValue("build_property.PrimitiveTypeConverters", out value)
            && bool.TryParse(value, out var generateTypeConverter)
        )
        {
            result.GenerateTypeConverters = generateTypeConverter;
        }

        if (
            analyzerOptions.GlobalOptions.TryGetValue("build_property.PrimitiveSwashbuckleSwaggerConverters", out value)
            && bool.TryParse(value, out var generateSwashbuckleSwaggerConverters)
        )
        {
            result.GenerateSwashbuckleSwaggerConverters = generateSwashbuckleSwaggerConverters;
        }

        if (
            analyzerOptions.GlobalOptions.TryGetValue("build_property.PrimitiveNswagSwaggerConverters", out value)
            && bool.TryParse(value, out var generateNswagSwaggerConverters)
        )
        {
            result.GenerateNswagSwaggerConverters = generateNswagSwaggerConverters;
        }

        if (
            analyzerOptions.GlobalOptions.TryGetValue("build_property.PrimitiveXmlConverters", out value)
            && bool.TryParse(value, out var generateXmlSerialization)
        )
        {
            result.GenerateXmlConverters = generateXmlSerialization;
        }

        if (
            analyzerOptions.GlobalOptions.TryGetValue(
                "build_property.PrimitiveEntityFrameworkValueConverters",
                out value
            ) && bool.TryParse(value, out var generateEntityFrameworkValueConverters)
        )
        {
            result.GenerateEntityFrameworkValueConverters = generateEntityFrameworkValueConverters;
        }

        return result;
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
    private static INamedTypeSymbol? _GetSemanticTargetForGeneration(GeneratorSyntaxContext ctx, CancellationToken ct)
    {
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
    private static bool _IsSyntaxTargetForGeneration(SyntaxNode syntaxNode, CancellationToken ct)
    {
        return syntaxNode
            is ClassDeclarationSyntax { BaseList: not null }
                or StructDeclarationSyntax { BaseList: not null }
                or RecordDeclarationSyntax { BaseList: not null };
    }
}
