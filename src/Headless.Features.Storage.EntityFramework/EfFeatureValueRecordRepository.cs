// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Runtime.ExceptionServices;
using Headless.Caching;
using Headless.Checks;
using Headless.UnitOfWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Headless.Features;

/// <summary>EF Core implementation of <see cref="IFeatureValueRecordRepository"/>.</summary>
/// <typeparam name="TContext">The <see cref="DbContext"/> type that owns the feature value entities.</typeparam>
/// <param name="dbFactory">Factory used to create <typeparamref name="TContext"/> instances per operation.</param>
/// <param name="valueCache">
/// The feature-value cache shared with <c>FeatureValueStore</c>, the named one when
/// <see cref="FeatureManagementOptions.FeatureValueCacheName"/> is set. A write here syncs the current value into the
/// affected cache entry (an empty item for a delete), so a direct repository write (bypassing <c>IFeatureManager</c>) is
/// reflected on the next read and a concurrent reader's older load cannot overwrite it.
/// </param>
internal sealed class EfFeatureValueRecordRepository<TContext>(
    IDbContextFactory<TContext> dbFactory,
    FeatureValueCache valueCache,
    ILogger<EfFeatureValueRecordRepository<TContext>> logger
) : IFeatureValueRecordRepository, IFeatureValueRepositoryForStore
    where TContext : DbContext
{
    private const string _VersionConflictMessage = "The feature values changed after the version was read.";

    private const string _Owner = "the EF Core features storage";
    private const string _Operation = "feature value";

    private bool WritesCache { get; init; } = true;

    IFeatureValueRecordRepository IFeatureValueRepositoryForStore.ForStore(ICache storeCache)
    {
        return ReferenceEquals(storeCache, valueCache.Cache)
            ? new EfFeatureValueRecordRepository<TContext>(dbFactory, valueCache, logger) { WritesCache = false }
            : this;
    }

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
        await _WriteCacheAsync([featureValue], []).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task UpdateAsync(FeatureValueRecord featureValue, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        db.Set<FeatureValueRecord>().Update(featureValue);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await _WriteCacheAsync([featureValue], []).ConfigureAwait(false);
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
        await _WriteCacheAsync([], featureValues).ConfigureAwait(false);
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
        await _WriteCacheAsync([.. inserted, .. updated], deleted).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<string> SaveIfVersionAsync(
        HashSet<string> names,
        string providerName,
        string? providerKey,
        string expectedVersion,
        IReadOnlyCollection<FeatureValueRecord> inserted,
        IReadOnlyCollection<FeatureValueRecord> updated,
        IReadOnlyCollection<FeatureValueRecord> deleted,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(names);
        Argument.IsNotNull(expectedVersion);

        await using var strategyContext = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var strategy = strategyContext.Database.CreateExecutionStrategy();

        var committedVersion = await RelationalVersionedWrite
            .ExecuteAsync(
                async (attempt, token) =>
                {
                    // The strategy returns every fault as a value, so it never replays an attempt whose commit reply
                    // was lost; the shared write settles that outcome instead.
                    var outcome = await strategy
                        .ExecuteAsync(
                            async ct =>
                            {
                                try
                                {
                                    await using var db = await dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
                                    await using var transaction = await db
                                        .Database.BeginTransactionAsync(IsolationLevel.Serializable, ct)
                                        .ConfigureAwait(false);
                                    var query = db.Set<FeatureValueRecord>()
                                        .AsNoTracking()
                                        .Where(s =>
                                            names.Contains(s.Name)
                                            && s.ProviderName == providerName
                                            && s.ProviderKey == providerKey
                                        );
                                    var current = await query.ToListAsync(ct).ConfigureAwait(false);
                                    var version = FeatureValueVersion.Compute(
                                        providerName,
                                        providerKey,
                                        names,
                                        current
                                    );
                                    if (!string.Equals(version, expectedVersion, StringComparison.Ordinal))
                                    {
                                        throw new DBConcurrencyException(_VersionConflictMessage);
                                    }

                                    var set = db.Set<FeatureValueRecord>();
                                    set.AddRange(inserted);
                                    set.UpdateRange(updated);
                                    set.RemoveRange(deleted);
                                    if (inserted.Count != 0 || updated.Count != 0 || deleted.Count != 0)
                                    {
                                        await db.SaveChangesAsync(ct).ConfigureAwait(false);
                                    }

                                    // Read the resulting rows before commit, not through a later GET that could observe another save.
                                    var result = FeatureValueVersion.Compute(
                                        providerName,
                                        providerKey,
                                        names,
                                        await query.ToListAsync(ct).ConfigureAwait(false)
                                    );
                                    attempt.BeginCommit(result);
                                    await transaction.CommitAsync(ct).ConfigureAwait(false);
                                    return (Version: result, Error: (ExceptionDispatchInfo?)null);
                                }
#pragma warning disable CA1031 // Returned to the shared write, which settles or rethrows it outside the strategy.
                                catch (Exception exception)
#pragma warning restore CA1031
                                {
                                    return (Version: string.Empty, Error: ExceptionDispatchInfo.Capture(exception));
                                }
                            },
                            token
                        )
                        .ConfigureAwait(false);

                    outcome.Error?.Throw();

                    return outcome.Version;
                },
                async ct =>
                    FeatureValueVersion.Compute(
                        providerName,
                        providerKey,
                        names,
                        await GetListAsync(names, providerName, providerKey, ct).ConfigureAwait(false)
                    ),
                expectedVersion,
                _VersionConflictMessage,
                cancellationToken
            )
            .ConfigureAwait(false);

        try
        {
            await _WriteCacheAsync([.. inserted, .. updated], deleted).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // The write committed; a cache fault is logged and repaired by expiry, never replayed.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            logger.LogFailedToInvalidateFeatureCache(exception, names.Count);
        }

        return committedVersion;
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

        if (!WritesCache)
        {
            return;
        }

        foreach (
            var scope in inserted
                .Concat(updated)
                .Concat(deleted)
                .GroupBy(record => (record.ProviderName, record.ProviderKey))
        )
        {
            FeatureValueCacheInvalidation.SyncAfterCommit(
                unitOfWork,
                valueCache.Cache,
                this,
                scope.Select(record => record.Name).Distinct(StringComparer.Ordinal).ToArray(),
                scope.Key.ProviderName,
                scope.Key.ProviderKey,
                logger
            );
        }
    }

    /// <summary>
    /// Writes the committed rows to the cache. It ignores the caller's token: the write is already durable, and a caller
    /// that stops waiting must not leave the cache serving the value it replaced.
    /// </summary>
    private async Task _WriteCacheAsync(
        IReadOnlyCollection<FeatureValueRecord> written,
        IReadOnlyCollection<FeatureValueRecord> deleted
    )
    {
        if (!WritesCache)
        {
            return;
        }

        foreach (var scope in written.Concat(deleted).GroupBy(record => (record.ProviderName, record.ProviderKey)))
        {
            var names = scope.Select(record => record.Name).Distinct(StringComparer.Ordinal).ToArray();

            try
            {
                await FeatureValueCacheInvalidation
                    .SyncAsync(
                        valueCache.Cache,
                        this,
                        names,
                        scope.Key.ProviderName,
                        scope.Key.ProviderKey,
                        CancellationToken.None
                    )
                    .ConfigureAwait(false);
            }
#pragma warning disable CA1031 // Cache maintenance cannot reverse the repository's durable write.
            catch (Exception exception)
#pragma warning restore CA1031
            {
                logger.LogFailedToInvalidateFeatureCache(exception, names.Length);
            }
        }
    }
}
