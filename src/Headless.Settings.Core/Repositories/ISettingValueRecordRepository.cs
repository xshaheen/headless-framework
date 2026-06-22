// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Settings.Entities;

namespace Headless.Settings.Repositories;

/// <summary>Persistence contract for <see cref="SettingValueRecord"/> aggregates.</summary>
public interface ISettingValueRecordRepository
{
    /// <summary>
    /// Returns the setting value record for the given name, provider, and optional provider key,
    /// or <see langword="null"/> if no matching record exists.
    /// </summary>
    /// <param name="name">The setting name to look up.</param>
    /// <param name="providerName">The name of the value provider.</param>
    /// <param name="providerKey">The optional provider-specific scope key (e.g. tenant id).</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The matching <see cref="SettingValueRecord"/>, or <see langword="null"/>.</returns>
    Task<SettingValueRecord?> FindAsync(
        string name,
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Returns all value records for the given setting name, optionally filtered by provider name and provider key.
    /// </summary>
    /// <param name="name">The setting name to look up.</param>
    /// <param name="providerName">The provider name filter, or <see langword="null"/> to include all providers.</param>
    /// <param name="providerKey">The provider key filter, or <see langword="null"/> to include all keys.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>All matching <see cref="SettingValueRecord"/> instances.</returns>
    Task<List<SettingValueRecord>> FindAllAsync(
        string name,
        string? providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Returns value records for the specified set of setting names scoped to a provider and optional provider key.
    /// </summary>
    /// <param name="names">The setting names to retrieve.</param>
    /// <param name="providerName">The provider name to filter by.</param>
    /// <param name="providerKey">The optional provider-specific scope key.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>All matching <see cref="SettingValueRecord"/> instances.</returns>
    Task<List<SettingValueRecord>> GetListAsync(
        HashSet<string> names,
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Returns all value records for the given provider and optional provider key.
    /// </summary>
    /// <param name="providerName">The provider name to filter by.</param>
    /// <param name="providerKey">The optional provider-specific scope key.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>All <see cref="SettingValueRecord"/> instances for the specified provider.</returns>
    Task<List<SettingValueRecord>> GetListAsync(
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    );

    /// <summary>Inserts a new <see cref="SettingValueRecord"/> into the data source.</summary>
    /// <param name="setting">The record to insert.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task InsertAsync(SettingValueRecord setting, CancellationToken cancellationToken = default);

    /// <summary>Updates an existing <see cref="SettingValueRecord"/> in the data source.</summary>
    /// <param name="setting">The record to update.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task UpdateAsync(SettingValueRecord setting, CancellationToken cancellationToken = default);

    /// <summary>Deletes a collection of <see cref="SettingValueRecord"/> instances from the data source.</summary>
    /// <param name="settings">The records to delete.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task DeleteAsync(IReadOnlyCollection<SettingValueRecord> settings, CancellationToken cancellationToken = default);
}
