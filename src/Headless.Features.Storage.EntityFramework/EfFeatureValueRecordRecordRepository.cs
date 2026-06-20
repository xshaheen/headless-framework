// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Domain;
using Headless.Features.Entities;
using Headless.Features.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Headless.Features;

/// <summary>EF Core implementation of <see cref="IFeatureValueRecordRepository"/>.</summary>
/// <typeparam name="TContext">The <see cref="DbContext"/> type that owns the feature value entities.</typeparam>
/// <param name="dbFactory">Factory used to create <typeparamref name="TContext"/> instances per operation.</param>
/// <param name="localPublisher">Local event bus used to publish <see cref="EntityChangedEventData{T}"/> after mutations.</param>
public sealed class EfFeatureValueRecordRecordRepository<TContext>(
    IDbContextFactory<TContext> dbFactory,
    ILocalEventBus localPublisher
) : IFeatureValueRecordRepository
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
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        return await db.Set<FeatureValueRecord>()
            .AsNoTracking()
            .OrderBy(x => x.Id)
            .FirstOrDefaultAsync(
                s => s.Name == name && s.ProviderName == providerName && s.ProviderKey == providerKey,
                cancellationToken
            );
    }

    /// <inheritdoc/>
    public async Task<List<FeatureValueRecord>> FindAllAsync(
        string name,
        string? providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var query = db.Set<FeatureValueRecord>().AsNoTracking().Where(s => s.Name == name);

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

    /// <inheritdoc/>
    public async Task<List<FeatureValueRecord>> GetListAsync(
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        return await db.Set<FeatureValueRecord>()
            .AsNoTracking()
            .Where(s => s.ProviderName == providerName && s.ProviderKey == providerKey)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task InsertAsync(FeatureValueRecord featureValue, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        db.Set<FeatureValueRecord>().Add(featureValue);
        await db.SaveChangesAsync(cancellationToken);

        await localPublisher.PublishAsync(
            new EntityChangedEventData<FeatureValueRecord>(featureValue),
            cancellationToken
        );
    }

    /// <inheritdoc/>
    public async Task UpdateAsync(FeatureValueRecord featureValue, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        db.Set<FeatureValueRecord>().Update(featureValue);
        await db.SaveChangesAsync(cancellationToken);

        await localPublisher.PublishAsync(
            new EntityChangedEventData<FeatureValueRecord>(featureValue),
            cancellationToken
        );
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(
        IReadOnlyCollection<FeatureValueRecord> featureValues,
        CancellationToken cancellationToken = default
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        db.Set<FeatureValueRecord>().RemoveRange(featureValues);
        await db.SaveChangesAsync(cancellationToken);

        foreach (var featureValue in featureValues)
        {
            await localPublisher.PublishAsync(
                new EntityChangedEventData<FeatureValueRecord>(featureValue),
                cancellationToken
            );
        }
    }
}
