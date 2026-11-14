// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Features.Internal;
using Headless.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Headless.Features;

/// <summary>EF Core entity type configuration for <see cref="FeatureDefinitionRecord"/>.</summary>
/// <param name="options">Storage options supplying the table name, schema, and column lengths.</param>
/// <param name="style">The naming style of the database the model targets.</param>
internal sealed class FeatureDefinitionRecordConfiguration(FeaturesStorageOptions options, StorageNamingStyle style)
    : IEntityTypeConfiguration<FeatureDefinitionRecord>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<FeatureDefinitionRecord> b)
    {
        var table = options.ResolveFeatureDefinitionsTableName(style);

        b.ToTable(table, options.Schema);
        b.TryConfigureExtraProperties();
        b.HasKey(x => x.Id).HasName(HeadlessStorageNaming.PrimaryKeyName(style, table));
        b.Property(x => x.GroupName).HasMaxLength(options.FeatureGroupDefinitionsNameMaxLength).IsRequired();
        b.Property(x => x.Name).HasMaxLength(options.FeatureDefinitionsNameMaxLength).IsRequired();
        b.Property(x => x.ParentName).HasMaxLength(options.FeatureDefinitionsNameMaxLength);
        b.Property(x => x.DisplayName).HasMaxLength(options.FeatureDefinitionsDisplayNameMaxLength).IsRequired();
        b.Property(x => x.Description).HasMaxLength(options.FeatureDefinitionsDescriptionMaxLength);
        b.Property(x => x.DefaultValue).HasMaxLength(options.FeatureDefinitionsDefaultValueMaxLength);
        b.Property(x => x.Providers).HasMaxLength(options.FeatureDefinitionsProvidersMaxLength);
        b.HasIndex(x => new { x.Name })
            .IsUnique()
            .HasDatabaseName(HeadlessStorageNaming.IndexName(style, table, FeaturesStorageNames.DefinitionsByName));
        b.HasIndex(x => new { x.GroupName })
            .HasDatabaseName(
                HeadlessStorageNaming.IndexName(style, table, FeaturesStorageNames.DefinitionsByGroupName)
            );
        b.ApplyColumnNaming(style);
    }
}
