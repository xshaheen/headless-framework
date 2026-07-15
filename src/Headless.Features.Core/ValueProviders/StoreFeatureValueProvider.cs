// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Core;
using Headless.Features.Models;
using Headless.Features.Values;

namespace Headless.Features.ValueProviders;

/// <summary>
/// Base class for <see cref="IFeatureValueProvider"/> implementations that delegate persistence to an
/// <see cref="IFeatureValueStore"/>. Subclasses override <see cref="Name"/> and may override
/// <see cref="NormalizeProviderKey"/> to adapt the incoming key before storage lookups.
/// </summary>
public abstract class StoreFeatureValueProvider(IFeatureValueStore store) : IFeatureValueProvider
{
    /// <summary>Gets the underlying feature value store used for persistence.</summary>
    protected IFeatureValueStore Store { get; } = store;

    /// <inheritdoc/>
    public abstract string Name { get; }

    /// <inheritdoc/>
    public virtual async Task<string?> GetOrDefaultAsync(
        FeatureDefinition feature,
        string? providerKey,
        CancellationToken cancellationToken = default
    )
    {
        var pk = NormalizeProviderKey(providerKey);

        return await Store.GetOrDefaultAsync(feature.Name, Name, pk, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public virtual async Task SetAsync(
        FeatureDefinition feature,
        string value,
        string? providerKey,
        CancellationToken cancellationToken = default
    )
    {
        var pk = NormalizeProviderKey(providerKey);

        await Store.SetAsync(feature.Name, value, Name, pk, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public virtual async Task ClearAsync(
        FeatureDefinition feature,
        string? providerKey,
        CancellationToken cancellationToken = default
    )
    {
        var pk = NormalizeProviderKey(providerKey);

        await Store.DeleteAsync(feature.Name, Name, pk, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> is cancelled.</exception>
    public virtual Task<IAsyncDisposable> HandleContextAsync(
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(DisposableFactory.EmptyAsync);
    }

    /// <summary>Transforms the caller-supplied <paramref name="providerKey"/> before passing it to the store. Returns <paramref name="providerKey"/> unchanged by default.</summary>
    /// <param name="providerKey">The raw provider key supplied by the caller.</param>
    /// <returns>The normalized key to use in store operations, or <see langword="null"/>.</returns>
    protected virtual string? NormalizeProviderKey(string? providerKey)
    {
        return providerKey;
    }
}
