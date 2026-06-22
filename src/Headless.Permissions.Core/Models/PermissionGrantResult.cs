// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Permissions.Models;

/// <summary>
/// The resolved grant decision for a single permission, produced by the permission evaluation pipeline.
/// Carries both the final <see cref="Status"/> and the keys of the providers that contributed to the decision.
/// Construct via the static factory methods; the parameterless constructor is private to enforce invariants.
/// </summary>
public sealed class PermissionGrantResult
{
    private PermissionGrantResult() { }

    /// <summary>The resolved grant status for this permission.</summary>
    public required PermissionGrantStatus Status { get; init; }

    /// <summary>
    /// The provider keys (e.g. role names, user ids) whose grant records were involved in reaching
    /// <see cref="Status"/>. For <see cref="PermissionGrantStatus.Undefined"/> results the collection may be
    /// empty; for <see cref="PermissionGrantStatus.Granted"/> and <see cref="PermissionGrantStatus.Prohibited"/>
    /// it must contain at least one entry.
    /// </summary>
    public required IReadOnlyCollection<string> ProviderKeys { get; init; }

    /// <summary>Creates a result with <see cref="PermissionGrantStatus.Granted"/> status.</summary>
    /// <param name="providerKeys">Must not be null or empty.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="providerKeys"/> is empty.</exception>
    public static PermissionGrantResult Granted(IReadOnlyCollection<string> providerKeys)
    {
        Argument.IsNotNullOrEmpty(providerKeys);

        return new PermissionGrantResult { Status = PermissionGrantStatus.Granted, ProviderKeys = providerKeys };
    }

    /// <summary>Creates a result with <see cref="PermissionGrantStatus.Prohibited"/> status.</summary>
    /// <param name="providerKeys">Must not be null or empty.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="providerKeys"/> is empty.</exception>
    public static PermissionGrantResult Prohibited(IReadOnlyCollection<string> providerKeys)
    {
        Argument.IsNotNullOrEmpty(providerKeys);

        return new PermissionGrantResult { Status = PermissionGrantStatus.Prohibited, ProviderKeys = providerKeys };
    }

    /// <summary>Creates a result with <see cref="PermissionGrantStatus.Undefined"/> status (no decision made).</summary>
    /// <param name="providerKeys">The provider keys to associate; may be empty.</param>
    public static PermissionGrantResult Undefined(IReadOnlyCollection<string> providerKeys)
    {
        Argument.IsNotNull(providerKeys);

        return new PermissionGrantResult { Status = PermissionGrantStatus.Undefined, ProviderKeys = providerKeys };
    }
}
