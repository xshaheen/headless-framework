// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Runtime.ExceptionServices;
using Headless.Caching;
using Headless.Checks;
using Headless.UnitOfWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Settings;

/// <summary>
/// EF Core implementation of <see cref="ISettingValueRecordRepository"/> that stores
/// <see cref="SettingValueRecord"/> entities via a pooled <typeparamref name="TContext"/>.
/// </summary>
/// <typeparam name="TContext">The <see cref="DbContext"/> type registered with the DI container.</typeparam>
/// <param name="dbFactory">Factory used to create <typeparamref name="TContext"/> instances per operation.</param>
/// <param name="cache">
/// The setting-value cache shared with <c>SettingValueStore</c>. A write here stores the written value in the affected
/// cache entry (an empty item for a delete), so a direct repository write (bypassing <c>ISettingManager</c>) is
/// reflected on the next read and a concurrent reader's older load cannot overwrite it.
/// </param>
/// <param name="options">Supplies the cache entry lifetime the value store uses.</param>
internal sealed class EfSettingValueRecordRepository<TContext>(
    IDbContextFactory<TContext> dbFactory,
    ICache<SettingValueCacheItem> cache,
    IOptions<SettingManagementOptions> options,
    ILogger<EfSettingValueRecordRepository<TContext>> logger
) : ISettingValueRecordRepository
    where TContext : DbContext
{
    private const string _VersionConflictMessage = "The setting values changed after the version was read.";

    private const string _Owner = "the EF Core settings storage";
    private const string _Operation = "setting value";

    private readonly TimeSpan _cacheExpiration = options.Value.ValueCacheExpiration;

    /// <inheritdoc/>
    public async Task<SettingValueRecord?> FindAsync(
        string name,
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        return await db.Set<SettingValueRecord>()
            .AsNoTracking()
            .OrderBy(x => x.Id)
            .FirstOrDefaultAsync(
                s => s.Name == name && s.ProviderName == providerName && s.ProviderKey == providerKey,
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<List<SettingValueRecord>> FindAllAsync(
        string name,
        string? providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var query = db.Set<SettingValueRecord>().AsNoTracking().Where(s => s.Name == name);

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
    public async Task<List<SettingValueRecord>> GetListAsync(
        HashSet<string> names,
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.Set<SettingValueRecord>()
            .AsNoTracking()
            .Where(s => names.Contains(s.Name) && s.ProviderName == providerName && s.ProviderKey == providerKey)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<List<SettingValueRecord>> GetListAsync(
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        return await db.Set<SettingValueRecord>()
            .AsNoTracking()
            .Where(s => s.ProviderName == providerName && s.ProviderKey == providerKey)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task InsertAsync(SettingValueRecord setting, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        db.Set<SettingValueRecord>().Add(setting);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await _WriteCacheAsync([setting], []).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task UpdateAsync(SettingValueRecord setting, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        db.Set<SettingValueRecord>().Update(setting);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await _WriteCacheAsync([setting], []).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(
        IReadOnlyCollection<SettingValueRecord> settings,
        CancellationToken cancellationToken = default
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        db.Set<SettingValueRecord>().RemoveRange(settings);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await _WriteCacheAsync([], settings).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task SaveAsync(
        IReadOnlyCollection<SettingValueRecord> inserted,
        IReadOnlyCollection<SettingValueRecord> updated,
        IReadOnlyCollection<SettingValueRecord> deleted,
        CancellationToken cancellationToken = default
    )
    {
        if (inserted.Count == 0 && updated.Count == 0 && deleted.Count == 0)
        {
            return;
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var set = db.Set<SettingValueRecord>();
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
        IReadOnlyCollection<SettingValueRecord> inserted,
        IReadOnlyCollection<SettingValueRecord> updated,
        IReadOnlyCollection<SettingValueRecord> deleted,
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
                                    var query = db.Set<SettingValueRecord>()
                                        .AsNoTracking()
                                        .Where(s =>
                                            names.Contains(s.Name)
                                            && s.ProviderName == providerName
                                            && s.ProviderKey == providerKey
                                        );
                                    var current = await query.ToListAsync(ct).ConfigureAwait(false);
                                    var version = SettingValueVersion.Compute(
                                        providerName,
                                        providerKey,
                                        names,
                                        current
                                    );
                                    if (!string.Equals(version, expectedVersion, StringComparison.Ordinal))
                                    {
                                        throw new DBConcurrencyException(_VersionConflictMessage);
                                    }

                                    var set = db.Set<SettingValueRecord>();
                                    set.AddRange(inserted);
                                    set.UpdateRange(updated);
                                    set.RemoveRange(deleted);
                                    if (inserted.Count != 0 || updated.Count != 0 || deleted.Count != 0)
                                    {
                                        await db.SaveChangesAsync(ct).ConfigureAwait(false);
                                    }

                                    // Read the resulting rows before commit, not through a later GET that could observe another save.
                                    var result = SettingValueVersion.Compute(
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
                    SettingValueVersion.Compute(
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
            logger.LogFailedToInvalidateSettingCache(exception, names.Count);
        }

        return committedVersion;
    }

    public Task<List<SettingValueRecord>> GetListEnlistedAsync(
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
                db.Set<SettingValueRecord>()
                    .AsNoTracking()
                    .Where(s =>
                        names.Contains(s.Name) && s.ProviderName == providerName && s.ProviderKey == providerKey
                    )
                    .ToListAsync(ct),
            cancellationToken
        );
    }

    public Task<List<SettingValueRecord>> GetListEnlistedAsync(
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
                db.Set<SettingValueRecord>()
                    .AsNoTracking()
                    .Where(s => s.ProviderName == providerName && s.ProviderKey == providerKey)
                    .ToListAsync(ct),
            cancellationToken
        );
    }

    public async Task SaveEnlistedAsync(
        IUnitOfWork unitOfWork,
        IReadOnlyCollection<SettingValueRecord> inserted,
        IReadOnlyCollection<SettingValueRecord> updated,
        IReadOnlyCollection<SettingValueRecord> deleted,
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

                    var set = db.Set<SettingValueRecord>();
                    set.AddRange(inserted);
                    set.UpdateRange(updated);
                    set.RemoveRange(deleted);

                    return db.SaveChangesAsync(ct);
                },
                cancellationToken
            )
            .ConfigureAwait(false);

        SettingValueCacheInvalidation.UpsertAfterCommit(
            unitOfWork,
            cache,
            SettingValueCacheInvalidation.WrittenItems([.. inserted, .. updated], deleted),
            _cacheExpiration,
            logger
        );
    }

    /// <summary>
    /// Writes the committed rows to the cache. It ignores the caller's token: the write is already durable, and a caller
    /// that stops waiting must not leave the cache serving the value it replaced.
    /// </summary>
    private async Task _WriteCacheAsync(
        IReadOnlyCollection<SettingValueRecord> written,
        IReadOnlyCollection<SettingValueRecord> deleted
    )
    {
        await cache
            .UpsertAllAsync(
                SettingValueCacheInvalidation.WrittenItems(written, deleted),
                _cacheExpiration,
                CancellationToken.None
            )
            .ConfigureAwait(false);
    }
}
