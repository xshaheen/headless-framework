// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Api.Security.Claims;
using Framework.Features.Providers;

namespace Framework.Features.FeatureManagement;

public class EditionFeatureManagementProvider : FeatureManagementProvider
{
    public override string Name => EditionFeatureValueProvider.ProviderName;

    protected ICurrentPrincipalAccessor PrincipalAccessor { get; }

    public EditionFeatureManagementProvider(IFeatureManagementStore store, ICurrentPrincipalAccessor principalAccessor)
        : base(store)
    {
        PrincipalAccessor = principalAccessor;
    }

    protected override Task<string?> NormalizeProviderKeyAsync(string? providerKey)
    {
        if (providerKey != null)
        {
            return Task.FromResult(providerKey);
        }

        return Task.FromResult(PrincipalAccessor.Principal?.FindEditionId()?.ToString("N"));
    }
}
