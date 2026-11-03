// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.UnitOfWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Headless.Features;

/// <summary>EF Core implementation of <see cref="IFeatureValueRecordRepository"/>.</summary>
/// <typeparam name="TContext">The <see cref="DbContext"/> type that owns the feature value entities.</typeparam>
/// <param name="dbFactory">Factory used to create <typeparamref name="TContext"/> instances per operation.</param>
/// <param name="valueCache">
/// The feature-value cache shared with <c>FeatureValueStore</c>, the named one when
/// <see cref="FeatureManagementOptions.FeatureValueCacheName"/> is set. A write here stores the written value in the
/// affected cache entry (an empty item for a delete), so a direct repository write (bypassing <c>IFeatureManager</c>) is
/// reflected on the next read and a concurrent reader's older load cannot overwrite it.
/// </param>
internal sealed class EfFeatureValueRecordRepository<TContext>(
    IDbContextFactory<TContext> dbFactory,
    FeatureValueCache valueCache,
    ILogger<EfFeatureValueRecordRepository<TContext>> logger
) : IFeatureValueRecordRepository
    where TContext : DbContext
{
    private const string _Owner = "the EF Core features storage";
    private const string _Operation = "feature value";

    private readonly ICache _cache = valueCache.Cache;

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
        HashSet<string> names,
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        return await db.Set<FeatureValueRecord>()
            .AsNoTracking()
            .Where(s => names.Contains(s.Name) && s.ProviderName == providerName && s.ProviderKey == providerKey)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
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
        await _WriteCacheAsync([featureValue], [], cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task UpdateAsync(FeatureValueRecord featureValue, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        db.Set<FeatureValueRecord>().Update(featureValue);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await _WriteCacheAsync([featureValue], [], cancellationToken).ConfigureAwait(false);
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
        await _WriteCacheAsync([], featureValues, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task SaveAsync(
        IReadOnlyCollection<FeatureValueRecord> inserted,
        IReadOnlyCollection<FeatureValueRecord> updated,
        IReadOnlyCollection<FeatureValueRecord> deleted,
        CancellationToken cancellationToken = default
    )
    {
        if (inserted.Count == 0 && updated.Count == 0 && deleted.Count == 0)
        {
            return;
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var set = db.Set<FeatureValueRecord>();
        set.AddRange(inserted);
        set.UpdateRange(updated);
        set.RemoveRange(deleted);

        // One SaveChanges runs every statement in the transaction EF opens for it, so a failing row rolls back
        // the whole batch.
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await _WriteCacheAsync([.. inserted, .. updated], deleted, cancellationToken).ConfigureAwait(false);
    }

    public Task<List<FeatureValueRecord>> GetListEnlistedAsync(
        IUnitOfWork unitOfWork,
        HashSet<string> names,
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken
    )
    {
        return EntityFrameworkEnlistment.RunAsync(
            dbFactory,
            unitOfWork,
            _Owner,
            _Operation,
            (db, ct) =>
                db.Set<FeatureValueRecord>()
                    .AsNoTracking()
                    .Where(s =>
                        names.Contains(s.Name) && s.ProviderName == providerName && s.ProviderKey == providerKey
                    )
                    .ToListAsync(ct),
            cancellationToken
        );
    }

    public Task<List<FeatureValueRecord>> GetListEnlistedAsync(
        IUnitOfWork unitOfWork,
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken
    )
    {
        return EntityFrameworkEnlistment.RunAsync(
            dbFactory,
            unitOfWork,
            _Owner,
            _Operation,
            (db, ct) =>
                db.Set<FeatureValueRecord>()
                    .AsNoTracking()
                    .Where(s => s.ProviderName == providerName && s.ProviderKey == providerKey)
                    .ToListAsync(ct),
            cancellationToken
        );
    }

    public async Task SaveEnlistedAsync(
        IUnitOfWork unitOfWork,
        IReadOnlyCollection<FeatureValueRecord> inserted,
        IReadOnlyCollection<FeatureValueRecord> updated,
        IReadOnlyCollection<FeatureValueRecord> deleted,
        CancellationToken cancellationToken
    )
    {
        if (inserted.Count == 0 && updated.Count == 0 && deleted.Count == 0)
        {
            return;
        }

        await EntityFrameworkEnlistment
            .RunAsync(
                dbFactory,
                unitOfWork,
                _Owner,
                _Operation,
                (db, ct) =>
                {
                    // The enlisted context is not the owner's, so an observed unit's owner (the EF save pipeline)
                    // cannot restore this write on replay; only an owned unit re-runs the caller's block.
                    if (unitOfWork.Resource is { IsOwned: false })
                    {
                        unitOfWork.PreventRetry();
                    }

                    var set = db.Set<FeatureValueRecord>();
                    set.AddRange(inserted);
                    set.UpdateRange(updated);
                    set.RemoveRange(deleted);

                    return db.SaveChangesAsync(ct);
                },
                cancellationToken
            )
            .ConfigureAwait(false);

        FeatureValueCacheInvalidation.UpsertAfterCommit(
            unitOfWork,
            _cache,
            FeatureValueCacheInvalidation.WrittenItems([.. inserted, .. updated], deleted),
            logger
        );
    }

    private async Task _WriteCacheAsync(
        IReadOnlyCollection<FeatureValueRecord> written,
        IReadOnlyCollection<FeatureValueRecord> deleted,
        CancellationToken cancellationToken
    )
    {
        await _cache
            .UpsertAllAsync(
                FeatureValueCacheInvalidation.WrittenItems(written, deleted),
                FeatureValueCacheInvalidation.Expiration,
                cancellationToken
            )
            .ConfigureAwait(false);
    }
}
