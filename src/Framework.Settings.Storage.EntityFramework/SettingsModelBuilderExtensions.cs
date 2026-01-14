// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Settings.Entities;
using Microsoft.EntityFrameworkCore;

namespace Framework.Settings;

[PublicAPI]
public static class SettingsModelBuilderExtensions
{
    public static string DefaultSettingValuesTableName { get; set; } = "SettingValues";

    public static string DefaultSettingDefinitionTableName { get; set; } = "SettingDefinitions";

    public static void AddSettingsConfiguration(this ModelBuilder modelBuilder, string schema = "settings")
    {
        modelBuilder.Entity<SettingValueRecord>(b =>
        {
            b.ToTable(DefaultSettingValuesTableName, schema);
            b.ConfigureFrameworkConvention();
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
            b.ToTable(DefaultSettingDefinitionTableName, schema);
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
