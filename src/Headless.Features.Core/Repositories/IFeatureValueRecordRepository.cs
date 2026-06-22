// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Features.Entities;

namespace Headless.Features.Repositories;

/// <summary>Persistence contract for reading and writing feature value records.</summary>
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
}
