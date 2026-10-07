// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;
using Headless.Settings.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Headless.Settings;

/// <summary>
/// EF Core configuration for <see cref="SettingValueRecord"/>, mapping the entity to the
/// table and schema specified by <see cref="SettingsStorageOptions"/> and enforcing column
/// length constraints plus a pair of filtered unique indexes on
/// (<c>Name</c>, <c>ProviderName</c>, <c>ProviderKey</c>) that also covers a <see langword="null"/>
/// <c>ProviderKey</c>.
/// </summary>
/// <param name="options">Storage options that supply the table name and schema.</param>
/// <param name="style">The naming style of the database the model targets.</param>
internal sealed class SettingValueRecordConfiguration(SettingsStorageOptions options, StorageNamingStyle style)
    : IEntityTypeConfiguration<SettingValueRecord>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<SettingValueRecord> b)
    {
        var table = options.ResolveSettingValuesTableName(style);

        b.ToTable(table, options.Schema);
        b.ConfigureHeadlessConvention();
        b.HasKey(x => x.Id).HasName(HeadlessStorageNaming.PrimaryKeyName(style, table));
        b.Property(x => x.Name).HasMaxLength(SettingValueRecordConstants.NameMaxLength).IsRequired();
        b.Property(x => x.Value).HasMaxLength(SettingValueRecordConstants.ValueMaxLength).IsRequired();
        b.Property(x => x.ProviderName).HasMaxLength(SettingValueRecordConstants.ProviderNameMaxLength).IsRequired();
        b.Property(x => x.ProviderKey).HasMaxLength(SettingValueRecordConstants.ProviderKeyMaxLength).IsRequired(false);

        var providerKey = HeadlessStorageNaming.Apply(style, nameof(SettingValueRecord.ProviderKey));

        // PostgreSQL and SQLite treat NULLs as distinct in a unique index, so a single index over the
        // nullable ProviderKey would let concurrent inserts create duplicate global (NULL-key) rows.
        // Mirror the raw PostgreSql/SqlServer initializers: one index per key nullability, same names.
        b.HasIndex(x => new
            {
                x.Name,
                x.ProviderName,
                x.ProviderKey,
            })
            .IsUnique()
            .HasFilter($"\"{providerKey}\" IS NOT NULL")
            .HasDatabaseName(
                HeadlessStorageNaming.IndexName(style, table, SettingsStorageNames.ValuesByNameProviderKey)
            );

        b.HasIndex(x => new { x.Name, x.ProviderName })
            .IsUnique()
            .HasFilter($"\"{providerKey}\" IS NULL")
            .HasDatabaseName(
                HeadlessStorageNaming.IndexName(style, table, SettingsStorageNames.ValuesByNameNullProviderKey)
            );

        b.HasIndex(x => new { x.ProviderName, x.ProviderKey })
            .HasDatabaseName(HeadlessStorageNaming.IndexName(style, table, SettingsStorageNames.ValuesByProvider));

        b.ApplyColumnNaming(style);
    }
}
