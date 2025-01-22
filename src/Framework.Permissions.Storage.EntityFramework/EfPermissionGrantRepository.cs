// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Domains;
using Framework.Messaging;
using Framework.Permissions.Entities;
using Framework.Permissions.Grants;
using Microsoft.EntityFrameworkCore;

namespace Framework.Permissions;

public sealed class EfPermissionGrantRepository<TContext>(
    IDbContextFactory<TContext> dbFactory,
    ILocalMessagePublisher localPublisher
) : IPermissionGrantRepository
    where TContext : DbContext, IPermissionsDbContext
{
    public async Task<PermissionGrantRecord?> FindAsync(
        string name,
        string providerName,
        string providerKey,
        CancellationToken cancellationToken = default
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        return await db
            .PermissionGrants.OrderBy(x => x.Id)
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

        return await db
            .PermissionGrants.Where(s => s.ProviderName == providerName && s.ProviderKey == providerKey)
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

        return await db
            .PermissionGrants.Where(s =>
                names.Contains(s.Name) && s.ProviderName == providerName && s.ProviderKey == providerKey
            )
            .ToListAsync(cancellationToken);
    }

    public async Task InsertAsync(PermissionGrantRecord permissionGrant, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        db.PermissionGrants.Add(permissionGrant);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task InsertManyAsync(
        IEnumerable<PermissionGrantRecord> permissionGrants,
        CancellationToken cancellationToken = default
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        db.PermissionGrants.AddRange(permissionGrants);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(PermissionGrantRecord permissionGrant, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        db.PermissionGrants.Remove(permissionGrant);
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

        db.PermissionGrants.RemoveRange(permissionGrants);
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
