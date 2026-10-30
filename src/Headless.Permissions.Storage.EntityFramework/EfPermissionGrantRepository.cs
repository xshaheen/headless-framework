// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.MultiTenancy;
using Headless.UnitOfWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Headless.Permissions;

/// <summary>
/// EF Core implementation of <see cref="IPermissionGrantRepository"/> that manages permission grant
/// records through <typeparamref name="TContext"/>.
/// </summary>
/// <remarks>
/// Each operation creates a short-lived, non-tracking context from
/// <c>IDbContextFactory&lt;<typeparamref name="TContext"/>&gt;</c> and disposes it immediately. An enlisted operation's
/// context runs on the caller's unit-of-work connection and transaction instead, and its cache removal waits for the
/// unit to commit.
/// </remarks>
/// <typeparam name="TContext">The consumer's <see cref="DbContext"/> that maps the permissions entities.</typeparam>
/// <param name="dbFactory">Factory used to create <typeparamref name="TContext"/> instances per operation.</param>
/// <param name="cache">
/// The permission-grant cache shared with <c>PermissionGrantStore</c>. A write here removes the affected cache
/// key so a direct repository write (bypassing <c>IPermissionManager</c>) is reflected on the next read.
/// </param>
/// <param name="currentTenant">Restores the write's tenant for a cache removal that runs after the unit commits.</param>
/// <param name="logger">Logs a cache removal that fails after the unit committed.</param>
internal sealed class EfPermissionGrantRepository<TContext>(
    IDbContextFactory<TContext> dbFactory,
    ICache<PermissionGrantCacheItem> cache,
    ICurrentTenant currentTenant,
    ILogger<EfPermissionGrantRepository<TContext>> logger
) : IPermissionGrantRepository
    where TContext : DbContext
{
    private const string _Owner = "the EF Core permissions storage";
    private const string _Operation = "permission grant";

    /// <summary>
    /// Returns the first grant record matching <paramref name="name"/>, <paramref name="providerName"/>,
    /// and <paramref name="providerKey"/>, ordered by <c>Id</c>, or <see langword="null"/> if none exists.
    /// </summary>
    public async Task<PermissionGrantRecord?> FindAsync(
        string name,
        string providerName,
        string providerKey,
        CancellationToken cancellationToken = default
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        return await db.Set<PermissionGrantRecord>()
            .AsNoTracking()
            .OrderBy(x => x.Id)
            .FirstOrDefaultAsync(
                s => s.Name == name && s.ProviderName == providerName && s.ProviderKey == providerKey,
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <summary>Returns all grant records for the given provider target, untracked.</summary>
    public async Task<List<PermissionGrantRecord>> GetListAsync(
        string providerName,
        string providerKey,
        CancellationToken cancellationToken = default
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        return await db.Set<PermissionGrantRecord>()
            .AsNoTracking()
            .Where(s => s.ProviderName == providerName && s.ProviderKey == providerKey)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Returns grant records whose permission name is in <paramref name="names"/> for the given provider
    /// target, untracked. Filters in-database with a <c>Contains</c> predicate.
    /// </summary>
    public async Task<List<PermissionGrantRecord>> GetListAsync(
        IReadOnlyCollection<string> names,
        string providerName,
        string providerKey,
        CancellationToken cancellationToken = default
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        return await db.Set<PermissionGrantRecord>()
            .AsNoTracking()
            .Where(s => names.Contains(s.Name) && s.ProviderName == providerName && s.ProviderKey == providerKey)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Returns grant records whose permission name is in <paramref name="names"/> and whose provider key is
    /// in <paramref name="providerKeys"/>, untracked. Filters in-database with <c>Contains</c> predicates.
    /// </summary>
    public async Task<List<PermissionGrantRecord>> GetListAsync(
        IReadOnlyCollection<string> names,
        string providerName,
        IReadOnlyCollection<string> providerKeys,
        CancellationToken cancellationToken = default
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        return await db.Set<PermissionGrantRecord>()
            .AsNoTracking()
            .Where(s =>
                names.Contains(s.Name) && s.ProviderName == providerName && providerKeys.Contains(s.ProviderKey)
            )
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task InsertAsync(PermissionGrantRecord permissionGrant, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        db.Set<PermissionGrantRecord>().Add(permissionGrant);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await cache.RemoveAsync(_CacheKey(permissionGrant), cancellationToken).ConfigureAwait(false);
    }

    public async Task InsertManyAsync(
        IEnumerable<PermissionGrantRecord> permissionGrants,
        CancellationToken cancellationToken = default
    )
    {
        var grants = permissionGrants as IReadOnlyCollection<PermissionGrantRecord> ?? [.. permissionGrants];

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        db.Set<PermissionGrantRecord>().AddRange(grants);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await cache.RemoveAllAsync(grants.Select(_CacheKey), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Deletes the grant record.</summary>
    public async Task DeleteAsync(PermissionGrantRecord permissionGrant, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        db.Set<PermissionGrantRecord>().Remove(permissionGrant);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await cache.RemoveAsync(_CacheKey(permissionGrant), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Deletes all supplied grant records.</summary>
    public async Task DeleteManyAsync(
        IReadOnlyCollection<PermissionGrantRecord> permissionGrants,
        CancellationToken cancellationToken = default
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        db.Set<PermissionGrantRecord>().RemoveRange(permissionGrants);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await cache.RemoveAllAsync(permissionGrants.Select(_CacheKey), cancellationToken).ConfigureAwait(false);
    }

    public Task<List<PermissionGrantRecord>> GetListEnlistedAsync(
        IUnitOfWork unitOfWork,
        string providerName,
        string providerKey,
        CancellationToken cancellationToken
    )
    {
        return EntityFrameworkEnlistment.RunAsync(
            dbFactory,
            unitOfWork,
            _Owner,
            _Operation,
            (db, ct) =>
                db.Set<PermissionGrantRecord>()
                    .AsNoTracking()
                    .Where(s => s.ProviderName == providerName && s.ProviderKey == providerKey)
                    .ToListAsync(ct),
            cancellationToken
        );
    }

    public Task<List<PermissionGrantRecord>> GetListEnlistedAsync(
        IUnitOfWork unitOfWork,
        IReadOnlyCollection<string> names,
        string providerName,
        string providerKey,
        CancellationToken cancellationToken
    )
    {
        return EntityFrameworkEnlistment.RunAsync(
            dbFactory,
            unitOfWork,
            _Owner,
            _Operation,
            (db, ct) =>
                db.Set<PermissionGrantRecord>()
                    .AsNoTracking()
                    .Where(s =>
                        names.Contains(s.Name) && s.ProviderName == providerName && s.ProviderKey == providerKey
                    )
                    .ToListAsync(ct),
            cancellationToken
        );
    }

    public async Task InsertManyEnlistedAsync(
        IUnitOfWork unitOfWork,
        IEnumerable<PermissionGrantRecord> permissionGrants,
        CancellationToken cancellationToken
    )
    {
        var grants = permissionGrants as IReadOnlyCollection<PermissionGrantRecord> ?? [.. permissionGrants];

        await EntityFrameworkEnlistment
            .RunAsync(
                dbFactory,
                unitOfWork,
                _Owner,
                _Operation,
                (db, ct) =>
                {
                    _PreventRetryWhenObserved(unitOfWork);
                    db.Set<PermissionGrantRecord>().AddRange(grants);

                    return db.SaveChangesAsync(ct);
                },
                cancellationToken
            )
            .ConfigureAwait(false);

        _RemoveFromCacheAfterCommit(unitOfWork, grants);
    }

    public async Task DeleteManyEnlistedAsync(
        IUnitOfWork unitOfWork,
        IReadOnlyCollection<PermissionGrantRecord> permissionGrants,
        CancellationToken cancellationToken
    )
    {
        await EntityFrameworkEnlistment
            .RunAsync(
                dbFactory,
                unitOfWork,
                _Owner,
                _Operation,
                (db, ct) =>
                {
                    _PreventRetryWhenObserved(unitOfWork);
                    db.Set<PermissionGrantRecord>().RemoveRange(permissionGrants);

                    return db.SaveChangesAsync(ct);
                },
                cancellationToken
            )
            .ConfigureAwait(false);

        _RemoveFromCacheAfterCommit(unitOfWork, permissionGrants);
    }

    // The enlisted context is not the owner's, so an observed unit's owner (the EF save pipeline) cannot restore this
    // write on replay; only an owned unit re-runs the caller's block. Marked once the enlistment checks passed.
    private static void _PreventRetryWhenObserved(IUnitOfWork unitOfWork)
    {
        if (unitOfWork.Resource is { IsOwned: false })
        {
            unitOfWork.PreventRetry();
        }
    }

    private void _RemoveFromCacheAfterCommit(IUnitOfWork unitOfWork, IReadOnlyCollection<PermissionGrantRecord> grants)
    {
        PermissionGrantCacheInvalidation.RemoveAfterCommit(
            unitOfWork,
            cache,
            currentTenant,
            [.. grants.Select(_CacheKey)],
            logger
        );
    }

    private static string _CacheKey(PermissionGrantRecord permissionGrant)
    {
        return PermissionGrantCacheItem.CalculateCacheKey(
            permissionGrant.Name,
            permissionGrant.ProviderName,
            permissionGrant.ProviderKey
        );
    }
}
