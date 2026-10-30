// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Serializer;
using Headless.UnitOfWork;

namespace Headless.Settings;

/// <summary>
/// Provides one unit-of-work handle bound to setting writes, returned by <c>unit.Settings</c>. A value written or
/// deleted through it is kept only when that unit commits.
/// </summary>
/// <remarks>
/// One binding per unit, created on the first read of <c>unit.Settings</c> and kept as unit-local state; it owns
/// nothing to dispose. The value cache is cleared and <see cref="SettingChangedMessage" /> is published after the unit
/// commits, never at write time. The injected <see cref="ISettingManager" /> stays autonomous: its writes never join a
/// unit.
/// </remarks>
[PublicAPI]
public sealed class UnitOfWorkSettings
{
    private readonly IUnitOfWorkSettings _settings;
    private readonly IUnitOfWork _unitOfWork;

    internal UnitOfWorkSettings(IUnitOfWorkSettings settings, IUnitOfWork unitOfWork)
    {
        _settings = settings;
        _unitOfWork = unitOfWork;
    }

    /// <summary>Writes one setting value inside the bound unit's transaction.</summary>
    /// <param name="settingName">The setting name.</param>
    /// <param name="value">The value; <see langword="null" /> clears the setting for the scope.</param>
    /// <param name="providerName">The value provider name, for example <c>"Global"</c>, <c>"Tenant"</c>, or <c>"User"</c>.</param>
    /// <param name="providerKey">The provider-specific key, such as a tenant or user id.</param>
    /// <param name="forceToSet">
    /// <see langword="true" /> to store the value even when it equals the inherited fallback value.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the write ran inside the unit's transaction.</returns>
    /// <exception cref="ConflictException">The setting is not defined, or the provider is not registered or is read-only.</exception>
    /// <exception cref="InvalidOperationException">The unit cannot host the write.</exception>
    /// <exception cref="NotSupportedException">The provider or the configured storage cannot write inside a unit of work.</exception>
    public Task SetAsync(
        string settingName,
        string? value,
        string providerName,
        string? providerKey,
        bool forceToSet = false,
        CancellationToken cancellationToken = default
    )
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal) { [settingName] = value };

        return _settings.SetAsync(_unitOfWork, values, providerName, providerKey, forceToSet, cancellationToken);
    }

    /// <summary>Writes one setting value serialized as JSON inside the bound unit's transaction.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="settingName">The setting name.</param>
    /// <param name="value">The value to serialize.</param>
    /// <param name="providerName">The value provider name.</param>
    /// <param name="providerKey">The provider-specific key.</param>
    /// <param name="forceToSet">
    /// <see langword="true" /> to store the value even when it equals the inherited fallback value.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the write ran inside the unit's transaction.</returns>
    public Task SetAsync<T>(
        string settingName,
        T? value,
        string providerName,
        string? providerKey,
        bool forceToSet = false,
        CancellationToken cancellationToken = default
    )
    {
        var valueJson = JsonSerializer.Serialize(value, JsonConstants.DefaultInternalJsonOptions);

        return SetAsync(settingName, valueJson, providerName, providerKey, forceToSet, cancellationToken);
    }

    /// <summary>Writes several setting values for one provider scope inside the bound unit's transaction.</summary>
    /// <param name="values">The setting names and values; a <see langword="null" /> value clears the setting.</param>
    /// <param name="providerName">The value provider name.</param>
    /// <param name="providerKey">The provider-specific key.</param>
    /// <param name="forceToSet">
    /// <see langword="true" /> to store a value even when it equals the inherited fallback value.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the writes ran inside the unit's transaction.</returns>
    /// <exception cref="ConflictException">A setting is not defined, or the provider is not registered or is read-only.</exception>
    /// <exception cref="InvalidOperationException">The unit cannot host the write.</exception>
    /// <exception cref="NotSupportedException">The provider or the configured storage cannot write inside a unit of work.</exception>
    public Task SetAsync(
        IReadOnlyDictionary<string, string?> values,
        string providerName,
        string? providerKey,
        bool forceToSet = false,
        CancellationToken cancellationToken = default
    )
    {
        return _settings.SetAsync(_unitOfWork, values, providerName, providerKey, forceToSet, cancellationToken);
    }

    /// <summary>Writes a global setting value inside the bound unit's transaction.</summary>
    /// <param name="settingName">The setting name.</param>
    /// <param name="value">The value; <see langword="null" /> clears the global value.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the write ran inside the unit's transaction.</returns>
    public Task SetGlobalAsync(string settingName, string? value, CancellationToken cancellationToken = default)
    {
        return SetAsync(
            settingName,
            value,
            SettingValueProviderNames.Global,
            providerKey: null,
            cancellationToken: cancellationToken
        );
    }

    /// <summary>Writes a setting value for a tenant inside the bound unit's transaction.</summary>
    /// <param name="tenantId">The tenant id.</param>
    /// <param name="settingName">The setting name.</param>
    /// <param name="value">The value; <see langword="null" /> clears the tenant's value.</param>
    /// <param name="forceToSet">
    /// <see langword="true" /> to store the value even when it equals the inherited fallback value.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the write ran inside the unit's transaction.</returns>
    public Task SetForTenantAsync(
        string tenantId,
        string settingName,
        string? value,
        bool forceToSet = false,
        CancellationToken cancellationToken = default
    )
    {
        return SetAsync(settingName, value, SettingValueProviderNames.Tenant, tenantId, forceToSet, cancellationToken);
    }

    /// <summary>Writes a setting value for a user inside the bound unit's transaction.</summary>
    /// <param name="userId">The user id.</param>
    /// <param name="settingName">The setting name.</param>
    /// <param name="value">The value; <see langword="null" /> clears the user's value.</param>
    /// <param name="forceToSet">
    /// <see langword="true" /> to store the value even when it equals the inherited fallback value.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the write ran inside the unit's transaction.</returns>
    public Task SetForUserAsync(
        string userId,
        string settingName,
        string? value,
        bool forceToSet = false,
        CancellationToken cancellationToken = default
    )
    {
        return SetAsync(settingName, value, SettingValueProviderNames.User, userId, forceToSet, cancellationToken);
    }

    /// <summary>Deletes every value stored for one provider scope inside the bound unit's transaction.</summary>
    /// <param name="providerName">The value provider name.</param>
    /// <param name="providerKey">The provider-specific key.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the delete ran inside the unit's transaction.</returns>
    /// <exception cref="InvalidOperationException">The unit cannot host the delete.</exception>
    /// <exception cref="NotSupportedException">The configured storage cannot write inside a unit of work.</exception>
    public Task DeleteAsync(string providerName, string providerKey, CancellationToken cancellationToken = default)
    {
        return _settings.DeleteAsync(_unitOfWork, providerName, providerKey, cancellationToken);
    }
}
