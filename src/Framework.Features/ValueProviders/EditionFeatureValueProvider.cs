// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using System.Security.Claims;
using Framework.Api.Security.Claims;
using Framework.Features.FeatureManagement;
using Framework.Features.Models;
using Framework.Features.Values;

namespace Framework.Features.ValueProviders;

[PublicAPI]
public sealed class EditionFeatureValueProvider(
    IFeatureManagementStore store,
    ICurrentPrincipalAccessor principalAccessor
) : StoreFeatureValueProvider(store)
{
    public const string ProviderName = "Edition";

    public override string Name => ProviderName;

    public async Task<string?> GetOrDefaultAsync(FeatureDefinition feature)
    {
        var editionId = principalAccessor.Principal.GetEditionId();

        if (editionId is null)
        {
            return null;
        }

        return await Store.GetOrDefaultAsync(feature.Name, Name, editionId);
    }

    protected override Task<string?> NormalizeProviderKeyAsync(string? providerKey)
    {
        return Task.FromResult(providerKey ?? principalAccessor.Principal.GetEditionId());
    }
}
