// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text;
using Headless.Jobs.SourceGenerator.Building;
using Headless.Jobs.SourceGenerator.Emitting;
using Headless.Jobs.SourceGenerator.Models;
using Headless.Jobs.SourceGenerator.Parsing;
using Headless.Jobs.SourceGenerator.Utilities;
using Headless.SourceGenerators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Headless.Jobs.SourceGenerator;

/// <summary>
/// Roslyn incremental source generator that discovers methods annotated with <c>[JobFunction]</c> and Jobs middleware
/// attributes, and emits one registration source file per assembly.
/// </summary>
/// <remarks>
/// Declarations are found with <c>ForAttributeWithMetadataName</c> and reduced to value-equal models in their
/// transform, so every later step compares by value and is skipped when an edit does not change what it produces.
/// Diagnostics travel beside the models as <see cref="DiagnosticInfo"/> and are reported separately, so reporting never
/// forces source to be re-emitted.
/// </remarks>
[Generator]
public sealed class JobsIncrementalSourceGenerator : IIncrementalGenerator
{
    /// <summary>Tracking names of the pipeline steps; tests assert these stay cached on unrelated edits.</summary>
    internal static class TrackingNames
    {
        public const string JobFunctions = nameof(JobFunctions);
        public const string ScheduleMiddleware = nameof(ScheduleMiddleware);
        public const string ExecuteMiddleware = nameof(ExecuteMiddleware);
        public const string AssemblyName = nameof(AssemblyName);
        public const string ReferencedFunctions = nameof(ReferencedFunctions);
        public const string GenerationResult = nameof(GenerationResult);
        public const string RegistrationModel = nameof(RegistrationModel);
        public const string Diagnostics = nameof(Diagnostics);
    }

    /// <summary>Registers the incremental pipeline.</summary>
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var functions = context
            .SyntaxProvider.ForAttributeWithMetadataName(
                SourceGeneratorConstants.JobFunctionAttributeMetadataName,
                JobFunctionParser.IsCandidate,
                JobFunctionParser.Parse
            )
            .Where(static result => result is not null)
            .Select(static (result, _) => result!)
            .WithTrackingName(TrackingNames.JobFunctions);

        var scheduleMiddleware = context
            .SyntaxProvider.ForAttributeWithMetadataName(
                SourceGeneratorConstants.ScheduleMiddlewareAttributeMetadataName,
                MiddlewareParser.IsCandidate,
                MiddlewareParser.Parse
            )
            .WithTrackingName(TrackingNames.ScheduleMiddleware);

        var executeMiddleware = context
            .SyntaxProvider.ForAttributeWithMetadataName(
                SourceGeneratorConstants.ExecuteMiddlewareAttributeMetadataName,
                MiddlewareParser.IsCandidate,
                MiddlewareParser.Parse
            )
            .WithTrackingName(TrackingNames.ExecuteMiddleware);

        var middleware = scheduleMiddleware
            .Collect()
            .Combine(executeMiddleware.Collect())
            .Select(static (pair, _) => pair.Left.AddRange(pair.Right).ToEquatableArray());

        var assemblyName = context
            .CompilationProvider.Select(static (compilation, _) => compilation.AssemblyName ?? string.Empty)
            .WithTrackingName(TrackingNames.AssemblyName);

        // Referenced descriptor metadata is read only when some assembly middleware targets a function by name, so an
        // edit in a project without such middleware never walks its references.
        var referencedFunctions = middleware
            .Select(
                static (results, _) =>
                    results.Any(result =>
                        result.Declarations.Any(declaration =>
                            declaration is { Placement: MiddlewarePlacement.Assembly, Function: not null }
                        )
                    )
            )
            .Combine(context.CompilationProvider)
            .Select(
                static (pair, cancellationToken) =>
                    pair.Left
                        ? MiddlewareParser.GetReferencedFunctionNames(pair.Right, cancellationToken)
                        : EquatableArray<string>.Empty
            )
            .WithTrackingName(TrackingNames.ReferencedFunctions);

        var generation = functions
            .Collect()
            .Combine(middleware)
            .Combine(referencedFunctions)
            .Combine(assemblyName)
            .Select(
                static (source, _) =>
                    JobsRegistrationBuilder.Build(
                        source.Left.Left.Left,
                        source.Left.Left.Right,
                        source.Left.Right,
                        source.Right
                    )
            )
            .WithTrackingName(TrackingNames.GenerationResult);

        var registrationModel = generation
            .Select(static (result, _) => result.Model)
            .WithTrackingName(TrackingNames.RegistrationModel);

        context.RegisterSourceOutput(
            registrationModel,
            static (productionContext, model) =>
            {
                if (model is null)
                {
                    return;
                }

                productionContext.AddSource(
                    SourceGeneratorConstants.GeneratedFileName,
                    SourceText.From(JobsSourceEmitter.Emit(model), Encoding.UTF8)
                );
            }
        );

        context.RegisterDiagnosticsOutput(
            generation.Select(static (result, _) => result.Diagnostics).WithTrackingName(TrackingNames.Diagnostics)
        );
    }
}
