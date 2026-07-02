// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Permissions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Demo;

internal sealed class PermissionsDemoDbContext(
    DbContextOptions<PermissionsDemoDbContext> options,
    IOptions<PermissionsStorageOptions> storageOptions
) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.AddHeadlessPermissions(storageOptions.Value);
    }
}
