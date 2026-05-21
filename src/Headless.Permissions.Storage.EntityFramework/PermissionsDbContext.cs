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

internal sealed class PermissionsStorageModelCacheKeyFactory : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime)
    {
        if (context is not PermissionsDbContext)
        {
            return (context.GetType(), designTime);
        }

        var options = context.GetService<IOptions<PermissionsStorageOptions>>().Value;

        return (
            context.GetType(),
            designTime,
            options.Schema,
            options.PermissionGrantsTableName,
            options.PermissionDefinitionsTableName,
            options.PermissionGroupDefinitionsTableName
        );
    }
}
