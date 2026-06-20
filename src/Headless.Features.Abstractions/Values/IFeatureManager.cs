// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Features.Models;

namespace Headless.Features.Values;

/// <summary>Reads and writes feature values across one or more value providers.</summary>
public interface IFeatureManager
{
    /// <summary>Gets the value of a feature by name.</summary>
    /// <param name="name">The feature name.</param>
    /// <param name="providerName">
    /// The provider to query. When <see langword="null"/>, the value is read from the first provider
    /// that has a value, in registration order.
    /// </param>
    /// <param name="providerKey">
    /// Provider-specific key (e.g., tenant ID). When <see langword="null"/>, each provider uses its own default logic.
    /// </param>
    /// <param name="fallback">
    /// When <see langword="true"/>, continues checking subsequent providers in registration order when the specified provider
    /// has no value. When <see langword="false"/> and <paramref name="providerName"/> is not <see langword="null"/>,
    /// only the specified provider is queried.
    /// </param>
    /// <param name="cancellationToken">The abort token.</param>
    /// <returns>A <see cref="FeatureValue"/> containing the resolved value and the provider that supplied it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="Headless.Exceptions.ConflictException">
    /// The feature is not defined, the specified provider is not registered, or the provider is read-only
    /// and <paramref name="fallback"/> is <see langword="false"/>.
    /// </exception>
    Task<FeatureValue> GetAsync(
        string name,
        string? providerName = null,
        string? providerKey = null,
        bool fallback = true,
        CancellationToken cancellationToken = default
    );

    /// <summary>Gets all feature values for the given provider and key.</summary>
    /// <param name="providerName">The provider to query.</param>
    /// <param name="providerKey">Provider-specific key. When <see langword="null"/>, the provider uses its default logic.</param>
    /// <param name="fallback">When <see langword="true"/>, falls back to subsequent providers for features with no value in <paramref name="providerName"/>.</param>
    /// <param name="cancellationToken">The abort token.</param>
    /// <returns>A list of <see cref="FeatureValue"/> instances — one per registered feature.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="providerName"/> is <see langword="null"/>.</exception>
    /// <exception cref="Headless.Exceptions.ConflictException">The specified provider is not registered.</exception>
    Task<List<FeatureValue>> GetAllAsync(
        string providerName,
        string? providerKey = null,
        bool fallback = true,
        CancellationToken cancellationToken = default
    );

    /// <summary>Sets the value of a feature for the given provider and key.</summary>
    /// <param name="name">The feature name.</param>
    /// <param name="value">The value to store, or <see langword="null"/> to clear it.</param>
    /// <param name="providerName">The provider to write the value to.</param>
    /// <param name="providerKey">Provider-specific key (e.g., tenant ID or edition ID).</param>
    /// <param name="forceToSet">
    /// When <see langword="false"/> and <paramref name="value"/> matches the fallback value, the write is skipped.
    /// When <see langword="true"/>, the value is always written.
    /// </param>
    /// <param name="cancellationToken">The abort token.</param>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> or <paramref name="providerName"/> is <see langword="null"/>.</exception>
    /// <exception cref="Headless.Exceptions.ConflictException">
    /// The feature is not defined, the specified provider is not registered, or the provider is read-only.
    /// </exception>
    Task SetAsync(
        string name,
        string? value,
        string providerName,
        string? providerKey,
        bool forceToSet = false,
        CancellationToken cancellationToken = default
    );

    /// <summary>Deletes all feature values for the given provider and key.</summary>
    /// <param name="providerName">The provider whose values should be deleted.</param>
    /// <param name="providerKey">The provider-specific key identifying the scope to clear (e.g., a tenant ID).</param>
    /// <param name="cancellationToken">The abort token.</param>
    /// <exception cref="ArgumentNullException"><paramref name="providerName"/> or <paramref name="providerKey"/> is <see langword="null"/>.</exception>
    /// <exception cref="Headless.Exceptions.ConflictException">The specified provider is not registered or is read-only.</exception>
    Task DeleteAsync(string providerName, string providerKey, CancellationToken cancellationToken = default);
}
