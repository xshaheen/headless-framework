// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Features.Models;
using Framework.Features.Values;
using Framework.Kernel.BuildingBlocks.Helpers.System;

namespace Framework.Features.ValueProviders;

public abstract class StoreFeatureValueProvider(IFeatureManagementStore store) : IFeatureValueProvider
{
    protected IFeatureManagementStore Store { get; } = store;

    public abstract string Name { get; }

    public bool Compatible(string providerName)
    {
        return string.Equals(providerName, Name, StringComparison.Ordinal);
    }

    public virtual async Task<string?> GetOrDefaultAsync(FeatureDefinition feature, string? providerKey)
    {
        return await Store.GetOrDefaultAsync(feature.Name, Name, await NormalizeProviderKeyAsync(providerKey));
    }

    public virtual async Task SetAsync(FeatureDefinition feature, string value, string? providerKey)
    {
        await Store.SetAsync(feature.Name, value, Name, await NormalizeProviderKeyAsync(providerKey));
    }

    public virtual async Task ClearAsync(FeatureDefinition feature, string? providerKey)
    {
        await Store.DeleteAsync(feature.Name, Name, await NormalizeProviderKeyAsync(providerKey));
    }

    public virtual Task<IAsyncDisposable> HandleContextAsync(string providerName, string providerKey)
    {
        return Task.FromResult<IAsyncDisposable>(NullAsyncDisposable.Instance);
    }

    protected virtual Task<string?> NormalizeProviderKeyAsync(string? providerKey)
    {
        return Task.FromResult(providerKey);
    }
}
