// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Settings.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Headless.Settings;

internal sealed class SettingDefinitionRecordConfiguration(SettingsStorageOptions options)
    : IEntityTypeConfiguration<SettingDefinitionRecord>
{
    public void Configure(EntityTypeBuilder<SettingDefinitionRecord> b)
    {
        b.ToTable(options.SettingDefinitionsTableName, options.Schema);
        b.TryConfigureExtraProperties();

        b.Property(x => x.Name).HasMaxLength(SettingDefinitionRecordConstants.NameMaxLength).IsRequired();
        b.Property(x => x.DisplayName).HasMaxLength(SettingDefinitionRecordConstants.DisplayNameMaxLength).IsRequired();
        b.Property(x => x.Description).HasMaxLength(SettingDefinitionRecordConstants.DescriptionMaxLength);
        b.Property(x => x.DefaultValue).HasMaxLength(SettingDefinitionRecordConstants.DefaultValueMaxLength);
        b.Property(x => x.Providers).HasMaxLength(SettingDefinitionRecordConstants.ProvidersMaxLength);

        b.HasIndex(x => new { x.Name }).IsUnique();
    }
}
