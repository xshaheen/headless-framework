// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Features.Models;

namespace Headless.Features.Values;

public interface IFeatureManager
{
    /// <summary>Get feature value by name.</summary>
    /// <param name="name">The feature name.</param>
    /// <param name="providerName">
    /// If the providerName isn't provided, it will get the value from the first provider that has the value
    /// by the order of the registered providers.
    /// </param>
    /// <param name="providerKey">
    /// If the providerKey isn't provided, it will get the value according to each value provider's logic.
    /// </param>
    /// <param name="fallback">Force the value finds fallback to other providers based on the order of the registered providers.</param>
    /// <param name="cancellationToken">The abort token.</param>
    Task<FeatureValue> GetAsync(
        string name,
        string? providerName = null,
        string? providerKey = null,
        bool fallback = true,
        CancellationToken cancellationToken = default
    );

    /// <summary>Get all feature values by providerName and providerKey.</summary>
    /// <param name="providerName">
    /// If the providerName isn't provided, it will get the value from the first provider that has the value
    /// by the order of the registered providers.
    /// </param>
    /// <param name="providerKey">
    /// If the providerKey isn't provided, it will get the value according to each value provider's logic.
    /// </param>
    /// <param name="fallback">Force the value finds fallback to other providers based on the order of the registered providers.</param>
    /// <param name="cancellationToken">The abort token.</param>
    Task<List<FeatureValue>> GetAllAsync(
        string providerName,
        string? providerKey = null,
        bool fallback = true,
        CancellationToken cancellationToken = default
    );

    /// <summary>Set feature value by name.</summary>
    /// <param name="forceToSet">
    /// When <see langword="true"/> and the value is same as the fallback value, it will not set the value
    /// otherwise it will set the value even if it is same as the fallback value.
    /// </param>
    Task SetAsync(
        string name,
        string? value,
        string providerName,
        string? providerKey,
        bool forceToSet = false,
        CancellationToken cancellationToken = default
    );

    /// <summary>Delete feature value from a specific providerName and providerKey.</summary>
    Task DeleteAsync(string providerName, string providerKey, CancellationToken cancellationToken = default);
}
