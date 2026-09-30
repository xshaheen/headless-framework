// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text;
using Headless.Messaging.SourceGenerator.Building;
using Headless.Messaging.SourceGenerator.Emitting;
using Headless.Messaging.SourceGenerator.Parsing;
using Headless.Messaging.SourceGenerator.Utilities;
using Headless.SourceGenerators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Headless.Messaging.SourceGenerator;

/// <summary>
/// Roslyn incremental source generator that discovers <c>[BusConsumer]</c> and <c>[QueueConsumer]</c> classes and emits
/// one <c>MessagingModule</c> per assembly, with a typed dispatcher for each consumer class.
/// </summary>
/// <remarks>
/// Declarations are found with <c>ForAttributeWithMetadataName</c> and reduced to value-equal models in their
/// transform, so every later step compares by value and is skipped when an edit does not change what it produces.
/// Diagnostics travel beside the models as <see cref="DiagnosticInfo"/> and are reported separately, so reporting never
/// forces source to be re-emitted.
/// </remarks>
[Generator]
public sealed class MessagingIncrementalSourceGenerator : IIncrementalGenerator
{
    /// <summary>Tracking names of the pipeline steps; tests assert these stay cached on unrelated edits.</summary>
    internal static class TrackingNames
    {
        public const string BusConsumers = nameof(BusConsumers);
        public const string QueueConsumers = nameof(QueueConsumers);
        public const string AssemblyName = nameof(AssemblyName);
        public const string GenerationResult = nameof(GenerationResult);
        public const string RegistrationModel = nameof(RegistrationModel);
        public const string Diagnostics = nameof(Diagnostics);
    }

    /// <summary>Registers the incremental pipeline.</summary>
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var busConsumers = context
            .SyntaxProvider.ForAttributeWithMetadataName(
                SourceGeneratorConstants.BusConsumerAttributeMetadataName,
                ConsumerParser.IsCandidate,
                ConsumerParser.ParseBus
            )
            .Where(static result => result is not null)
            .Select(static (result, _) => result!)
            .WithTrackingName(TrackingNames.BusConsumers);

        var queueConsumers = context
            .SyntaxProvider.ForAttributeWithMetadataName(
                SourceGeneratorConstants.QueueConsumerAttributeMetadataName,
                ConsumerParser.IsCandidate,
                ConsumerParser.ParseQueue
            )
            .Where(static result => result is not null)
            .Select(static (result, _) => result!)
            .WithTrackingName(TrackingNames.QueueConsumers);

        var assemblyName = context
            .CompilationProvider.Select(static (compilation, _) => compilation.AssemblyName ?? string.Empty)
            .WithTrackingName(TrackingNames.AssemblyName);

        var generation = busConsumers
            .Collect()
            .Combine(queueConsumers.Collect())
            .Combine(assemblyName)
            .Select(
                static (source, _) =>
                    MessagingRegistrationBuilder.Build(source.Left.Left, source.Left.Right, source.Right)
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
                    SourceText.From(MessagingSourceEmitter.Emit(model), Encoding.UTF8)
                );
            }
        );

        context.RegisterDiagnosticsOutput(
            generation.Select(static (result, _) => result.Diagnostics).WithTrackingName(TrackingNames.Diagnostics)
        );
    }
}
