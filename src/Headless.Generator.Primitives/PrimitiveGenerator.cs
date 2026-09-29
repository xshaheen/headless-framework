// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.SourceGenerators;
using Microsoft.CodeAnalysis;

namespace Headless.Generator.Primitives;

/* This is based on https://github.com/altasoft/DomainPrimitives */

/// <summary>
/// A custom source code generator responsible for generating code for primitive types
/// based on their declarations in the source code.
/// </summary>
[Generator]
public sealed class PrimitiveGenerator : IIncrementalGenerator
{
    /// <summary>Tracking names of the pipeline steps; tests assert these stay cached on unrelated edits.</summary>
    internal static class TrackingNames
    {
        public const string ParseResults = nameof(ParseResults);
        public const string Primitives = nameof(Primitives);
        public const string AssemblyName = nameof(AssemblyName);
        public const string GlobalOptions = nameof(GlobalOptions);
        public const string Diagnostics = nameof(Diagnostics);
    }

    /// <summary>Initializes the PrimitiveGenerator and registers it as a source code generator.</summary>
    /// <param name="context">The generator initialization context.</param>
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // #if DEBUG
        //         System.Diagnostics.Debugger.Launch();
        // #endif

        // Primitives are found by the IPrimitive<> interface, not an attribute, so ForAttributeWithMetadataName does
        // not apply. The transform reduces each declaration to value-equal data, so later steps are reused when an
        // edit does not change a primitive.
        var parseResults = context
            .SyntaxProvider.CreateSyntaxProvider(
                predicate: Parser.IsSyntaxTargetForGeneration,
                transform: Parser.GetSemanticTargetForGeneration
            )
            .Where(static x => x is not null)
            .Select(static (x, _) => x!)
            .WithTrackingName(TrackingNames.ParseResults);

        var primitivesToGenerate = parseResults
            .Select(static (x, _) => x.Info)
            .WithTrackingName(TrackingNames.Primitives);

        var assemblyNames = context
            .CompilationProvider.Select(
                (c, _) => c.AssemblyName ?? throw new InvalidOperationException("Assembly name must be provided")
            )
            .WithTrackingName(TrackingNames.AssemblyName);

        var globalOptions = context
            .AnalyzerConfigOptionsProvider.Select(Parser.ParseGlobalOptions)
            .WithTrackingName(TrackingNames.GlobalOptions);

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

        context.RegisterDiagnosticsOutput(
            parseResults
                .Collect()
                .Select(static (results, _) => results.SelectMany(x => x.Diagnostics).ToEquatableArray())
                .WithTrackingName(TrackingNames.Diagnostics)
        );
    }
}
