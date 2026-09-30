// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text;
using Headless.Generator.ProviderSetup.Emitting;
using Headless.Generator.ProviderSetup.Models;
using Headless.Generator.ProviderSetup.Parsing;
using Headless.Generator.ProviderSetup.Utilities;
using Headless.Generator.ProviderSetup.Validation;
using Headless.SourceGenerators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Headless.Generator.ProviderSetup;

/// <summary>
/// Roslyn incremental source generator that discovers options classes annotated with
/// <c>[GenerateProviderSetup]</c> and emits their registration surface: the <c>Use{Provider}</c> overload trios
/// (default and named), options wiring with FluentValidation startup validation, the named HttpClient whose
/// resilience pipeline is derived from the declared <c>[OutboundEffect]</c>, and the default/keyed sender
/// registrations.
/// </summary>
/// <remarks>
/// Declarations are found with <c>ForAttributeWithMetadataName</c> and reduced to value-equal models in the
/// transform, so every later step compares by value and is skipped when an edit does not change what it produces.
/// One source file is emitted per attributed options class.
/// </remarks>
[Generator]
public sealed class ProviderSetupGenerator : IIncrementalGenerator
{
    /// <summary>Tracking names of the pipeline steps; tests assert these stay cached on unrelated edits.</summary>
    internal static class TrackingNames
    {
        public const string ProviderOptions = nameof(ProviderOptions);
        public const string GenerationResult = nameof(GenerationResult);
        public const string Models = nameof(Models);
        public const string Diagnostics = nameof(Diagnostics);
        public const string ClientOptions = nameof(ClientOptions);
        public const string ClientDiagnostics = nameof(ClientDiagnostics);
    }

    /// <summary>Registers the incremental pipeline.</summary>
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var providerOptions = context
            .SyntaxProvider.ForAttributeWithMetadataName(
                GeneratorConstants.AttributeMetadataName,
                ProviderSetupParser.IsCandidate,
                ProviderSetupParser.Parse
            )
            .Where(static result => result is not null)
            .Select(static (result, _) => result!)
            .WithTrackingName(TrackingNames.ProviderOptions);

        var generation = providerOptions
            .Collect()
            .Select(
                static (results, _) =>
                {
                    // Cross-declaration rule: one Use{Provider} method name per assembly, so two attributed
                    // options classes can never emit colliding extension members. A duplicate is reported and not
                    // emitted: a second file with the same hint name would crash the generator instead.
                    var diagnostics = new List<DiagnosticInfo>();
                    var models = new List<ProviderSetupModel>();
                    var seenNames = new HashSet<string>(StringComparer.Ordinal);

                    foreach (var result in results)
                    {
                        diagnostics.AddRange(result.Diagnostics);

                        if (result.Model is not { } model)
                        {
                            continue;
                        }

                        if (seenNames.Add(model.UseMethodName))
                        {
                            models.Add(model);
                        }
                        else
                        {
                            diagnostics.Add(
                                new DiagnosticInfo(
                                    Descriptors.DuplicateUseMethod,
                                    result.AttributeLocation,
                                    new[] { model.UseMethodName }.ToEquatableArray()
                                )
                            );
                        }
                    }

                    return (Models: models.ToEquatableArray(), Diagnostics: diagnostics.ToEquatableArray());
                }
            )
            .WithTrackingName(TrackingNames.GenerationResult);

        // Models only, without locations, so an edit that just moves an options class never re-emits source.
        var models = generation
            .Select(static (generation, _) => generation.Models)
            .WithTrackingName(TrackingNames.Models);

        context.RegisterSourceOutput(
            models,
            static (productionContext, models) =>
            {
                foreach (var model in models)
                {
                    productionContext.AddSource(
                        $"Setup{ProviderSetupEmitter.GetProviderName(model)}.g.cs",
                        SourceText.From(ProviderSetupEmitter.Emit(model), Encoding.UTF8)
                    );
                }
            }
        );

        context.RegisterDiagnosticsOutput(
            generation.Select(static (result, _) => result.Diagnostics).WithTrackingName(TrackingNames.Diagnostics)
        );

        // Single-backend client packages: one file per [GenerateClientSetup] options type.
        var clientOptions = context
            .SyntaxProvider.ForAttributeWithMetadataName(
                GeneratorConstants.ClientAttributeMetadataName,
                ClientSetupParser.IsCandidate,
                ClientSetupParser.Parse
            )
            .Where(static result => result is not null)
            .Select(static (result, _) => result!)
            .WithTrackingName(TrackingNames.ClientOptions);

        context.RegisterSourceOutput(
            clientOptions.Where(static result => result.Model is not null).Select(static (result, _) => result.Model!),
            static (productionContext, model) =>
                productionContext.AddSource(
                    $"Setup{ClientSetupEmitter.GetFeatureName(model)}.g.cs",
                    SourceText.From(ClientSetupEmitter.Emit(model), Encoding.UTF8)
                )
        );

        context.RegisterDiagnosticsOutput(
            clientOptions
                .Collect()
                .Select(static (results, _) => results.SelectMany(static r => r.Diagnostics).ToEquatableArray())
                .WithTrackingName(TrackingNames.ClientDiagnostics)
        );
    }
}
