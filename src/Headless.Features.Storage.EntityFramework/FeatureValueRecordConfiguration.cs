// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Features.Entities;
using Headless.Features.Internal;
using Headless.Hosting.Initialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Headless.Features;

/// <summary>EF Core entity type configuration for <see cref="FeatureValueRecord"/>.</summary>
/// <param name="options">Storage options supplying the table name and schema.</param>
/// <param name="style">The naming style of the database the model targets.</param>
internal sealed class FeatureValueRecordConfiguration(FeaturesStorageOptions options, StorageNamingStyle style)
    : IEntityTypeConfiguration<FeatureValueRecord>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<FeatureValueRecord> b)
    {
        var table = options.ResolveFeatureValuesTableName(style);

        b.ToTable(table, options.Schema);
        b.ConfigureHeadlessConvention();
        b.HasKey(x => x.Id).HasName(HeadlessStorageNaming.PrimaryKeyName(style, table));
        b.Property(x => x.Name).HasMaxLength(FeatureValueRecordConstants.NameMaxLength).IsRequired();
        b.Property(x => x.Value).HasMaxLength(FeatureValueRecordConstants.ValueMaxLength).IsRequired();
        b.Property(x => x.ProviderName).HasMaxLength(FeatureValueRecordConstants.ProviderNameMaxLength).IsRequired();
        b.Property(x => x.ProviderKey).HasMaxLength(FeatureValueRecordConstants.ProviderKeyMaxLength).IsRequired(false);

        var providerKey = HeadlessStorageNaming.Apply(style, nameof(FeatureValueRecord.ProviderKey));

        // PostgreSQL and SQLite treat NULLs as distinct in a unique index, so a single index over the
        // nullable ProviderKey would let concurrent inserts create duplicate NULL-key rows.
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
                HeadlessStorageNaming.IndexName(style, table, FeaturesStorageNames.ValuesByNameProviderKey)
            );

        b.HasIndex(x => new { x.Name, x.ProviderName })
            .IsUnique()
            .HasFilter($"\"{providerKey}\" IS NULL")
            .HasDatabaseName(
                HeadlessStorageNaming.IndexName(style, table, FeaturesStorageNames.ValuesByNameNullProviderKey)
            );

        b.HasIndex(x => new { x.ProviderName, x.ProviderKey })
            .HasDatabaseName(HeadlessStorageNaming.IndexName(style, table, FeaturesStorageNames.ValuesByProvider));

        b.ApplyColumnNaming(style);
    }
}
