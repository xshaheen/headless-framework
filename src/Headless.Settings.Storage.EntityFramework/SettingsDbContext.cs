// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Settings.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Options;

namespace Headless.Settings.Storage.EntityFramework;

[PublicAPI]
public sealed class SettingsDbContext(DbContextOptions options) : DbContext(options), ISettingsDbContext
{
    public required DbSet<SettingValueRecord> SettingValues { get; init; }

    public required DbSet<SettingDefinitionRecord> SettingDefinitions { get; init; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.AddSettingsConfiguration(this);
    }
}

[PublicAPI]
public sealed class SettingsStorageModelCacheKeyFactory : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime)
    {
        var options = context.GetService<IOptions<SettingsStorageOptions>>().Value;

        return (
            context.GetType(),
            designTime,
            options.Schema,
            options.SettingValuesTableName,
            options.SettingDefinitionsTableName
        );
    }
}
