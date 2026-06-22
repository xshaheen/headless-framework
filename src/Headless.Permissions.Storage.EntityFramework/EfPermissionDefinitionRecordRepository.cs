// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Permissions.Entities;
using Headless.Permissions.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Headless.Permissions;

/// <summary>
/// EF Core implementation of <see cref="Repositories.IPermissionDefinitionRecordRepository"/> that reads and writes
/// permission definition and group records through <typeparamref name="TContext"/>.
/// </summary>
/// <remarks>
/// Each operation acquires a short-lived, non-tracking context from <c>IDbContextFactory&lt;<typeparamref name="TContext"/>&gt;</c>
/// and disposes it immediately after the call — the repository itself is a singleton and holds no
/// open connection between calls.
/// </remarks>
/// <typeparam name="TContext">The consumer's <see cref="DbContext"/> that maps the permissions entities.</typeparam>
public sealed class EfPermissionDefinitionRecordRepository<TContext>(IDbContextFactory<TContext> dbFactory)
    : IPermissionDefinitionRecordRepository
    where TContext : DbContext
{
    /// <summary>Returns all permission group definition records, untracked.</summary>
    public async Task<List<PermissionGroupDefinitionRecord>> GetGroupsListAsync(
        CancellationToken cancellationToken = default
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        return await db.Set<PermissionGroupDefinitionRecord>()
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Returns all permission definition records, untracked.</summary>
    public async Task<List<PermissionDefinitionRecord>> GetPermissionsListAsync(
        CancellationToken cancellationToken = default
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        return await db.Set<PermissionDefinitionRecord>()
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Applies inserts, updates, and deletes for both permission groups and permissions in a single
    /// <c>SaveChangesAsync</c> call. All six lists are flushed atomically within the same EF change-tracking
    /// unit of work.
    /// </summary>
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
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        db.Set<PermissionGroupDefinitionRecord>().AddRange(newGroups);
        db.Set<PermissionGroupDefinitionRecord>().UpdateRange(updatedGroups);
        db.Set<PermissionGroupDefinitionRecord>().RemoveRange(deletedGroups);

        db.Set<PermissionDefinitionRecord>().AddRange(newPermissions);
        db.Set<PermissionDefinitionRecord>().UpdateRange(updatedPermissions);
        db.Set<PermissionDefinitionRecord>().RemoveRange(deletedPermissions);

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
