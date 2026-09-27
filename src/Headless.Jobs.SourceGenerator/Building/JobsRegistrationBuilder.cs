// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Immutable;
using Headless.Jobs.SourceGenerator.Models;
using Headless.Jobs.SourceGenerator.Utilities;
using Headless.Jobs.SourceGenerator.Validation;
using Headless.SourceGenerators;

namespace Headless.Jobs.SourceGenerator.Building;

/// <summary>
/// Combines the per-declaration results of one assembly into the emission model and the full diagnostic set. Only
/// cross-declaration rules live here: collisions, middleware target resolution, and duplicate middleware.
/// </summary>
internal static class JobsRegistrationBuilder
{
    public static JobsGenerationResult Build(
        ImmutableArray<JobFunctionResult> functions,
        EquatableArray<MiddlewareResult> middleware,
        EquatableArray<string> referencedFunctions,
        string assemblyName
    )
    {
        if (string.Equals(assemblyName, SourceGeneratorConstants.ExcludedAssemblyName, StringComparison.Ordinal))
        {
            return JobsGenerationResult.Empty;
        }

        // A collision makes every generated lookup ambiguous, so it is reported alone and nothing is emitted.
        var collisions = _FindCollisions(functions);
        if (collisions.Count > 0)
        {
            return new(Model: null, collisions.ToEquatableArray());
        }

        var diagnostics = new List<DiagnosticInfo>();
        foreach (var function in functions)
        {
            diagnostics.AddRange(function.Diagnostics);
        }

        var functionModels = functions.Select(function => function.Function).ToEquatableArray();
        var registrations = _ResolveMiddleware(
            functionModels,
            middleware,
            referencedFunctions,
            assemblyName,
            diagnostics
        );
        // An assembly that declares nothing gets no module: a public JobsModule type would otherwise appear in every
        // project that references Jobs.
        var model =
            functionModels.Count == 0 && registrations.Count == 0
                ? null
                : new JobsRegistrationModel(assemblyName, functionModels, registrations);

        // One class can hold several functions, so class-level diagnostics arrive once per function.
        return new(model, diagnostics.Distinct().ToEquatableArray());
    }

    private static List<DiagnosticInfo> _FindCollisions(ImmutableArray<JobFunctionResult> functions)
    {
        var functionNames = new HashSet<string>(StringComparer.Ordinal);
        var requestTypes = new HashSet<string>(StringComparer.Ordinal);
        var collisions = new List<DiagnosticInfo>();

        foreach (var result in functions)
        {
            var function = result.Function;
            if (!string.IsNullOrWhiteSpace(function.FunctionName) && !functionNames.Add(function.FunctionName!))
            {
                collisions.Add(
                    new(
                        DiagnosticDescriptors.DuplicateFunctionName,
                        result.AttributeLocation,
                        new EquatableArray<string>([function.FunctionName!])
                    )
                );
            }

            if (function.RequestTypeName is { } requestType && !requestTypes.Add(requestType))
            {
                collisions.Add(
                    new(
                        DiagnosticDescriptors.DuplicateRequestType,
                        result.AttributeLocation,
                        new EquatableArray<string>([requestType])
                    )
                );
            }
        }

        return collisions;
    }

    private static EquatableArray<MiddlewareRegistrationModel> _ResolveMiddleware(
        EquatableArray<JobFunctionModel> functions,
        EquatableArray<MiddlewareResult> middleware,
        EquatableArray<string> referencedFunctions,
        string assemblyName,
        List<DiagnosticInfo> diagnostics
    )
    {
        var ownFunctions = new HashSet<string?>(functions.Select(x => x.FunctionName), StringComparer.Ordinal);
        var referenced = new HashSet<string>(referencedFunctions, StringComparer.Ordinal);
        var candidates = new List<MiddlewareDeclarationModel>();

        // Assembly placement first, matching the order declarations are listed on the assembly symbol.
        foreach (var declaration in middleware.SelectMany(x => x.Declarations).OrderBy(x => x.Placement))
        {
            if (declaration is { Placement: MiddlewarePlacement.Assembly, Function: { } target })
            {
                if (ownFunctions.Contains(target))
                {
                    diagnostics.Add(
                        new(
                            DiagnosticDescriptors.LocalAssemblyMiddlewareTarget,
                            declaration.Location,
                            new EquatableArray<string>([target])
                        )
                    );
                    continue;
                }

                if (!referenced.Contains(target))
                {
                    diagnostics.Add(
                        new(
                            DiagnosticDescriptors.UnknownMiddlewareTarget,
                            declaration.Location,
                            new EquatableArray<string>([target])
                        )
                    );
                    continue;
                }
            }

            if (!declaration.IsAccessible)
            {
                diagnostics.Add(
                    new(
                        DiagnosticDescriptors.InaccessibleMiddlewareType,
                        declaration.Location,
                        new EquatableArray<string>([declaration.TypeDisplayName])
                    )
                );
                continue;
            }

            if (!declaration.ImplementsInterface)
            {
                // The generic attribute constraint already reports the actionable compiler diagnostic.
                continue;
            }

            candidates.Add(declaration);
        }

        foreach (var result in middleware)
        {
            diagnostics.AddRange(result.Diagnostics);
        }

        var registrations = new List<MiddlewareRegistrationModel>();
        var seen = new HashSet<(bool IsSchedule, string? Function, int Priority, string Identity)>();
        foreach (
            var candidate in candidates
                .OrderBy(x => x.IsSchedule ? 0 : 1)
                .ThenBy(x => x.Function, StringComparer.Ordinal)
                .ThenBy(x => x.Priority)
                .ThenBy(x => x.TypeIdentity, StringComparer.Ordinal)
                .ThenBy(x => x.Location?.FilePath ?? string.Empty, StringComparer.Ordinal)
                .ThenBy(x => x.Location?.TextSpan.Start ?? 0)
        )
        {
            var identity = $"{assemblyName}:{candidate.TypeIdentity}";
            if (!seen.Add((candidate.IsSchedule, candidate.Function, candidate.Priority, identity)))
            {
                diagnostics.Add(
                    new(
                        DiagnosticDescriptors.DuplicateMiddleware,
                        candidate.Location,
                        new EquatableArray<string>([identity])
                    )
                );
                continue;
            }

            registrations.Add(
                new(candidate.TypeName, identity, candidate.Function, candidate.Priority, candidate.IsSchedule)
            );
        }

        return registrations.ToEquatableArray();
    }
}
