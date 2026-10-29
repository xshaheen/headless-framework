// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.UnitOfWork;

namespace Headless.Settings;

/// <summary>Persistence contract for <see cref="SettingValueRecord"/> aggregates.</summary>
/// <remarks>
/// New members arrive as default interface members, so a third-party implementation keeps compiling. The enlisted
/// members (<c>*EnlistedAsync</c>) run inside a caller's <see cref="IUnitOfWork" /> and default to throwing
/// <see cref="NotSupportedException" />; an implementation that cannot join a unit leaves them, and <c>unit.Settings</c>
/// then refuses before any write.
/// </remarks>
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

    /// <summary>
    /// Inserts, updates, and deletes the given records in one transaction: either every change persists or none does.
    /// </summary>
    /// <remarks>
    /// An update that matches no stored row must fail the batch (for example with
    /// <see cref="System.Data.DBConcurrencyException"/>) rather than succeed silently: the store treats that failure,
    /// like a unique-key violation on insert, as a concurrent writer and plans the batch again.
    /// </remarks>
    /// <param name="inserted">The new records to insert.</param>
    /// <param name="updated">The existing records whose values changed.</param>
    /// <param name="deleted">The existing records to delete.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task SaveAsync(
        IReadOnlyCollection<SettingValueRecord> inserted,
        IReadOnlyCollection<SettingValueRecord> updated,
        IReadOnlyCollection<SettingValueRecord> deleted,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Returns the records for <paramref name="names"/> in one provider scope, read inside
    /// <paramref name="unitOfWork" />'s transaction so the read sees the unit's own uncommitted writes.
    /// </summary>
    /// <param name="unitOfWork">The unit whose transaction runs the read.</param>
    /// <param name="names">The setting names to read.</param>
    /// <param name="providerName">The name of the value provider.</param>
    /// <param name="providerKey">The optional provider-specific scope key.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <exception cref="InvalidOperationException">The unit cannot host the read.</exception>
    /// <exception cref="NotSupportedException">This storage cannot run inside a unit of work.</exception>
    Task<List<SettingValueRecord>> GetListEnlistedAsync(
        IUnitOfWork unitOfWork,
        HashSet<string> names,
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken
    )
    {
        throw _NotEnlistable();
    }

    /// <summary>
    /// Returns every record of one provider scope, read inside <paramref name="unitOfWork" />'s transaction.
    /// </summary>
    /// <param name="unitOfWork">The unit whose transaction runs the read.</param>
    /// <param name="providerName">The name of the value provider.</param>
    /// <param name="providerKey">The optional provider-specific scope key.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <exception cref="InvalidOperationException">The unit cannot host the read.</exception>
    /// <exception cref="NotSupportedException">This storage cannot run inside a unit of work.</exception>
    Task<List<SettingValueRecord>> GetListEnlistedAsync(
        IUnitOfWork unitOfWork,
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken
    )
    {
        throw _NotEnlistable();
    }

    /// <summary>
    /// Inserts, updates, and deletes the given records inside <paramref name="unitOfWork" />'s transaction, with the
    /// same failure rule as <see cref="SaveAsync"/>: an update that matches no stored row fails.
    /// </summary>
    /// <param name="unitOfWork">The unit whose transaction holds the writes.</param>
    /// <param name="inserted">The new records to insert.</param>
    /// <param name="updated">The existing records whose values changed.</param>
    /// <param name="deleted">The existing records to delete.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <exception cref="InvalidOperationException">The unit cannot host the write.</exception>
    /// <exception cref="NotSupportedException">This storage cannot run inside a unit of work.</exception>
    Task SaveEnlistedAsync(
        IUnitOfWork unitOfWork,
        IReadOnlyCollection<SettingValueRecord> inserted,
        IReadOnlyCollection<SettingValueRecord> updated,
        IReadOnlyCollection<SettingValueRecord> deleted,
        CancellationToken cancellationToken
    )
    {
        throw _NotEnlistable();
    }

    private static NotSupportedException _NotEnlistable()
    {
        return new NotSupportedException(
            "The configured setting value repository cannot run inside a unit of work. Use the EF Core, PostgreSQL, or "
                + "SQL Server settings storage, or write through the injected ISettingManager."
        );
    }
}
