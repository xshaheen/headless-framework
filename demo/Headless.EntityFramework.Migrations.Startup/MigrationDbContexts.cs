// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.EntityFrameworkCore;

namespace Headless.EntityFramework.Migrations.Startup;

internal sealed class SettingsMigrationDbContext(DbContextOptions<SettingsMigrationDbContext> options)
    : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.AddHeadlessSettings(this);
    }
}

internal sealed class PermissionsMigrationDbContext(DbContextOptions<PermissionsMigrationDbContext> options)
    : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.AddHeadlessPermissions(this);
    }
}

internal sealed class FeaturesMigrationDbContext(DbContextOptions<FeaturesMigrationDbContext> options)
    : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.AddHeadlessFeatures(this);
    }
}
