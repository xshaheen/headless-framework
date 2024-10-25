// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Features.Models;
using Framework.Features.Values;
using Framework.Kernel.BuildingBlocks.Helpers.System;

namespace Framework.Features.ValueProviders;

public abstract class StoreFeatureValueProvider(IFeatureStore store) : IFeatureValueProvider
{
    protected IFeatureStore Store { get; } = store;

    public abstract string Name { get; }

    public virtual async Task<string?> GetOrDefaultAsync(
        FeatureDefinition feature,
        string? providerKey,
        CancellationToken cancellationToken = default
    )
    {
        var pk = await NormalizeProviderKeyAsync(providerKey, cancellationToken);

        return await Store.GetOrDefaultAsync(feature.Name, Name, pk, cancellationToken);
    }

    public virtual async Task SetAsync(
        FeatureDefinition feature,
        string value,
        string? providerKey,
        CancellationToken cancellationToken = default
    )
    {
        var pk = await NormalizeProviderKeyAsync(providerKey, cancellationToken);

        await Store.SetAsync(feature.Name, value, Name, pk, cancellationToken);
    }

    public virtual async Task ClearAsync(
        FeatureDefinition feature,
        string? providerKey,
        CancellationToken cancellationToken = default
    )
    {
        var pk = await NormalizeProviderKeyAsync(providerKey, cancellationToken);

        await Store.DeleteAsync(feature.Name, Name, pk, cancellationToken);
    }

    public virtual Task<IAsyncDisposable> HandleContextAsync(
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult<IAsyncDisposable>(NullAsyncDisposable.Instance);
    }

    protected virtual Task<string?> NormalizeProviderKeyAsync(
        string? providerKey,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(providerKey);
    }
}
