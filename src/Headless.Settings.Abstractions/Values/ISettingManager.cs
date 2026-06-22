// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Settings.Models;

namespace Headless.Settings.Values;

/// <summary>Reads and writes setting values across one or more value providers.</summary>
public interface ISettingManager
{
    /// <summary>Returns the value of a named setting from the configured providers.</summary>
    /// <param name="settingName">The unique name of the setting.</param>
    /// <param name="providerName">
    /// The name of the provider to query. When <see langword="null"/>, the value is retrieved from
    /// the first provider (in registration order) that has a value for the setting.
    /// </param>
    /// <param name="providerKey">
    /// A provider-specific discriminator (e.g. a tenant ID or user ID). When <see langword="null"/>,
    /// each provider applies its own default key resolution logic.
    /// </param>
    /// <param name="fallback">
    /// When <see langword="true"/>, the lookup falls back to subsequent providers if the selected
    /// provider has no value for the setting.
    /// </param>
    /// <param name="cancellationToken">The abort token.</param>
    /// <returns>The setting value string, or <see langword="null"/> if no provider has a value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="settingName"/> is <see langword="null"/>.</exception>
    Task<string?> FindAsync(
        string settingName,
        string? providerName = null,
        string? providerKey = null,
        bool fallback = true,
        CancellationToken cancellationToken = default
    );

    /// <summary>Returns the current values for a set of named settings.</summary>
    /// <param name="settingNames">The set of setting names to resolve.</param>
    /// <param name="cancellationToken">The abort token.</param>
    /// <returns>
    /// A dictionary keyed by setting name. Settings that have no value still appear in the
    /// dictionary with a <see cref="SettingValue"/> whose <see cref="SettingValue.Value"/> is
    /// <see langword="null"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="settingNames"/> is <see langword="null"/>.</exception>
    Task<Dictionary<string, SettingValue>> GetAllAsync(
        HashSet<string> settingNames,
        CancellationToken cancellationToken = default
    );

    /// <summary>Returns all setting values from a specific provider.</summary>
    /// <param name="providerName">The name of the provider to query.</param>
    /// <param name="providerKey">
    /// A provider-specific discriminator (e.g. a tenant ID or user ID). When <see langword="null"/>,
    /// each provider applies its own default key resolution logic.
    /// </param>
    /// <param name="fallback">
    /// When <see langword="true"/>, values not found in <paramref name="providerName"/> fall back
    /// to subsequent providers.
    /// </param>
    /// <param name="cancellationToken">The abort token.</param>
    /// <returns>A list of <see cref="SettingValue"/> instances for all settings served by the provider.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="providerName"/> is <see langword="null"/>.</exception>
    Task<List<SettingValue>> GetAllAsync(
        string providerName,
        string? providerKey = null,
        bool fallback = true,
        CancellationToken cancellationToken = default
    );

    /// <summary>Persists a setting value through a specific provider.</summary>
    /// <param name="settingName">The unique name of the setting to update.</param>
    /// <param name="value">The new value to store, or <see langword="null"/> to clear it.</param>
    /// <param name="providerName">The name of the provider that will store the value.</param>
    /// <param name="providerKey">
    /// A provider-specific discriminator (e.g. a tenant ID or user ID).
    /// Pass <see langword="null"/> to use the provider's default key.
    /// </param>
    /// <param name="forceToSet">
    /// When <see langword="true"/>, bypasses provider-level read-only guards and forces the write.
    /// </param>
    /// <param name="cancellationToken">The abort token.</param>
    /// <exception cref="ArgumentNullException"><paramref name="settingName"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="providerName"/> is <see langword="null"/>.</exception>
    Task SetAsync(
        string settingName,
        string? value,
        string providerName,
        string? providerKey,
        bool forceToSet = false,
        CancellationToken cancellationToken = default
    );

    /// <summary>Deletes all setting values stored under a specific provider and key.</summary>
    /// <param name="providerName">The name of the provider whose values will be deleted.</param>
    /// <param name="providerKey">The provider-specific discriminator identifying the scope to clear.</param>
    /// <param name="cancellationToken">The abort token.</param>
    /// <exception cref="ArgumentNullException"><paramref name="providerName"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="providerKey"/> is <see langword="null"/>.</exception>
    Task DeleteAsync(string providerName, string providerKey, CancellationToken cancellationToken = default);
}
