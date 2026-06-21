// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Features.Models;

namespace Headless.Features.ValueProviders;

/// <summary>Read-only contract for a feature value provider that can resolve and contextually scope feature values.</summary>
public interface IFeatureValueReadProvider
{
    /// <summary>Gets the unique name that identifies this provider (e.g. <c>"Tenant"</c>, <c>"Edition"</c>).</summary>
    string Name { get; }

    /// <summary>
    /// Applies provider-specific context (e.g. switches the active tenant) for the duration of the returned
    /// <see cref="IAsyncDisposable"/>, then restores the original context on disposal.
    /// </summary>
    /// <param name="providerName">The name of the provider for which context should be applied.</param>
    /// <param name="providerKey">An optional key that qualifies the context (e.g. a tenant identifier).</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>An <see cref="IAsyncDisposable"/> that reverts the context change when disposed.</returns>
    Task<IAsyncDisposable> HandleContextAsync(
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    );

    /// <summary>Returns the raw string value of <paramref name="feature"/> for the given <paramref name="providerKey"/>, or <see langword="null"/> if not set.</summary>
    /// <param name="feature">The feature definition to look up.</param>
    /// <param name="providerKey">An optional key that qualifies the scope (e.g. tenant or edition identifier).</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The stored string value, or <see langword="null"/> if this provider has no value for the feature.</returns>
    Task<string?> GetOrDefaultAsync(
        FeatureDefinition feature,
        string? providerKey,
        CancellationToken cancellationToken = default
    );
}

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
}
