// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Checks;
using Headless.Features.Definitions;
using Headless.Features.Values;
using Headless.MultiTenancy;

namespace Headless.Features.ClientVisibility;

/// <summary>Default <see cref="IClientVisibleFeaturesReader"/>.</summary>
internal sealed class ClientVisibleFeaturesReader(
    IFeatureDefinitionManager definitionManager,
    IFeatureManager featureManager,
    ICurrentPrincipalAccessor principalAccessor,
    ICurrentTenant currentTenant
) : IClientVisibleFeaturesReader
{
    public async Task<IReadOnlyDictionary<string, string?>> GetAsync(
        PrincipalContext context,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(context);

        // The value providers read the ambient tenant and principal, so resolve under the context's identity.
        using var principalScope = principalAccessor.Change(context.Principal);
        using var tenantScope = currentTenant.Change(context.TenantId);

        var definitions = await definitionManager.GetFeaturesAsync(cancellationToken).ConfigureAwait(false);
        var names = definitions
            .Where(definition => definition.IsVisibleToClients)
            .Select(definition => definition.Name)
            .ToHashSet(StringComparer.Ordinal);

        var values = await featureManager.GetAllAsync(names, cancellationToken).ConfigureAwait(false);

        return values.ToDictionary(pair => pair.Key, pair => pair.Value.Value, StringComparer.Ordinal);
    }
}
