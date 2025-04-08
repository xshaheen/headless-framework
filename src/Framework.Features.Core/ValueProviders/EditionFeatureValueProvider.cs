// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Claims;
using Framework.Abstractions;
using Framework.Features.Models;
using Framework.Features.Values;

namespace Framework.Features.ValueProviders;

[PublicAPI]
public sealed class EditionFeatureValueProvider(IFeatureValueStore store, ICurrentPrincipalAccessor principalAccessor)
    : StoreFeatureValueProvider(store)
{
    public const string ProviderName = FeatureValueProviderNames.Edition;

    public override string Name => ProviderName;

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

        return await Store.GetOrDefaultAsync(feature.Name, Name, editionId, cancellationToken);
    }

    protected override string? NormalizeProviderKey(string? providerKey)
    {
        var editionId = providerKey ?? principalAccessor.Principal.GetEditionId();

        return editionId;
    }
}
