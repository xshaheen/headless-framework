// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Settings.Entities;
using Microsoft.EntityFrameworkCore;

namespace Framework.Settings.Storage.EntityFramework;

[PublicAPI]
public sealed class SettingsDbContext : DbContext
{
    public static string DefaultSchema { get; set; } = "settings";
    public static string DefaultSettingValuesTableName { get; set; } = "SettingValues";
    public static string DefaultSettingDefinitionTableName { get; set; } = "SettingDefinitions";

    public required DbSet<SettingValueRecord> SettingValues { get; init; }

    public required DbSet<SettingDefinitionRecord> SettingDefinitions { get; init; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<SettingValueRecord>(b =>
        {
            b.ToTable(DefaultSettingValuesTableName, DefaultSchema);
            b.Property(x => x.Name).HasMaxLength(SettingValueRecordConstants.MaxNameLength).IsRequired();
            b.Property(x => x.Value).HasMaxLength(SettingValueRecordConstants.MaxValueLengthValue).IsRequired();
            b.Property(x => x.ProviderName).HasMaxLength(SettingValueRecordConstants.MaxProviderNameLength);
            b.Property(x => x.ProviderKey).HasMaxLength(SettingValueRecordConstants.MaxProviderKeyLength);

            b.HasIndex(x => new
                {
                    x.Name,
                    x.ProviderName,
                    x.ProviderKey,
                })
                .IsUnique();
        });

        modelBuilder.Entity<SettingDefinitionRecord>(b =>
        {
            b.ToTable(DefaultSettingDefinitionTableName, DefaultSchema);
            b.TryConfigureExtraProperties();

            b.Property(x => x.Name).HasMaxLength(SettingDefinitionRecordConstants.MaxNameLength).IsRequired();
            b.Property(x => x.DisplayName)
                .HasMaxLength(SettingDefinitionRecordConstants.MaxDisplayNameLength)
                .IsRequired();
            b.Property(x => x.Description).HasMaxLength(SettingDefinitionRecordConstants.MaxDescriptionLength);
            b.Property(x => x.DefaultValue).HasMaxLength(SettingDefinitionRecordConstants.MaxDefaultValueLength);
            b.Property(x => x.Providers).HasMaxLength(SettingDefinitionRecordConstants.MaxProvidersLength);

            b.HasIndex(x => new { x.Name }).IsUnique();
        });
    }
}
