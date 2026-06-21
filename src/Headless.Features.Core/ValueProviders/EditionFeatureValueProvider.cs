// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Claims;
using Headless.Abstractions;
using Headless.Features.Models;
using Headless.Features.Values;

namespace Headless.Features.ValueProviders;

/// <summary>
/// Feature value provider that resolves values scoped to the current user's edition, derived from
/// the active <see cref="ClaimsPrincipal"/> via <see cref="ICurrentPrincipalAccessor"/>.
/// </summary>
[PublicAPI]
public sealed class EditionFeatureValueProvider(IFeatureValueStore store, ICurrentPrincipalAccessor principalAccessor)
    : StoreFeatureValueProvider(store)
{
    /// <summary>The well-known name used to identify this provider in the provider chain.</summary>
    public const string ProviderName = FeatureValueProviderNames.Edition;

    /// <inheritdoc/>
    public override string Name => ProviderName;

    /// <summary>
    /// Returns the feature value for the current user's edition. Returns <see langword="null"/> when no edition claim
    /// is present on the current principal.
    /// </summary>
    /// <param name="feature">The feature definition to look up.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The stored string value for the edition, or <see langword="null"/> if not set or no edition is active.</returns>
    public async Task<string?> GetOrDefaultAsync(
        FeatureDefinition feature,
        CancellationToken cancellationToken = default
    )
    {
        var editionId = principalAccessor.Principal.GetEditionId();

        if (editionId is null)
        {
            return null;
        }

        return await Store.GetOrDefaultAsync(feature.Name, Name, editionId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Returns <paramref name="providerKey"/> when it is non-null; otherwise falls back to the edition identifier
    /// on the current principal.
    /// </summary>
    /// <param name="providerKey">The explicit provider key supplied by the caller, or <see langword="null"/> to resolve from the current principal.</param>
    /// <returns>The edition identifier to use in store operations, or <see langword="null"/> when none is available.</returns>
    protected override string? NormalizeProviderKey(string? providerKey)
    {
        var editionId = providerKey ?? principalAccessor.Principal.GetEditionId();

        return editionId;
    }
}
