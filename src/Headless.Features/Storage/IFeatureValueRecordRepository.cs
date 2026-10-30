// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.UnitOfWork;

namespace Headless.Features;

/// <summary>Persistence contract for reading and writing feature value records.</summary>
/// <remarks>
/// New members arrive as default interface members, so a third-party implementation keeps compiling. The enlisted
/// members (<c>*EnlistedAsync</c>) run inside a caller's <see cref="IUnitOfWork" /> and default to throwing
/// <see cref="NotSupportedException" />; an implementation that cannot join a unit leaves them, and <c>unit.Features</c>
/// then refuses before any write.
/// </remarks>
public interface IFeatureValueRecordRepository
{
    /// <summary>
    /// Finds the single feature value record for the given feature <paramref name="name"/> and optional provider
    /// scope, or returns <see langword="null"/> when absent.
    /// </summary>
    /// <param name="name">The feature name to look up.</param>
    /// <param name="providerName">The value provider name to match, or <see langword="null"/> to match any.</param>
    /// <param name="providerKey">The provider-specific scope key to match, or <see langword="null"/> for provider-global values.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The matching <see cref="FeatureValueRecord"/>, or <see langword="null"/> when not found.</returns>
    Task<FeatureValueRecord?> FindAsync(
        string name,
        string? providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Finds all feature value records for the given feature <paramref name="name"/> and optional provider scope.
    /// </summary>
    /// <param name="name">The feature name to look up.</param>
    /// <param name="providerName">The value provider name to filter by, or <see langword="null"/> to include all providers.</param>
    /// <param name="providerKey">The provider-specific scope key to filter by, or <see langword="null"/> for provider-global values.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A list of all matching <see cref="FeatureValueRecord"/> instances.</returns>
    Task<List<FeatureValueRecord>> FindAllAsync(
        string name,
        string? providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Returns the feature value records for the specified set of feature <paramref name="names"/> scoped to a
    /// provider and optional provider key.
    /// </summary>
    /// <param name="names">The feature names to retrieve.</param>
    /// <param name="providerName">The value provider name to filter by.</param>
    /// <param name="providerKey">The provider-specific scope key to filter by, or <see langword="null"/> for provider-global values.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>All matching <see cref="FeatureValueRecord"/> instances.</returns>
    Task<List<FeatureValueRecord>> GetListAsync(
        HashSet<string> names,
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    );

    /// <summary>Returns all feature value records for the given <paramref name="providerName"/> and optional <paramref name="providerKey"/>.</summary>
    /// <param name="providerName">The value provider name to filter by.</param>
    /// <param name="providerKey">The provider-specific scope key to filter by, or <see langword="null"/> for provider-global values.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A list of all <see cref="FeatureValueRecord"/> instances matching the given scope.</returns>
    Task<List<FeatureValueRecord>> GetListAsync(
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    );

    /// <summary>Inserts a new <see cref="FeatureValueRecord"/> into the store.</summary>
    /// <param name="featureValue">The record to insert.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task InsertAsync(FeatureValueRecord featureValue, CancellationToken cancellationToken = default);

    /// <summary>Updates an existing <see cref="FeatureValueRecord"/> in the store.</summary>
    /// <param name="featureValue">The record to update.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task UpdateAsync(FeatureValueRecord featureValue, CancellationToken cancellationToken = default);

    /// <summary>Deletes the given <see cref="FeatureValueRecord"/> instances from the store.</summary>
    /// <param name="featureValues">The collection of records to delete.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task DeleteAsync(
        IReadOnlyCollection<FeatureValueRecord> featureValues,
        CancellationToken cancellationToken = default
    );

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
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task SaveAsync(
        IReadOnlyCollection<FeatureValueRecord> inserted,
        IReadOnlyCollection<FeatureValueRecord> updated,
        IReadOnlyCollection<FeatureValueRecord> deleted,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Returns the records for <paramref name="names"/> in one provider scope, read inside
    /// <paramref name="unitOfWork" />'s transaction so the read sees the unit's own uncommitted writes.
    /// </summary>
    /// <param name="unitOfWork">The unit whose transaction runs the read.</param>
    /// <param name="names">The feature names to read.</param>
    /// <param name="providerName">The name of the value provider.</param>
    /// <param name="providerKey">The optional provider-specific scope key.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <exception cref="InvalidOperationException">The unit cannot host the read.</exception>
    /// <exception cref="NotSupportedException">This storage cannot run inside a unit of work.</exception>
    Task<List<FeatureValueRecord>> GetListEnlistedAsync(
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
    Task<List<FeatureValueRecord>> GetListEnlistedAsync(
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
        IReadOnlyCollection<FeatureValueRecord> inserted,
        IReadOnlyCollection<FeatureValueRecord> updated,
        IReadOnlyCollection<FeatureValueRecord> deleted,
        CancellationToken cancellationToken
    )
    {
        throw _NotEnlistable();
    }

    private static NotSupportedException _NotEnlistable()
    {
        return new NotSupportedException(
            "The configured feature value repository cannot run inside a unit of work. Use the EF Core, PostgreSQL, or "
                + "SQL Server features storage, or write through the injected IFeatureManager."
        );
    }
}
