// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Claims;
using Headless.Context;
using Headless.MultiTenancy;

namespace Headless.Features;

/// <summary>
/// Feature value provider that resolves values scoped to the edition of the ambient tenant: the edition claim on the
/// current <see cref="ClaimsPrincipal"/> when the principal belongs to that tenant, otherwise the registered
/// <see cref="ITenantEditionResolver"/>.
/// </summary>
/// <remarks>
/// The claim describes the caller's own tenant. It is not used for a tenant the feature manager switched to on the
/// caller's behalf (for example <c>SetForTenantAsync</c> or <c>GetForTenantAsync</c> from a host administrator), and
/// a job or message consumer that restores only the tenant has no principal at all. Both resolve through
/// <see cref="ITenantEditionResolver"/>; without one registered, they see no edition value.
/// </remarks>
[PublicAPI]
public sealed class EditionFeatureValueProvider(
    IFeatureValueStore store,
    ICurrentPrincipalAccessor principalAccessor,
    ICurrentTenant currentTenant,
    ITenantEditionResolver? editionResolver = null
) : StoreFeatureValueProvider(store)
{
    /// <summary>The well-known name used to identify this provider in the provider chain.</summary>
    public const string ProviderName = FeatureValueProviderNames.Edition;

    /// <inheritdoc/>
    public override string Name => ProviderName;

    /// <summary>
    /// Returns the feature value for the ambient tenant's edition. Returns <see langword="null"/> when no edition
    /// resolves for it.
    /// </summary>
    /// <param name="feature">The feature definition to look up.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The stored string value for the edition, or <see langword="null"/> if not set or no edition is active.</returns>
    public async Task<string?> GetOrDefaultAsync(
        FeatureDefinition feature,
        CancellationToken cancellationToken = default
    )
    {
        var edition = await ResolveCurrentEditionAsync(cancellationToken).ConfigureAwait(false);

        if (edition.EditionId is null)
        {
            return null;
        }

        return await Store
            .GetOrDefaultAsync(feature.Name, Name, edition.EditionId, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Returns <paramref name="providerKey"/> when it is non-null; otherwise the edition of the ambient tenant.
    /// </summary>
    /// <param name="providerKey">The explicit provider key supplied by the caller, or <see langword="null"/> to resolve from the ambient tenant.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The edition identifier to use in store operations, or <see langword="null"/> when none is available.</returns>
    protected override async ValueTask<string?> NormalizeProviderKeyAsync(
        string? providerKey,
        CancellationToken cancellationToken
    )
    {
        if (providerKey is not null)
        {
            return providerKey;
        }

        var edition = await ResolveCurrentEditionAsync(cancellationToken).ConfigureAwait(false);

        return edition.EditionId;
    }

    /// <summary>
    /// Resolves the ambient tenant's edition and says whether the answer is known: an edition the principal or the
    /// resolver supplied, or a confirmed absence of one, as opposed to no source that could tell.
    /// </summary>
    internal async ValueTask<EditionResolution> ResolveCurrentEditionAsync(CancellationToken cancellationToken)
    {
        var tenantId = currentTenant.Id;
        var principal = principalAccessor.Principal;
        var principalDescribesTenant =
            principal?.Identity is not null
            && (tenantId is null || !string.Equals(FeatureTenantSwitch.SwitchedTo, tenantId, StringComparison.Ordinal))
            && (
                principal.GetTenantId() is not { } claimTenant
                || string.Equals(claimTenant, tenantId, StringComparison.Ordinal)
            );

        var claimEdition = principalDescribesTenant ? principal!.GetEditionId() : null;

        if (claimEdition is not null)
        {
            return new(claimEdition, IsKnown: true);
        }

        if (tenantId is not null && editionResolver is not null)
        {
            var resolved = await editionResolver.GetEditionIdAsync(tenantId, cancellationToken).ConfigureAwait(false);

            return new(resolved, IsKnown: true);
        }

        // No tenant: the host has no edition. A principal of this tenant without the claim: the tenant has none.
        return new(EditionId: null, IsKnown: tenantId is null || principalDescribesTenant);
    }
}

/// <summary>The edition resolved for the ambient tenant, and whether any source could establish it.</summary>
internal readonly record struct EditionResolution(string? EditionId, bool IsKnown);
