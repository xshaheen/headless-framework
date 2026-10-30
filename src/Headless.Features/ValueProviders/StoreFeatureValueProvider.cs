// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.UnitOfWork;

namespace Headless.Features;

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
    public virtual async Task<List<FeatureValue>> GetAllAsync(
        FeatureDefinition[] features,
        string? providerKey = null,
        CancellationToken cancellationToken = default
    )
    {
        var names = features.Select(static x => x.Name).ToHashSet(StringComparer.Ordinal);

        return await Store
            .GetAllAsync(names, Name, NormalizeProviderKey(providerKey), cancellationToken)
            .ConfigureAwait(false);
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
    /// <remarks>
    /// Writes straight to <see cref="IFeatureValueStore.SetAllAsync"/> without calling <see cref="SetAsync"/> or
    /// <see cref="ClearAsync"/>, so a subclass that overrides either of those must override this member as well.
    /// </remarks>
    public virtual async Task SetAllAsync(
        IReadOnlyList<KeyValuePair<FeatureDefinition, string?>> values,
        string? providerKey,
        CancellationToken cancellationToken = default
    )
    {
        var valuesByName = new Dictionary<string, string?>(values.Count, StringComparer.Ordinal);

        foreach (var (feature, value) in values)
        {
            valuesByName[feature.Name] = value;
        }

        var pk = NormalizeProviderKey(providerKey);

        await Store.SetAllAsync(valuesByName, Name, pk, cancellationToken).ConfigureAwait(false);
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

    /// <inheritdoc/>
    public virtual Task<string?> GetOrDefaultEnlistedAsync(
        IUnitOfWork unitOfWork,
        FeatureDefinition feature,
        string? providerKey,
        CancellationToken cancellationToken
    )
    {
        return Store.GetOrDefaultEnlistedAsync(
            unitOfWork,
            feature.Name,
            Name,
            NormalizeProviderKey(providerKey),
            cancellationToken
        );
    }

    /// <inheritdoc/>
    public virtual Task SetAllEnlistedAsync(
        IUnitOfWork unitOfWork,
        IReadOnlyList<KeyValuePair<FeatureDefinition, string?>> values,
        string? providerKey,
        CancellationToken cancellationToken
    )
    {
        var valuesByName = new Dictionary<string, string?>(values.Count, StringComparer.Ordinal);

        foreach (var (feature, value) in values)
        {
            valuesByName[feature.Name] = value;
        }

        return Store.SetAllEnlistedAsync(
            unitOfWork,
            valuesByName,
            Name,
            NormalizeProviderKey(providerKey),
            cancellationToken
        );
    }

    /// <inheritdoc/>
    public virtual Task<IReadOnlyList<string>> DeleteAllEnlistedAsync(
        IUnitOfWork unitOfWork,
        string? providerKey,
        CancellationToken cancellationToken
    )
    {
        return Store.DeleteEnlistedAsync(unitOfWork, Name, NormalizeProviderKey(providerKey), cancellationToken);
    }

    /// <summary>Transforms the caller-supplied <paramref name="providerKey"/> before passing it to the store. Returns <paramref name="providerKey"/> unchanged by default.</summary>
    /// <param name="providerKey">The raw provider key supplied by the caller.</param>
    /// <returns>The normalized key to use in store operations, or <see langword="null"/>.</returns>
    protected virtual string? NormalizeProviderKey(string? providerKey)
    {
        return providerKey;
    }
}
