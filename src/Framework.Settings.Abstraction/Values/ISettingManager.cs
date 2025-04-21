// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Settings.Models;

namespace Framework.Settings.Values;

public interface ISettingManager
{
    /// <summary>Get feature value by name.</summary>
    /// <param name="settingName">The feature name.</param>
    /// <param name="providerName">
    /// If the providerName isn't provided, it will get the value from the first provider that has the value
    /// by the order of the registered providers.
    /// </param>
    /// <param name="providerKey">
    /// If the providerKey isn't provided, it will get the value according to each value provider's logic.
    /// </param>
    /// <param name="fallback">Force the value finds fallback to other providers.</param>
    /// <param name="cancellationToken">The abort token.</param>
    /// <returns></returns>
    Task<string?> GetOrDefaultAsync(
        string settingName,
        string? providerName = null,
        string? providerKey = null,
        bool fallback = true,
        CancellationToken cancellationToken = default
    );

    Task<List<SettingValue>> GetAllAsync(
        string providerName,
        string? providerKey = null,
        bool fallback = true,
        CancellationToken cancellationToken = default
    );

    Task SetAsync(
        string settingName,
        string? value,
        string providerName,
        string? providerKey,
        bool forceToSet = false,
        CancellationToken cancellationToken = default
    );

    Task DeleteAsync(string providerName, string providerKey, CancellationToken cancellationToken = default);
}
