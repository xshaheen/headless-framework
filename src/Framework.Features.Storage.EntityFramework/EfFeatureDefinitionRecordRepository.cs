// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Features.Definitions;
using Framework.Features.Entities;
using Microsoft.EntityFrameworkCore;

namespace Framework.Features;

public sealed class EfFeatureDefinitionRecordRepository<TContext>(IDbContextFactory<TContext> dbFactory)
    : IFeatureDefinitionRecordRepository
    where TContext : DbContext, IFeaturesDbContext
{
    public async Task<List<FeatureGroupDefinitionRecord>> GetGroupsListAsync(
        CancellationToken cancellationToken = default
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        return await db.FeatureGroupDefinitions.ToListAsync(cancellationToken);
    }

    public async Task<List<FeatureDefinitionRecord>> GetFeaturesListAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        return await db.FeatureDefinitions.ToListAsync(cancellationToken);
    }

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
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        db.FeatureGroupDefinitions.AddRange(newGroups);
        db.FeatureGroupDefinitions.UpdateRange(updatedGroups);
        db.FeatureGroupDefinitions.RemoveRange(deletedGroups);

        db.FeatureDefinitions.AddRange(newFeatures);
        db.FeatureDefinitions.UpdateRange(updatedFeatures);
        db.FeatureDefinitions.RemoveRange(deletedFeatures);

        await db.SaveChangesAsync(cancellationToken);
    }
}
