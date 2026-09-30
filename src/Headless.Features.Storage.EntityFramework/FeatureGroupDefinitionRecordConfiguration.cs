// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Features.Entities;
using Headless.Features.Internal;
using Headless.Hosting.Initialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Headless.Features;

/// <summary>EF Core entity type configuration for <see cref="FeatureGroupDefinitionRecord"/>.</summary>
/// <param name="options">Storage options supplying the table name and schema.</param>
/// <param name="style">The naming style of the database the model targets.</param>
internal sealed class FeatureGroupDefinitionRecordConfiguration(
    FeaturesStorageOptions options,
    StorageNamingStyle style
) : IEntityTypeConfiguration<FeatureGroupDefinitionRecord>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<FeatureGroupDefinitionRecord> b)
    {
        var table = options.ResolveFeatureGroupDefinitionsTableName(style);

        b.ToTable(table, options.Schema);
        b.TryConfigureExtraProperties();
        b.HasKey(x => x.Id).HasName(HeadlessStorageNaming.PrimaryKeyName(style, table));
        b.Property(x => x.Name).HasMaxLength(FeatureGroupDefinitionRecordConstants.NameMaxLength).IsRequired();
        b.Property(x => x.DisplayName)
            .HasMaxLength(FeatureGroupDefinitionRecordConstants.DisplayNameMaxLength)
            .IsRequired();
        b.HasIndex(x => new { x.Name })
            .IsUnique()
            .HasDatabaseName(HeadlessStorageNaming.IndexName(style, table, FeaturesStorageNames.GroupsByName));
        b.ApplyColumnNaming(style);
    }
}
