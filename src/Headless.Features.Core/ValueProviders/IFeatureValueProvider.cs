// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Features.Models;

namespace Headless.Features.ValueProviders;

/// <summary>Read-write contract for a feature value provider; extends <see cref="IFeatureValueReadProvider"/> with mutation operations.</summary>
public interface IFeatureValueProvider : IFeatureValueReadProvider
{
    /// <summary>Persists <paramref name="value"/> for <paramref name="feature"/> under <paramref name="providerKey"/>.</summary>
    /// <param name="feature">The feature definition whose value is being set.</param>
    /// <param name="value">The new value to store.</param>
    /// <param name="providerKey">An optional key that qualifies the scope (e.g. tenant or edition identifier).</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task SetAsync(
        FeatureDefinition feature,
        string value,
        string? providerKey,
        CancellationToken cancellationToken = default
    );

    /// <summary>Removes the stored value of <paramref name="feature"/> for <paramref name="providerKey"/>, reverting to the fallback value.</summary>
    /// <param name="feature">The feature definition whose value should be cleared.</param>
    /// <param name="providerKey">An optional key that qualifies the scope.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task ClearAsync(FeatureDefinition feature, string? providerKey, CancellationToken cancellationToken = default);

    /// <summary>Stores or clears several values for <paramref name="providerKey"/> as one write.</summary>
    /// <remarks>
    /// <c>FeatureManager.SetAsync</c> routes every write through this member. The default implementation calls
    /// <see cref="SetAsync"/> and <see cref="ClearAsync"/> once per entry, so a failure part-way leaves the earlier
    /// entries written; override it when the backing source can apply the whole batch atomically, as
    /// <see cref="StoreFeatureValueProvider"/> does.
    /// </remarks>
    /// <param name="values">The values to write, each paired with its definition. A <see langword="null"/> value clears the feature.</param>
    /// <param name="providerKey">An optional key that qualifies the scope (e.g. tenant or edition identifier).</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    async Task SetAllAsync(
        IReadOnlyList<KeyValuePair<FeatureDefinition, string?>> values,
        string? providerKey,
        CancellationToken cancellationToken = default
    )
    {
        foreach (var (feature, value) in values)
        {
            if (value is null)
            {
                await ClearAsync(feature, providerKey, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await SetAsync(feature, value, providerKey, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
