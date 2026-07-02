// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Features.Entities;
using Headless.Features.Repositories;
using Headless.Features.Values;
using Microsoft.EntityFrameworkCore;

namespace Headless.Features;

/// <summary>EF Core implementation of <see cref="IFeatureValueRecordRepository"/>.</summary>
/// <typeparam name="TContext">The <see cref="DbContext"/> type that owns the feature value entities.</typeparam>
/// <param name="dbFactory">Factory used to create <typeparamref name="TContext"/> instances per operation.</param>
/// <param name="cache">
/// The feature-value cache shared with <c>FeatureValueStore</c>. A write here removes the affected cache key
/// so a direct repository write (bypassing <c>IFeatureManager</c>) is reflected on the next read.
/// </param>
public sealed class EfFeatureValueRecordRepository<TContext>(IDbContextFactory<TContext> dbFactory, ICache cache)
    : IFeatureValueRecordRepository
    where TContext : DbContext
{
    /// <inheritdoc/>
    public async Task<FeatureValueRecord?> FindAsync(
        string name,
        string? providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        return await db.Set<FeatureValueRecord>()
            .AsNoTracking()
            .OrderBy(x => x.Id)
            .FirstOrDefaultAsync(
                s => s.Name == name && s.ProviderName == providerName && s.ProviderKey == providerKey,
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<List<FeatureValueRecord>> FindAllAsync(
        string name,
        string? providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var query = db.Set<FeatureValueRecord>().AsNoTracking().Where(s => s.Name == name);

        if (providerName != null)
        {
            query = query.Where(s => s.ProviderName == providerName);
        }

        if (providerKey != null)
        {
            query = query.Where(s => s.ProviderKey == providerKey);
        }

        return await query.ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<List<FeatureValueRecord>> GetListAsync(
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        return await db.Set<FeatureValueRecord>()
            .AsNoTracking()
            .Where(s => s.ProviderName == providerName && s.ProviderKey == providerKey)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task InsertAsync(FeatureValueRecord featureValue, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        db.Set<FeatureValueRecord>().Add(featureValue);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await cache.RemoveAsync(_CacheKey(featureValue), cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task UpdateAsync(FeatureValueRecord featureValue, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        db.Set<FeatureValueRecord>().Update(featureValue);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await cache.RemoveAsync(_CacheKey(featureValue), cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(
        IReadOnlyCollection<FeatureValueRecord> featureValues,
        CancellationToken cancellationToken = default
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        db.Set<FeatureValueRecord>().RemoveRange(featureValues);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await cache.RemoveAllAsync(featureValues.Select(_CacheKey), cancellationToken).ConfigureAwait(false);
    }

    private static string _CacheKey(FeatureValueRecord featureValue)
    {
        return FeatureValueCacheItem.CalculateCacheKey(
            featureValue.Name,
            featureValue.ProviderName,
            featureValue.ProviderKey
        );
    }
}
