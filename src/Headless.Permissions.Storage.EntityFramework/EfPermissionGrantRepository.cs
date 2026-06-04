// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Domain;
using Headless.Permissions.Entities;
using Headless.Permissions.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Headless.Permissions;

public sealed class EfPermissionGrantRepository<TContext>(
    IDbContextFactory<TContext> dbFactory,
    ILocalEventBus localPublisher
) : IPermissionGrantRepository
    where TContext : DbContext
{
    public async Task<PermissionGrantRecord?> FindAsync(
        string name,
        string providerName,
        string providerKey,
        CancellationToken cancellationToken = default
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        return await db.Set<PermissionGrantRecord>()
            .AsNoTracking()
            .OrderBy(x => x.Id)
            .FirstOrDefaultAsync(
                s => s.Name == name && s.ProviderName == providerName && s.ProviderKey == providerKey,
                cancellationToken
            );
    }

    public async Task<List<PermissionGrantRecord>> GetListAsync(
        string providerName,
        string providerKey,
        CancellationToken cancellationToken = default
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        return await db.Set<PermissionGrantRecord>()
            .AsNoTracking()
            .Where(s => s.ProviderName == providerName && s.ProviderKey == providerKey)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<PermissionGrantRecord>> GetListAsync(
        IReadOnlyCollection<string> names,
        string providerName,
        string providerKey,
        CancellationToken cancellationToken = default
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        return await db.Set<PermissionGrantRecord>()
            .AsNoTracking()
            .Where(s => names.Contains(s.Name) && s.ProviderName == providerName && s.ProviderKey == providerKey)
            .ToListAsync(cancellationToken);
    }

    public async Task InsertAsync(PermissionGrantRecord permissionGrant, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        db.Set<PermissionGrantRecord>().Add(permissionGrant);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task InsertManyAsync(
        IEnumerable<PermissionGrantRecord> permissionGrants,
        CancellationToken cancellationToken = default
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        db.Set<PermissionGrantRecord>().AddRange(permissionGrants);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(PermissionGrantRecord permissionGrant, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        db.Set<PermissionGrantRecord>().Remove(permissionGrant);
        await db.SaveChangesAsync(cancellationToken);

        await localPublisher.PublishAsync(
            new EntityChangedEventData<PermissionGrantRecord>(permissionGrant),
            cancellationToken
        );
    }

    public async Task DeleteManyAsync(
        IReadOnlyCollection<PermissionGrantRecord> permissionGrants,
        CancellationToken cancellationToken = default
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        db.Set<PermissionGrantRecord>().RemoveRange(permissionGrants);
        await db.SaveChangesAsync(cancellationToken);

        foreach (var permissionGrant in permissionGrants)
        {
            await localPublisher.PublishAsync(
                new EntityChangedEventData<PermissionGrantRecord>(permissionGrant),
                cancellationToken
            );
        }
    }
}
