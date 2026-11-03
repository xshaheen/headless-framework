// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.MultiTenancy;
using Headless.UnitOfWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Permissions;

/// <summary>
/// EF Core implementation of <see cref="IPermissionGrantRepository"/> that manages permission grant
/// records through <typeparamref name="TContext"/>.
/// </summary>
/// <remarks>
/// <para>
/// Each operation creates a short-lived, non-tracking context from
/// <c>IDbContextFactory&lt;<typeparamref name="TContext"/>&gt;</c> and disposes it immediately. An enlisted operation's
/// context runs on the caller's unit-of-work connection and transaction instead, and its cache write waits for the
/// unit to commit.
/// </para>
/// <para>
/// Reads and deletes are scoped to the current tenant, the same as the relational repository: a null tenant matches
/// only the host's rows. The repository applies the predicate itself rather than relying on a context query filter,
/// because a plain <see cref="DbContext"/> has none and the same provider target (a role name such as <c>admin</c>)
/// repeats across tenants.
/// </para>
/// </remarks>
/// <typeparam name="TContext">The consumer's <see cref="DbContext"/> that maps the permissions entities.</typeparam>
/// <param name="dbFactory">Factory used to create <typeparamref name="TContext"/> instances per operation.</param>
/// <param name="cache">
/// The permission-grant cache shared with <c>PermissionGrantStore</c>. A write here stores the written status in the
/// affected cache entry (undefined for a delete), so a direct repository write (bypassing <c>IPermissionManager</c>) is
/// reflected on the next read. Storing rather than removing keeps out a reader that loaded the rows before the write:
/// readers fill only an absent entry.
/// </param>
/// <param name="currentTenant">
/// Scopes reads and deletes to the current tenant, and restores the write's tenant for a cache write that runs after
/// the unit commits.
/// </param>
/// <param name="managementOptions">Supplies the cache entry lifetime the grant store uses.</param>
/// <param name="logger">Logs a cache write that fails after the unit committed.</param>
/// <param name="writeGuardBypass">
/// Lets a host write (no ambient tenant, records without a tenant) through the Headless tenant write guard; absent when
/// the host does not use Headless EF Core.
/// </param>
internal sealed class EfPermissionGrantRepository<TContext>(
    IDbContextFactory<TContext> dbFactory,
    ICache<PermissionGrantCacheItem> cache,
    ICurrentTenant currentTenant,
    IOptions<PermissionManagementOptions> managementOptions,
    ILogger<EfPermissionGrantRepository<TContext>> logger,
    ITenantWriteGuardBypass? writeGuardBypass = null
) : IPermissionGrantRepository
    where TContext : DbContext
{
    private const string _Owner = "the EF Core permissions storage";
    private const string _Operation = "permission grant";

    private readonly TimeSpan _cacheExpiration = managementOptions.Value.GrantCacheExpiration;

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

        return await _Grants(db)
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

        return await _Grants(db)
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

        return await _Grants(db)
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

        return await _Grants(db)
            .Where(s =>
                names.Contains(s.Name) && s.ProviderName == providerName && providerKeys.Contains(s.ProviderKey)
            )
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task InsertAsync(PermissionGrantRecord permissionGrant, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        await _WriteAsync(db, [permissionGrant], remove: false, cancellationToken).ConfigureAwait(false);
        await _WriteCacheAsync([permissionGrant], [], cancellationToken).ConfigureAwait(false);
    }

    public async Task InsertManyAsync(
        IEnumerable<PermissionGrantRecord> permissionGrants,
        CancellationToken cancellationToken = default
    )
    {
        var grants = permissionGrants as IReadOnlyCollection<PermissionGrantRecord> ?? [.. permissionGrants];

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        await _WriteAsync(db, grants, remove: false, cancellationToken).ConfigureAwait(false);
        await _WriteCacheAsync(grants, [], cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Deletes the grant record when it belongs to the current tenant; another tenant's record is left alone.</summary>
    public Task DeleteAsync(PermissionGrantRecord permissionGrant, CancellationToken cancellationToken)
    {
        return DeleteManyAsync([permissionGrant], cancellationToken);
    }

    /// <summary>
    /// Deletes the supplied grant records that belong to the current tenant; another tenant's records are left alone.
    /// </summary>
    public async Task DeleteManyAsync(
        IReadOnlyCollection<PermissionGrantRecord> permissionGrants,
        CancellationToken cancellationToken = default
    )
    {
        var owned = _OwnedByCurrentTenant(permissionGrants);

        if (owned.Count == 0)
        {
            return;
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        await _WriteAsync(db, owned, remove: true, cancellationToken).ConfigureAwait(false);
        await _WriteCacheAsync([], owned, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Deletes the supplied records that belong to the current tenant and inserts the new ones in one
    /// <c>SaveChanges</c>, so both commit or neither does.
    /// </summary>
    public async Task SaveAsync(
        IReadOnlyCollection<PermissionGrantRecord> inserted,
        IReadOnlyCollection<PermissionGrantRecord> deleted,
        CancellationToken cancellationToken = default
    )
    {
        var owned = _OwnedByCurrentTenant(deleted);

        if (inserted.Count == 0 && owned.Count == 0)
        {
            return;
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        using (_BeginHostWrite([.. inserted, .. owned]))
        {
            var set = db.Set<PermissionGrantRecord>();
            set.RemoveRange(owned);
            set.AddRange(inserted);

            // One SaveChanges runs every statement in the transaction EF opens for it, and EF orders a delete before
            // an insert that reuses its unique key, so a failing insert rolls the delete back.
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        await cache
            .RemoveAllAsync(
                inserted.Concat(owned).Select(_CacheKey).Distinct(StringComparer.Ordinal),
                cancellationToken
            )
            .ConfigureAwait(false);
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
                _Grants(db).Where(s => s.ProviderName == providerName && s.ProviderKey == providerKey).ToListAsync(ct),
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
                _Grants(db)
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

                    return _WriteAsync(db, grants, remove: false, ct);
                },
                cancellationToken
            )
            .ConfigureAwait(false);

        _WriteCacheAfterCommit(unitOfWork, grants, []);
    }

    public async Task DeleteManyEnlistedAsync(
        IUnitOfWork unitOfWork,
        IReadOnlyCollection<PermissionGrantRecord> permissionGrants,
        CancellationToken cancellationToken
    )
    {
        var owned = _OwnedByCurrentTenant(permissionGrants);

        if (owned.Count == 0)
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
                    _PreventRetryWhenObserved(unitOfWork);

                    return _WriteAsync(db, owned, remove: true, ct);
                },
                cancellationToken
            )
            .ConfigureAwait(false);

        _WriteCacheAfterCommit(unitOfWork, [], owned);
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

    private void _WriteCacheAfterCommit(
        IUnitOfWork unitOfWork,
        IReadOnlyCollection<PermissionGrantRecord> written,
        IReadOnlyCollection<PermissionGrantRecord> deleted
    )
    {
        PermissionGrantCacheInvalidation.UpsertAfterCommit(
            unitOfWork,
            cache,
            currentTenant,
            PermissionGrantCacheInvalidation.WrittenItems(written, deleted),
            _cacheExpiration,
            logger
        );
    }

    private async Task _WriteCacheAsync(
        IReadOnlyCollection<PermissionGrantRecord> written,
        IReadOnlyCollection<PermissionGrantRecord> deleted,
        CancellationToken cancellationToken
    )
    {
        await cache
            .UpsertAllAsync(
                PermissionGrantCacheInvalidation.WrittenItems(written, deleted),
                _cacheExpiration,
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <summary>The grant rows of the current tenant only; a null tenant selects the host's rows.</summary>
    private IQueryable<PermissionGrantRecord> _Grants(TContext db)
    {
        var tenantId = currentTenant.Id;
        var grants = db.Set<PermissionGrantRecord>().AsNoTracking();

        // Two shapes instead of one null-safe comparison: each matches one of the filtered unique indexes, and neither
        // depends on the context's null-comparison semantics.
        return tenantId is null ? grants.Where(x => x.TenantId == null) : grants.Where(x => x.TenantId == tenantId);
    }

    private List<PermissionGrantRecord> _OwnedByCurrentTenant(IEnumerable<PermissionGrantRecord> permissionGrants)
    {
        var tenantId = currentTenant.Id;

        return [.. permissionGrants.Where(x => string.Equals(x.TenantId, tenantId, StringComparison.Ordinal))];
    }

    /// <summary>Tracks and saves the records, inside the host-write bypass when they are host records.</summary>
    private async Task<int> _WriteAsync(
        TContext db,
        IReadOnlyCollection<PermissionGrantRecord> grants,
        bool remove,
        CancellationToken cancellationToken
    )
    {
        // The bypass opens before tracking: a HeadlessDbContext checks an added tenant-owned entry's tenant when the
        // entry is tracked, not only when it is saved.
        using (_BeginHostWrite(grants))
        {
            if (remove)
            {
                db.Set<PermissionGrantRecord>().RemoveRange(grants);
            }
            else
            {
                db.Set<PermissionGrantRecord>().AddRange(grants);
            }

            return await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Opens the tenant write guard for a host write: no ambient tenant and only records without a tenant. Host grants
    /// are a supported shape (the same grant can exist per tenant and for the host), but the guard refuses every
    /// tenant-owned write made without an ambient tenant. A write that names a tenant stays guarded.
    /// </summary>
    private IDisposable? _BeginHostWrite(IReadOnlyCollection<PermissionGrantRecord> grants)
    {
        if (writeGuardBypass is null || currentTenant.Id is not null || grants.Any(x => x.TenantId is not null))
        {
            return null;
        }

        return writeGuardBypass.BeginBypass();
    }
}
