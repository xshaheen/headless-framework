// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.EntityFrameworkCore;

namespace Headless.EntityFramework.Migrations.Startup;

internal sealed class SettingsMigrationDbContext(DbContextOptions<SettingsMigrationDbContext> options)
    : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ConfigureHeadlessSettings(this);
    }
}

internal sealed class PermissionsMigrationDbContext(DbContextOptions<PermissionsMigrationDbContext> options)
    : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ConfigureHeadlessPermissions(this);
    }
}

internal sealed class FeaturesMigrationDbContext(DbContextOptions<FeaturesMigrationDbContext> options)
    : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ConfigureHeadlessFeatures(this);
    }
}
