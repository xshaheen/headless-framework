// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Settings.Entities;
using Microsoft.EntityFrameworkCore;

namespace Framework.Settings.Storage.EntityFramework;

[PublicAPI]
public sealed class SettingsDbContext(DbContextOptions options) : DbContext(options)
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
            b.Property(x => x.Name).HasMaxLength(SettingValueRecordConstants.NameMaxLength).IsRequired();
            b.Property(x => x.Value).HasMaxLength(SettingValueRecordConstants.ValueMaxLength).IsRequired();

            b.Property(x => x.ProviderName)
                .HasMaxLength(SettingValueRecordConstants.ProviderNameMaxLength)
                .IsRequired();

            b.Property(x => x.ProviderKey)
                .HasMaxLength(SettingValueRecordConstants.ProviderKeyMaxLength)
                .IsRequired(false);

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

            b.Property(x => x.Name).HasMaxLength(SettingDefinitionRecordConstants.NameMaxLength).IsRequired();

            b.Property(x => x.DisplayName)
                .HasMaxLength(SettingDefinitionRecordConstants.DisplayNameMaxLength)
                .IsRequired();

            b.Property(x => x.Description).HasMaxLength(SettingDefinitionRecordConstants.DescriptionMaxLength);
            b.Property(x => x.DefaultValue).HasMaxLength(SettingDefinitionRecordConstants.DefaultValueMaxLength);
            b.Property(x => x.Providers).HasMaxLength(SettingDefinitionRecordConstants.ProvidersMaxLength);

            b.HasIndex(x => new { x.Name }).IsUnique();
        });
    }
}
