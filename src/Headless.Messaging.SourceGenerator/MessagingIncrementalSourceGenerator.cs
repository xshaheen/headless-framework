// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text;
using Headless.Messaging.SourceGenerator.Building;
using Headless.Messaging.SourceGenerator.Emitting;
using Headless.Messaging.SourceGenerator.Models;
using Headless.Messaging.SourceGenerator.Parsing;
using Headless.Messaging.SourceGenerator.Utilities;
using Headless.Messaging.SourceGenerator.Validation;
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
        public const string RequestCalls = nameof(RequestCalls);
        public const string LocalResponders = nameof(LocalResponders);
        public const string ReferencedResponders = nameof(ReferencedResponders);
        public const string RequestDiagnostics = nameof(RequestDiagnostics);
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
            .WhereNotNull()
            .WithTrackingName(TrackingNames.BusConsumers);

        var queueConsumers = context
            .SyntaxProvider.ForAttributeWithMetadataName(
                SourceGeneratorConstants.QueueConsumerAttributeMetadataName,
                ConsumerParser.IsCandidate,
                ConsumerParser.ParseQueue
            )
            .WhereNotNull()
            .WithTrackingName(TrackingNames.QueueConsumers);

        var assemblyName = context
            .CompilationProvider.SelectAssemblyName()
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

        _RegisterRequestCallChecks(context, registrationModel);
    }

    /// <summary>
    /// Checks every <c>RequestAsync&lt;TRequest, TResponse&gt;</c> call against the responders the project can see: the
    /// ones this assembly registers and the ones its references publish as generated metadata.
    /// </summary>
    /// <remarks>
    /// Referenced metadata is read from the compilation, which changes on every edit, so it is read only while the
    /// project calls <c>RequestAsync</c>; the read is equal between edits, so the check after it stays cached.
    /// </remarks>
    private static void _RegisterRequestCallChecks(
        IncrementalGeneratorInitializationContext context,
        IncrementalValueProvider<MessagingRegistrationModel?> registrationModel
    )
    {
        var requestCalls = context
            .SyntaxProvider.CreateSyntaxProvider(RequestCallParser.IsCandidate, RequestCallParser.Parse)
            .WhereNotNull()
            .WithTrackingName(TrackingNames.RequestCalls)
            .Collect();

        var localResponders = registrationModel
            .Select(
                static (model, _) =>
                    model is null
                        ? EquatableArray<ResponderModel>.Empty
                        : model
                            .Consumers.SelectMany(registration => registration.Consumer.Responders)
                            .ToEquatableArray()
            )
            .WithTrackingName(TrackingNames.LocalResponders);

        var referencedResponders = requestCalls
            .Combine(context.CompilationProvider)
            .Select(
                static (source, cancellationToken) =>
                    source.Left.IsEmpty
                        ? EquatableArray<ResponderModel>.Empty
                        : RequestCallParser.GetReferencedResponders(source.Right, cancellationToken)
            )
            .WithTrackingName(TrackingNames.ReferencedResponders);

        context.RegisterDiagnosticsOutput(
            requestCalls
                .Combine(localResponders)
                .Combine(referencedResponders)
                .Select(
                    static (source, _) =>
                        RequestCallValidator.Validate(source.Left.Left, source.Left.Right, source.Right)
                )
                .WithTrackingName(TrackingNames.RequestDiagnostics)
        );
    }
}
