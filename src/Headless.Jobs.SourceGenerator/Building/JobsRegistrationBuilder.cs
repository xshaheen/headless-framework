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
        ImmutableArray<JobResult> jobs,
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
        var collisions = _FindCollisions(jobs);
        if (collisions.Count > 0)
        {
            return new(Model: null, collisions.ToEquatableArray());
        }

        var diagnostics = new List<DiagnosticInfo>();
        foreach (var job in jobs)
        {
            diagnostics.AddRange(job.Diagnostics);
        }

        var jobModels = jobs.Where(job => job.Job is not null).Select(job => job.Job!).ToEquatableArray();
        var registrations = _ResolveMiddleware(jobs, middleware, referencedFunctions, assemblyName, diagnostics);
        // An assembly that declares nothing gets no module: a public JobsModule type would otherwise appear in every
        // project that references Jobs.
        var model =
            jobModels.Count == 0 && registrations.Count == 0
                ? null
                : new JobsRegistrationModel(assemblyName, jobModels, registrations);

        return new(model, diagnostics.Distinct().ToEquatableArray());
    }

    /// <summary>
    /// Finds identities and argument types declared by more than one job. Scheduling resolves a job from its argument
    /// type, so a shared argument type is as ambiguous as a shared identity.
    /// </summary>
    private static List<DiagnosticInfo> _FindCollisions(ImmutableArray<JobResult> jobs)
    {
        var identities = new HashSet<string>(StringComparer.Ordinal);
        var argsTypes = new HashSet<string>(StringComparer.Ordinal);
        var collisions = new List<DiagnosticInfo>();

        // Source order is not stable across partial edits, so the second declaration is chosen by location.
        foreach (
            var result in jobs.OrderBy(x => x.AttributeLocation?.FilePath ?? string.Empty, StringComparer.Ordinal)
                .ThenBy(x => x.AttributeLocation?.TextSpan.Start ?? 0)
        )
        {
            if (HandlerIdentity.IsValid(result.Identity) && !identities.Add(result.Identity!))
            {
                collisions.Add(
                    new(
                        DiagnosticDescriptors.DuplicateJobIdentity,
                        result.AttributeLocation,
                        new EquatableArray<string>([result.Identity!])
                    )
                );
            }

            if (result.ArgsTypeName is { } argsType && !argsTypes.Add(argsType))
            {
                collisions.Add(
                    new(
                        DiagnosticDescriptors.DuplicateArgumentType,
                        result.AttributeLocation,
                        new EquatableArray<string>([argsType])
                    )
                );
            }
        }

        return collisions;
    }

    private static EquatableArray<MiddlewareRegistrationModel> _ResolveMiddleware(
        ImmutableArray<JobResult> jobs,
        EquatableArray<MiddlewareResult> middleware,
        EquatableArray<string> referencedFunctions,
        string assemblyName,
        List<DiagnosticInfo> diagnostics
    )
    {
        var ownFunctions = new HashSet<string?>(jobs.Select(x => x.Identity), StringComparer.Ordinal);
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
