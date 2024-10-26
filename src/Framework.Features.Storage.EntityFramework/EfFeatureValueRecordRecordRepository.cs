// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Features.Entities;
using Framework.Features.Values;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Framework.Features.Storage.EntityFramework;

public sealed class EfFeatureValueRecordRecordRepository(IServiceScopeFactory scopeFactory)
    : IFeatureValueRecordRepository
{
    public async Task<FeatureValueRecord?> FindAsync(
        string name,
        string? providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    )
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FeaturesDbContext>();

        return await db
            .FeatureValues.OrderBy(x => x.Id)
            .FirstOrDefaultAsync(
                s => s.Name == name && s.ProviderName == providerName && s.ProviderKey == providerKey,
                cancellationToken
            );
    }

    public async Task<List<FeatureValueRecord>> FindAllAsync(
        string name,
        string? providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    )
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FeaturesDbContext>();

        var query = db.FeatureValues.Where(s => s.Name == name);

        if (providerName != null)
        {
            query = query.Where(s => s.ProviderName == providerName);
        }

        if (providerKey != null)
        {
            query = query.Where(s => s.ProviderKey == providerKey);
        }

        return await query.ToListAsync(cancellationToken);
    }

    public async Task<List<FeatureValueRecord>> GetListAsync(
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    )
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FeaturesDbContext>();

        return await db
            .FeatureValues.Where(s => s.ProviderName == providerName && s.ProviderKey == providerKey)
            .ToListAsync(cancellationToken);
    }

    public async Task InsertAsync(FeatureValueRecord featureValue, CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FeaturesDbContext>();
        db.FeatureValues.Add(featureValue);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(FeatureValueRecord featureValue, CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FeaturesDbContext>();
        db.FeatureValues.Update(featureValue);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(
        IEnumerable<FeatureValueRecord> featureValues,
        CancellationToken cancellationToken = default
    )
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FeaturesDbContext>();
        db.FeatureValues.RemoveRange(featureValues);
        await db.SaveChangesAsync(cancellationToken);
    }
}
