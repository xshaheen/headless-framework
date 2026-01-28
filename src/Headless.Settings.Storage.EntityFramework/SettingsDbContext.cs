// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Settings.Entities;
using Microsoft.EntityFrameworkCore;

namespace Headless.Settings.Storage.EntityFramework;

[PublicAPI]
public sealed class SettingsDbContext(DbContextOptions options) : DbContext(options), ISettingsDbContext
{
    public required DbSet<SettingValueRecord> SettingValues { get; init; }

    public required DbSet<SettingDefinitionRecord> SettingDefinitions { get; init; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.AddSettingsConfiguration();
    }
}
