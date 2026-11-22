// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Settings;

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
    /// <returns>
    /// A never-<see langword="null"/> <see cref="SettingValue"/>. On a miss its
    /// <see cref="SettingValue.Value"/> and <see cref="SettingValue.Provider"/> are both
    /// <see langword="null"/>; otherwise <see cref="SettingValue.Provider"/> identifies the
    /// resolving provider.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="settingName"/> is <see langword="null"/>.</exception>
    /// <exception cref="Headless.ConflictException">The setting named <paramref name="settingName"/> is not defined.</exception>
    Task<SettingValue> GetAsync(
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
    /// <returns>A read-only list of <see cref="SettingValue"/> instances for all settings served by the provider.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="providerName"/> is <see langword="null"/>.</exception>
    Task<IReadOnlyList<SettingValue>> GetAllAsync(
        string providerName,
        string? providerKey = null,
        bool fallback = true,
        CancellationToken cancellationToken = default
    );

    /// <summary>Returns the values of a set of named settings from a specific provider.</summary>
    /// <param name="settingNames">The set of setting names to resolve.</param>
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
    /// <returns>A read-only list of <see cref="SettingValue"/> instances for the requested settings that resolved.</returns>
    /// <remarks>
    /// Prefer this over <see cref="GetAllAsync(HashSet{string}, CancellationToken)"/> when the values
    /// belong to one scope rather than to the caller. The name-only overload walks the whole provider
    /// chain, so a per-user or per-tenant value can shadow an application-wide one, which is wrong for
    /// a value that is meant to be the same for everybody.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="settingNames"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="providerName"/> is <see langword="null"/>.</exception>
    Task<IReadOnlyList<SettingValue>> GetAllAsync(
        HashSet<string> settingNames,
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
    /// <exception cref="Headless.ConflictException">
    /// The setting is not defined, the provider named <paramref name="providerName"/> is not registered, or that
    /// provider is read-only.
    /// </exception>
    Task SetAsync(
        string settingName,
        string? value,
        string providerName,
        string? providerKey,
        bool forceToSet = false,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Persists several setting values through a specific provider and announces them in one
    /// <see cref="SettingChangedMessage"/>.
    /// </summary>
    /// <param name="values">
    /// The new values keyed by setting name. A <see langword="null"/> value clears that setting. An empty
    /// dictionary writes nothing and announces nothing.
    /// </param>
    /// <param name="providerName">The name of the provider that will store the values.</param>
    /// <param name="providerKey">
    /// A provider-specific discriminator (e.g. a tenant ID or user ID).
    /// Pass <see langword="null"/> to use the provider's default key.
    /// </param>
    /// <param name="forceToSet">
    /// When <see langword="true"/>, stores each value even when it equals the value the next provider would
    /// supply; otherwise such a value is cleared instead so the setting keeps inheriting it.
    /// </param>
    /// <param name="cancellationToken">The abort token.</param>
    /// <remarks>
    /// Every name, the provider, and its writability are checked before anything is written, so a rejected batch
    /// changes nothing. The built-in store providers then write the whole batch in one transaction: a failed write
    /// leaves every value as it was, and no announcement goes out. A custom provider that does not override
    /// <c>ISettingValueProvider.SetAllAsync</c> writes one value at a time and gives no such guarantee.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="values"/> or <paramref name="providerName"/> is <see langword="null"/>.</exception>
    /// <exception cref="Headless.ConflictException">
    /// A setting in <paramref name="values"/> is not defined, the provider named <paramref name="providerName"/>
    /// is not registered, or that provider is read-only.
    /// </exception>
    Task SetAsync(
        IReadOnlyDictionary<string, string?> values,
        string providerName,
        string? providerKey,
        bool forceToSet = false,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Reads a group of settings in one provider scope together with the group's version, for an edit form that saves
    /// them back with <see cref="SetVersionedAsync"/>.
    /// </summary>
    /// <param name="settingNames">The settings of the group. Save back exactly this set of names.</param>
    /// <param name="providerName">The provider whose scope the form edits, for example <c>"Tenant"</c>.</param>
    /// <param name="providerKey">
    /// The provider-specific key (e.g. a tenant ID). When <see langword="null"/>, the provider resolves its default key,
    /// such as the current tenant.
    /// </param>
    /// <param name="fallback">
    /// When <see langword="true"/>, an inherited setting that this scope does not store shows the value the next
    /// provider supplies. The version never covers those inherited values, so a form that saves an inherited value
    /// back unchanged stores it as an override, even after the inherited value changed, without a version mismatch.
    /// Leave it <see langword="false"/> for an edit form, or send back only the values the user changed.
    /// </param>
    /// <param name="cancellationToken">The abort token.</param>
    /// <returns>
    /// One <see cref="SettingValue"/> per requested name, in no particular order, read from storage rather than the
    /// cache, and the group's version.
    /// </returns>
    /// <remarks>
    /// The version represents the current stored rows, not write history. An unchanged save preserves it. A return to
    /// the absent representation can reuse it. With fallback disabled, the default, it serves as a scope-local HTTP <c>ETag</c>.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="settingNames"/> or <paramref name="providerName"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="settingNames"/> is empty.</exception>
    /// <exception cref="Headless.ConflictException">
    /// A setting is not defined or does not allow the provider, or the provider is not registered or is read-only.
    /// </exception>
    /// <exception cref="NotSupportedException">
    /// The provider's storage cannot version its values, or more than one provider is registered under the name.
    /// </exception>
    Task<VersionedSettingValues> GetVersionedAsync(
        IReadOnlyCollection<string> settingNames,
        string providerName,
        string? providerKey = null,
        bool fallback = false,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Saves a group of settings read with <see cref="GetVersionedAsync"/> only while its stored representation
    /// still matches that version in the same scope.
    /// </summary>
    /// <param name="values">
    /// The new values keyed by setting name, for exactly the names the version was read for. A <see langword="null"/>
    /// value clears that setting.
    /// </param>
    /// <param name="providerName">The provider that will store the values.</param>
    /// <param name="providerKey">The provider-specific key, as passed to <see cref="GetVersionedAsync"/>.</param>
    /// <param name="version">The <see cref="VersionedSettingValues.Version"/> the caller read.</param>
    /// <param name="forceToSet">
    /// When <see langword="true"/>, stores each value even when it equals the value the next provider would supply;
    /// otherwise such a value is cleared instead so the setting keeps inheriting it.
    /// </param>
    /// <param name="cancellationToken">The abort token.</param>
    /// <remarks>
    /// The check and the write run in one serializable transaction. Of two saves that change the same group from one
    /// version, at most one succeeds. An unchanged save writes nothing and preserves the token. A successful save
    /// announces one <see cref="SettingChangedMessage"/>. The returned token is computed inside the transaction.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="values"/> or <paramref name="providerName"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="values"/> is empty, or <paramref name="version"/> is empty or white space.</exception>
    /// <exception cref="Headless.ConflictException">
    /// The group changed after <paramref name="version"/> was read (error code <c>g:setting_version_mismatch</c>, also
    /// raised when the names differ from the ones read), or a setting is not defined or does not allow the provider, or
    /// the provider is not registered or is read-only.
    /// </exception>
    /// <exception cref="NotSupportedException">
    /// The provider's storage cannot version its values, or more than one provider is registered under the name.
    /// </exception>
    Task<string> SetVersionedAsync(
        IReadOnlyDictionary<string, string?> values,
        string providerName,
        string? providerKey,
        string version,
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
