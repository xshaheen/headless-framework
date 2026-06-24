// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Domain;
using Headless.Features.Entities;
using Headless.Features.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Features;

/// <summary>EF Core implementation of <see cref="IFeatureValueRecordRepository"/>.</summary>
/// <typeparam name="TContext">The <see cref="DbContext"/> type that owns the feature value entities.</typeparam>
/// <param name="dbFactory">Factory used to create <typeparamref name="TContext"/> instances per operation.</param>
/// <param name="services">
/// Root service provider used to resolve a scoped <see cref="ILocalEventBus"/> per publish. The repository is a
/// singleton, so it cannot capture the scoped bus directly; each publish opens a short-lived scope instead.
/// </param>
public sealed class EfFeatureValueRecordRecordRepository<TContext>(
    IDbContextFactory<TContext> dbFactory,
    IServiceProvider services
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

        await _PublishAsync(featureValue, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task UpdateAsync(FeatureValueRecord featureValue, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        db.Set<FeatureValueRecord>().Update(featureValue);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await _PublishAsync(featureValue, cancellationToken).ConfigureAwait(false);
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

        foreach (var featureValue in featureValues)
        {
            await _PublishAsync(featureValue, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Publishes an <see cref="EntityChangedEventData{T}"/> event for <paramref name="featureValue"/> when an
    /// <see cref="ILocalEventBus"/> is registered in the container. Resolved from a short-lived scope because
    /// the repository is a singleton and the bus is scoped.
    /// </summary>
    private async ValueTask _PublishAsync(FeatureValueRecord featureValue, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var publisher = scope.ServiceProvider.GetService<ILocalEventBus>();

        if (publisher is not null)
        {
            await publisher
                .PublishAsync(new EntityChangedEventData<FeatureValueRecord>(featureValue), cancellationToken)
                .ConfigureAwait(false);
        }
    }
}
