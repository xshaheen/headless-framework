// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.EntityFramework;
using Headless.Settings.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Headless.Settings;

/// <summary>
/// EF Core configuration for <see cref="SettingValueRecord"/>, mapping the entity to the
/// table and schema specified by <see cref="SettingsStorageOptions"/> and enforcing column
/// length constraints and a unique index on (<c>Name</c>, <c>ProviderName</c>, <c>ProviderKey</c>).
/// </summary>
/// <param name="options">Storage options that supply the table name and schema.</param>
internal sealed class SettingValueRecordConfiguration(SettingsStorageOptions options)
    : IEntityTypeConfiguration<SettingValueRecord>
{
    /// <inheritdoc/>
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
