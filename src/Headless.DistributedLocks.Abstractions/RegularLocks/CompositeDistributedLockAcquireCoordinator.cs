// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

/// <summary>
/// The mutex adapter over <see cref="CompositeAcquireCoordinator"/>: canonicalizes a resource-name set and supplies
/// <see cref="IDistributedLock.TryAcquireAsync"/> as the child-acquire delegate. All acquisition mechanics — the
/// shared budget, formation renewal, rollback, and loss linking — live in the shared coordinator.
/// </summary>
internal static class CompositeDistributedLockAcquireCoordinator
{
    internal static Task<CompositeAcquireResult> TryAcquireAsync(
        IDistributedLock provider,
        IEnumerable<string> resources,
        DistributedLockAcquireOptions? options,
        CancellationToken cancellationToken
    )
    {
        Argument.IsNotNull(provider);
        Argument.IsNotNull(resources);

        var canonicalResources = _MaterializeCanonicalResources(resources);

        var environment = CompositeAcquireEnvironment.From(provider);

        return CompositeAcquireCoordinator.TryAcquireAsync(
            canonicalResources,
            static resource => resource,
            (resource, childOptions, childToken) => provider.TryAcquireAsync(resource, childOptions, childToken),
            _GetCompositeResource,
            environment,
            options,
            cancellationToken
        );
    }

    /// <summary>
    /// Validates, deduplicates, and ordinal-sorts the requested resource names. Ordinal ordering is what prevents two
    /// composites over overlapping names from deadlocking against each other.
    /// </summary>
    private static string[] _MaterializeCanonicalResources(IEnumerable<string> resources)
    {
        var materialized = new List<string>();

        foreach (var resource in resources)
        {
            materialized.Add(Argument.IsNotNullOrWhiteSpace(resource));
        }

        Argument.IsNotEmpty(materialized, paramName: nameof(resources));

        return [.. materialized.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
    }

    /// <summary>
    /// Builds the composite's diagnostic identity. This name exists in no backend — never pass it to a by-resource
    /// provider API.
    /// </summary>
    private static string _GetCompositeResource(IReadOnlyList<string> canonicalResources)
    {
        return string.Join('+', canonicalResources);
    }
}
