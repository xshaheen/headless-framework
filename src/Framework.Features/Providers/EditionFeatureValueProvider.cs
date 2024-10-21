// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using System.Security.Claims;
using Framework.Api.Security.Claims;
using Framework.Features.Models;
using Framework.Features.Values;

namespace Framework.Features.Providers;

[PublicAPI]
public sealed class EditionFeatureValueProvider(IFeatureStore store, ICurrentPrincipalAccessor principalAccessor)
    : FeatureValueProvider(store)
{
    public const string ProviderName = "Edition";

    public override string Name => ProviderName;

    public override async Task<string?> GetOrDefaultAsync(FeatureDefinition feature)
    {
        var editionId = principalAccessor.Principal.GetEditionId();

        if (editionId is null)
        {
            return null;
        }

        return await Store.GetOrDefaultAsync(feature.Name, Name, editionId);
    }
}
