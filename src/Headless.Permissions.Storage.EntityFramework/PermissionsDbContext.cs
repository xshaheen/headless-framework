// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Permissions.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Options;

namespace Headless.Permissions.Storage.EntityFramework;

[PublicAPI]
public sealed class PermissionsDbContext(DbContextOptions options) : DbContext(options), IPermissionsDbContext
{
    public required DbSet<PermissionGrantRecord> PermissionGrants { get; init; }

    public required DbSet<PermissionDefinitionRecord> PermissionDefinitions { get; init; }

    public required DbSet<PermissionGroupDefinitionRecord> PermissionGroupDefinitions { get; init; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        var storageOptions = this.GetService<IOptions<PermissionsStorageOptions>>().Value;
        modelBuilder.AddPermissionsConfiguration(storageOptions);
    }
}
