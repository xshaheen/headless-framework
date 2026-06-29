// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Features;
using Headless.Permissions;
using Headless.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Headless.EntityFramework.Migrations.Startup;

internal sealed class SettingsMigrationDbContext(
    DbContextOptions<SettingsMigrationDbContext> options,
    IOptions<SettingsStorageOptions> storageOptions
) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.AddHeadlessSettings(storageOptions.Value);
    }
}

internal sealed class PermissionsMigrationDbContext(
    DbContextOptions<PermissionsMigrationDbContext> options,
    IOptions<PermissionsStorageOptions> storageOptions
) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.AddHeadlessPermissions(storageOptions.Value);
    }
}

internal sealed class FeaturesMigrationDbContext(
    DbContextOptions<FeaturesMigrationDbContext> options,
    IOptions<FeaturesStorageOptions> storageOptions
) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.AddHeadlessFeatures(storageOptions.Value);
    }
}
