// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Settings.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Options;

namespace Headless.Settings;

[PublicAPI]
public static class SettingsModelBuilderExtensions
{
    public static void AddSettingsConfiguration(this ModelBuilder modelBuilder, DbContext context)
    {
        Argument.IsNotNull(context);

        var options = context.GetService<IOptions<SettingsStorageOptions>>().Value;
        modelBuilder.AddSettingsConfiguration(options);
    }

    public static void AddSettingsConfiguration(this ModelBuilder modelBuilder, SettingsStorageOptions options)
    {
        Argument.IsNotNull(options);

        modelBuilder.Entity<SettingValueRecord>(b =>
        {
            b.ToTable(options.SettingValuesTableName, options.Schema);
            b.ConfigureHeadlessConvention();
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
            b.ToTable(options.SettingDefinitionsTableName, options.Schema);
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
