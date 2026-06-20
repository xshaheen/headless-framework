// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Features.Entities;

namespace Headless.Features.Repositories;

/// <summary>Persistence contract for reading and writing feature group and feature definition records.</summary>
public interface IFeatureDefinitionRecordRepository
{
    /// <summary>Returns all stored feature group definition records.</summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A list of all <see cref="FeatureGroupDefinitionRecord"/> rows.</returns>
    Task<List<FeatureGroupDefinitionRecord>> GetGroupsListAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns all stored feature definition records.</summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A list of all <see cref="FeatureDefinitionRecord"/> rows.</returns>
    Task<List<FeatureDefinitionRecord>> GetFeaturesListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies a batch of inserts, updates, and deletes for both feature groups and features in a single operation.
    /// </summary>
    /// <param name="newGroups">Feature group records to insert.</param>
    /// <param name="updatedGroups">Feature group records to update.</param>
    /// <param name="deletedGroups">Feature group records to delete.</param>
    /// <param name="newFeatures">Feature definition records to insert.</param>
    /// <param name="updatedFeatures">Feature definition records to update.</param>
    /// <param name="deletedFeatures">Feature definition records to delete.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task SaveAsync(
        List<FeatureGroupDefinitionRecord> newGroups,
        List<FeatureGroupDefinitionRecord> updatedGroups,
        List<FeatureGroupDefinitionRecord> deletedGroups,
        List<FeatureDefinitionRecord> newFeatures,
        List<FeatureDefinitionRecord> updatedFeatures,
        List<FeatureDefinitionRecord> deletedFeatures,
        CancellationToken cancellationToken = default
    );
}
