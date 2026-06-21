// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Features.Entities;
using Headless.Features.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Headless.Features;

/// <summary>EF Core implementation of <see cref="IFeatureDefinitionRecordRepository"/>.</summary>
/// <typeparam name="TContext">The <see cref="DbContext"/> type that owns the feature definition entities.</typeparam>
/// <param name="dbFactory">Factory used to create <typeparamref name="TContext"/> instances per operation.</param>
public sealed class EfFeatureDefinitionRecordRepository<TContext>(IDbContextFactory<TContext> dbFactory)
    : IFeatureDefinitionRecordRepository
    where TContext : DbContext
{
    /// <inheritdoc/>
    public async Task<List<FeatureGroupDefinitionRecord>> GetGroupsListAsync(
        CancellationToken cancellationToken = default
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        return await db.Set<FeatureGroupDefinitionRecord>()
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<List<FeatureDefinitionRecord>> GetFeaturesListAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        return await db.Set<FeatureDefinitionRecord>()
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task SaveAsync(
        List<FeatureGroupDefinitionRecord> newGroups,
        List<FeatureGroupDefinitionRecord> updatedGroups,
        List<FeatureGroupDefinitionRecord> deletedGroups,
        List<FeatureDefinitionRecord> newFeatures,
        List<FeatureDefinitionRecord> updatedFeatures,
        List<FeatureDefinitionRecord> deletedFeatures,
        CancellationToken cancellationToken = default
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        db.Set<FeatureGroupDefinitionRecord>().AddRange(newGroups);
        db.Set<FeatureGroupDefinitionRecord>().UpdateRange(updatedGroups);
        db.Set<FeatureGroupDefinitionRecord>().RemoveRange(deletedGroups);

        db.Set<FeatureDefinitionRecord>().AddRange(newFeatures);
        db.Set<FeatureDefinitionRecord>().UpdateRange(updatedFeatures);
        db.Set<FeatureDefinitionRecord>().RemoveRange(deletedFeatures);

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
