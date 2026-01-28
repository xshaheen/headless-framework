// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Core;
using Headless.Features.Models;
using Headless.Features.Values;

namespace Headless.Features.ValueProviders;

public abstract class StoreFeatureValueProvider(IFeatureValueStore store) : IFeatureValueProvider
{
    protected IFeatureValueStore Store { get; } = store;

    public abstract string Name { get; }

    public virtual async Task<string?> GetOrDefaultAsync(
        FeatureDefinition feature,
        string? providerKey,
        CancellationToken cancellationToken = default
    )
    {
        var pk = NormalizeProviderKey(providerKey);

        return await Store.GetOrDefaultAsync(feature.Name, Name, pk, cancellationToken);
    }

    public virtual async Task SetAsync(
        FeatureDefinition feature,
        string value,
        string? providerKey,
        CancellationToken cancellationToken = default
    )
    {
        var pk = NormalizeProviderKey(providerKey);

        await Store.SetAsync(feature.Name, value, Name, pk, cancellationToken);
    }

    public virtual async Task ClearAsync(
        FeatureDefinition feature,
        string? providerKey,
        CancellationToken cancellationToken = default
    )
    {
        var pk = NormalizeProviderKey(providerKey);

        await Store.DeleteAsync(feature.Name, Name, pk, cancellationToken);
    }

    public virtual Task<IAsyncDisposable> HandleContextAsync(
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(DisposableFactory.EmptyAsync);
    }

    protected virtual string? NormalizeProviderKey(string? providerKey) => providerKey;
}
