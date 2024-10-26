// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Features.Definitions;
using Framework.Features.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Framework.Features.Storage.EntityFramework;

public sealed class EfFeatureDefinitionRecordRepository(IServiceScopeFactory scopeFactory)
    : IFeatureDefinitionRecordRepository
{
    public async Task<List<FeatureGroupDefinitionRecord>> GetGroupsListAsync(
        CancellationToken cancellationToken = default
    )
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FeaturesDbContext>();

        return await db.FeatureGroupDefinitions.ToListAsync(cancellationToken);
    }

    public async Task<List<FeatureDefinitionRecord>> GetFeaturesListAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FeaturesDbContext>();

        return await db.FeatureDefinitions.ToListAsync(cancellationToken);
    }

    public async Task SaveAsync(
        List<FeatureGroupDefinitionRecord> newGroups,
        List<FeatureGroupDefinitionRecord> updatedGroups,
        List<FeatureGroupDefinitionRecord> deletedGroups,
        List<FeatureDefinitionRecord> newFeatures,
        List<FeatureDefinitionRecord> updatedFeatures,
        List<FeatureDefinitionRecord> deletedFeatures,
        CancellationToken cancellationToken
    )
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FeaturesDbContext>();

        db.FeatureGroupDefinitions.AddRange(newGroups);
        db.FeatureGroupDefinitions.UpdateRange(updatedGroups);
        db.FeatureGroupDefinitions.RemoveRange(deletedGroups);

        db.FeatureDefinitions.AddRange(newFeatures);
        db.FeatureDefinitions.UpdateRange(updatedFeatures);
        db.FeatureDefinitions.RemoveRange(deletedFeatures);

        await db.SaveChangesAsync(cancellationToken);
    }
}
