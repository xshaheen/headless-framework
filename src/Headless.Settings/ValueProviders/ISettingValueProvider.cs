// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.UnitOfWork;

namespace Headless.Settings;

/// <summary>Provides read and write access to setting values from a named source.</summary>
/// <remarks>
/// New members arrive as default interface members. <see cref="SetAllEnlistedAsync" /> defaults to throwing
/// <see cref="NotSupportedException" />, so <c>unit.Settings</c> refuses a provider that cannot write inside a unit of
/// work; <see cref="StoreSettingValueProvider" /> implements it.
/// </remarks>
public interface ISettingValueProvider : ISettingValueReadProvider
{
    /// <summary>Persists <paramref name="value"/> for <paramref name="setting"/> scoped to <paramref name="providerKey"/>.</summary>
    /// <param name="setting">The setting definition to update.</param>
    /// <param name="value">The new value to store.</param>
    /// <param name="providerKey">Optional scoping key (e.g. tenant or user identifier).</param>
    /// <param name="cancellationToken">The abort token.</param>
    Task SetAsync(
        SettingDefinition setting,
        string value,
        string? providerKey,
        CancellationToken cancellationToken = default
    );

    /// <summary>Removes the stored value for <paramref name="setting"/> scoped to <paramref name="providerKey"/>.</summary>
    /// <param name="setting">The setting definition to clear.</param>
    /// <param name="providerKey">Optional scoping key (e.g. tenant or user identifier).</param>
    /// <param name="cancellationToken">The abort token.</param>
    Task ClearAsync(SettingDefinition setting, string? providerKey, CancellationToken cancellationToken = default);

    /// <summary>Stores or clears several values scoped to <paramref name="providerKey"/> as one write.</summary>
    /// <remarks>
    /// <c>SettingManager</c> routes every write through this member. The default implementation calls
    /// <see cref="SetAsync"/> and <see cref="ClearAsync"/> once per entry, so a failure part-way leaves the earlier
    /// entries written; override it when the backing source can apply the whole batch atomically, as
    /// <see cref="StoreSettingValueProvider"/> does.
    /// </remarks>
    /// <param name="values">The values to write, each paired with its definition. A <see langword="null"/> value clears the setting.</param>
    /// <param name="providerKey">Optional scoping key (e.g. tenant or user identifier).</param>
    /// <param name="cancellationToken">The abort token.</param>
    async Task SetAllAsync(
        IReadOnlyList<KeyValuePair<SettingDefinition, string?>> values,
        string? providerKey,
        CancellationToken cancellationToken = default
    )
    {
        foreach (var (setting, value) in values)
        {
            if (value is null)
            {
                await ClearAsync(setting, providerKey, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await SetAsync(setting, value, providerKey, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Persists <paramref name="values"/> scoped to <paramref name="providerKey"/> inside
    /// <paramref name="unitOfWork" />'s transaction; a <see langword="null"/> value clears the setting.
    /// </summary>
    /// <param name="unitOfWork">The unit whose transaction holds the writes.</param>
    /// <param name="values">The setting definitions and the values to store.</param>
    /// <param name="providerKey">Optional scoping key (e.g. tenant or user identifier).</param>
    /// <param name="cancellationToken">The abort token.</param>
    /// <exception cref="InvalidOperationException">The unit cannot host the writes.</exception>
    /// <exception cref="NotSupportedException">This provider cannot write inside a unit of work.</exception>
    Task SetAllEnlistedAsync(
        IUnitOfWork unitOfWork,
        IReadOnlyList<KeyValuePair<SettingDefinition, string?>> values,
        string? providerKey,
        CancellationToken cancellationToken
    )
    {
        throw new NotSupportedException(
            $"The setting value provider '{Name}' cannot write inside a unit of work. Write through the injected "
                + "ISettingManager, or implement SetAllEnlistedAsync."
        );
    }
}

/// <summary>Read-only contract for a named source of setting values (e.g. database, configuration, defaults).</summary>
public interface ISettingValueReadProvider
{
    /// <summary>Gets the unique name of this provider.</summary>
    string Name { get; }

    /// <summary>
    /// Whether values this provider returns for encrypted definitions are ciphertext produced by the
    /// manager's encryption pipeline. The manager encrypts before every provider write, so writable
    /// providers default to <see langword="true"/> while read-only sources that surface plaintext the
    /// manager never wrote (configuration, definition defaults) default to <see langword="false"/>.
    /// Override to <see langword="false"/> only for a writable provider whose values bypass the manager's
    /// encryption pipeline.
    /// </summary>
    bool StoresEncryptedValues => this is ISettingValueProvider;

    /// <summary>Returns the stored value for <paramref name="setting"/>, or <see langword="null"/> if none is set.</summary>
    /// <param name="setting">The setting definition to look up.</param>
    /// <param name="providerKey">Optional scoping key (e.g. tenant or user identifier). Provider implementations determine how this is used.</param>
    /// <param name="cancellationToken">The abort token.</param>
    /// <returns>The stored value, or <see langword="null"/> if not found.</returns>
    Task<string?> GetOrDefaultAsync(
        SettingDefinition setting,
        string? providerKey = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>Returns the stored values for all <paramref name="settings"/>.</summary>
    /// <remarks>
    /// Must agree with <see cref="GetOrDefaultAsync"/>: a setting resolvable through the single-value read must
    /// resolve identically through the batch, and vice versa. <c>SettingManager</c> routes by-provider bulk reads
    /// through this member, so a stubbed implementation silently drops those settings from bulk results.
    /// </remarks>
    /// <param name="settings">The setting definitions to look up.</param>
    /// <param name="providerKey">Optional scoping key (e.g. tenant or user identifier).</param>
    /// <param name="cancellationToken">The abort token.</param>
    /// <returns>A list of <see cref="SettingValue"/> entries; a <see langword="null"/> <c>Value</c> indicates no stored entry.</returns>
    Task<List<SettingValue>> GetAllAsync(
        SettingDefinition[] settings,
        string? providerKey = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Returns the stored value for <paramref name="setting"/> as seen inside <paramref name="unitOfWork" />'s
    /// transaction. <c>SettingManager</c> uses it for the inherited-value comparison of an enlisted write, so a value
    /// the unit already wrote is seen.
    /// </summary>
    /// <remarks>
    /// The default is <see cref="GetOrDefaultAsync"/>, which is right for a provider whose values do not live in the
    /// unit's database (configuration, definition defaults). A provider that reads that database overrides it.
    /// </remarks>
    /// <param name="unitOfWork">The unit whose transaction runs the read.</param>
    /// <param name="setting">The setting definition to look up.</param>
    /// <param name="providerKey">Optional scoping key (e.g. tenant or user identifier).</param>
    /// <param name="cancellationToken">The abort token.</param>
    /// <returns>The stored value, or <see langword="null"/> if not found.</returns>
    Task<string?> GetOrDefaultEnlistedAsync(
        IUnitOfWork unitOfWork,
        SettingDefinition setting,
        string? providerKey,
        CancellationToken cancellationToken
    )
    {
        return GetOrDefaultAsync(setting, providerKey, cancellationToken);
    }
}
