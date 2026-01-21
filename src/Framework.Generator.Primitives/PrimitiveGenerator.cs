// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.CodeAnalysis;

namespace Framework.Generator.Primitives;

/* This is based on https://github.com/altasoft/DomainPrimitives */

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

        // The semantic transform now returns PrimitiveTypeInfo? which is equatable,
        // enabling proper incremental caching in the generator pipeline.
        var primitivesToGenerate = context
            .SyntaxProvider.CreateSyntaxProvider(
                predicate: Parser.IsSyntaxTargetForGeneration,
                transform: Parser.GetSemanticTargetForGeneration
            )
            .Where(static x => x is not null);

        var assemblyNames = context.CompilationProvider.Select(
            (c, _) => c.AssemblyName ?? throw new InvalidOperationException("Assembly name must be provided")
        );

        var globalOptions = context.AnalyzerConfigOptionsProvider.Select(Parser.ParseGlobalOptions);

        var allData = primitivesToGenerate.Collect().Combine(assemblyNames).Combine(globalOptions);

        context.RegisterSourceOutput(
            allData,
            static (context, pair) =>
                Emitter.Execute(
                    context: in context,
                    typesToGenerate: in pair.Left.Left,
                    assemblyName: in pair.Left.Right,
                    globalOptions: in pair.Right
                )
        );
    }
}
