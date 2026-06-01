// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Settings.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Headless.Settings;

internal sealed class SettingValueRecordConfiguration(SettingsStorageOptions options)
    : IEntityTypeConfiguration<SettingValueRecord>
{
    public void Configure(EntityTypeBuilder<SettingValueRecord> b)
    {
        b.ToTable(options.SettingValuesTableName, options.Schema);
        b.ConfigureHeadlessConvention();
        b.Property(x => x.Name).HasMaxLength(SettingValueRecordConstants.NameMaxLength).IsRequired();
        b.Property(x => x.Value).HasMaxLength(SettingValueRecordConstants.ValueMaxLength).IsRequired();
        b.Property(x => x.ProviderName).HasMaxLength(SettingValueRecordConstants.ProviderNameMaxLength).IsRequired();
        b.Property(x => x.ProviderKey).HasMaxLength(SettingValueRecordConstants.ProviderKeyMaxLength).IsRequired(false);

        b.HasIndex(x => new
            {
                x.Name,
                x.ProviderName,
                x.ProviderKey,
            })
            .IsUnique();
    }
}
