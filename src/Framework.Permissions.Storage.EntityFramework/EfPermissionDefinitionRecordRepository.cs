// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Permissions.Definitions;
using Framework.Permissions.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Framework.Permissions.Storage.EntityFramework;

public sealed class EfPermissionDefinitionRecordRepository(IServiceScopeFactory scopeFactory)
    : IPermissionDefinitionRecordRepository
{
    public async Task<List<PermissionGroupDefinitionRecord>> GetGroupsListAsync(
        CancellationToken cancellationToken = default
    )
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PermissionsDbContext>();

        return await db.PermissionGroupDefinitions.ToListAsync(cancellationToken);
    }

    public async Task<List<PermissionDefinitionRecord>> GetPermissionsListAsync(
        CancellationToken cancellationToken = default
    )
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PermissionsDbContext>();

        return await db.PermissionDefinitions.ToListAsync(cancellationToken);
    }

    public async Task SaveAsync(
        List<PermissionGroupDefinitionRecord> newGroups,
        List<PermissionGroupDefinitionRecord> updatedGroups,
        List<PermissionGroupDefinitionRecord> deletedGroups,
        List<PermissionDefinitionRecord> newPermissions,
        List<PermissionDefinitionRecord> updatedPermissions,
        List<PermissionDefinitionRecord> deletedPermissions,
        CancellationToken cancellationToken = default
    )
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PermissionsDbContext>();

        db.PermissionGroupDefinitions.AddRange(newGroups);
        db.PermissionGroupDefinitions.UpdateRange(updatedGroups);
        db.PermissionGroupDefinitions.RemoveRange(deletedGroups);

        db.PermissionDefinitions.AddRange(newPermissions);
        db.PermissionDefinitions.UpdateRange(updatedPermissions);
        db.PermissionDefinitions.RemoveRange(deletedPermissions);

        await db.SaveChangesAsync(cancellationToken);
    }
}
