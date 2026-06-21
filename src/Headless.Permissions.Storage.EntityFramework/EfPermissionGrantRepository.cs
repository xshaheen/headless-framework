// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Domain;
using Headless.Permissions.Entities;
using Headless.Permissions.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Headless.Permissions;

/// <summary>
/// EF Core implementation of <see cref="Repositories.IPermissionGrantRepository"/> that manages permission grant
/// records through <typeparamref name="TContext"/>.
/// </summary>
/// <remarks>
/// Each operation creates a short-lived, non-tracking context from
/// <c>IDbContextFactory&lt;<typeparamref name="TContext"/>&gt;</c> and disposes it immediately.
/// Delete operations publish an <c>EntityChangedEventData&lt;PermissionGrantRecord&gt;</c> event via
/// <see cref="ILocalEventBus"/> after the row is removed to trigger cache invalidation.
/// </remarks>
/// <typeparam name="TContext">The consumer's <see cref="DbContext"/> that maps the permissions entities.</typeparam>
public sealed class EfPermissionGrantRepository<TContext>(
    IDbContextFactory<TContext> dbFactory,
    ILocalEventBus localPublisher
) : IPermissionGrantRepository
    where TContext : DbContext
{
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

    public async Task InsertAsync(PermissionGrantRecord permissionGrant, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        db.Set<PermissionGrantRecord>().Add(permissionGrant);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task InsertManyAsync(
        IEnumerable<PermissionGrantRecord> permissionGrants,
        CancellationToken cancellationToken = default
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        db.Set<PermissionGrantRecord>().AddRange(permissionGrants);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Deletes the grant record and publishes an <c>EntityChangedEventData&lt;PermissionGrantRecord&gt;</c>
    /// event via <see cref="ILocalEventBus"/> to trigger cache invalidation.
    /// </summary>
    public async Task DeleteAsync(PermissionGrantRecord permissionGrant, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        db.Set<PermissionGrantRecord>().Remove(permissionGrant);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await localPublisher
            .PublishAsync(new EntityChangedEventData<PermissionGrantRecord>(permissionGrant), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Deletes all supplied grant records and publishes an <c>EntityChangedEventData&lt;PermissionGrantRecord&gt;</c>
    /// event for each deleted record via <see cref="ILocalEventBus"/> to trigger cache invalidation.
    /// </summary>
    public async Task DeleteManyAsync(
        IReadOnlyCollection<PermissionGrantRecord> permissionGrants,
        CancellationToken cancellationToken = default
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        db.Set<PermissionGrantRecord>().RemoveRange(permissionGrants);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        foreach (var permissionGrant in permissionGrants)
        {
            await localPublisher
                .PublishAsync(new EntityChangedEventData<PermissionGrantRecord>(permissionGrant), cancellationToken)
                .ConfigureAwait(false);
        }
    }
}
