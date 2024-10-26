// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using System.Security.Claims;
using Framework.Features.Models;
using Framework.Features.Values;
using Framework.Kernel.BuildingBlocks.Abstractions;

namespace Framework.Features.ValueProviders;

[PublicAPI]
public sealed class EditionFeatureValueProvider(IFeatureValueStore store, ICurrentPrincipalAccessor principalAccessor)
    : StoreFeatureValueProvider(store)
{
    public const string ProviderName = "Edition";

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

    protected override Task<string?> NormalizeProviderKeyAsync(
        string? providerKey,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(providerKey ?? principalAccessor.Principal.GetEditionId());
    }
}
