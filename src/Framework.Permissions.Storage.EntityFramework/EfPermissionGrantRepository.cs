// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Permissions.Entities;
using Framework.Permissions.Values;
using Microsoft.EntityFrameworkCore;

namespace Framework.Permissions.Storage.EntityFramework;

public sealed class EfPermissionGrantRepository(IDbContextFactory<PermissionsDbContext> dbFactory)
    : IPermissionGrantRepository
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
    }

    public async Task DeleteManyAsync(
        IEnumerable<PermissionGrantRecord> permissionGrants,
        CancellationToken cancellationToken = default
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        db.PermissionGrants.RemoveRange(permissionGrants);
        await db.SaveChangesAsync(cancellationToken);
    }
}
