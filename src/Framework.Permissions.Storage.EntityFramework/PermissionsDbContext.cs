// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Permissions.Entities;
using Microsoft.EntityFrameworkCore;

namespace Framework.Permissions;

[PublicAPI]
public sealed class PermissionsDbContext(DbContextOptions options) : DbContext(options), IPermissionsDbContext
{
    public required DbSet<PermissionGrantRecord> PermissionGrants { get; init; }

    public required DbSet<PermissionDefinitionRecord> PermissionDefinitions { get; init; }

    public required DbSet<PermissionGroupDefinitionRecord> PermissionGroupDefinitions { get; init; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.AddPermissionsConfiguration();
    }
}
