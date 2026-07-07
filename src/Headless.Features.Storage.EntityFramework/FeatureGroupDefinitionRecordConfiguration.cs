// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.EntityFramework;
using Headless.Features.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Headless.Features;

/// <summary>EF Core entity type configuration for <see cref="FeatureGroupDefinitionRecord"/>.</summary>
/// <param name="options">Storage options supplying the table name and schema.</param>
internal sealed class FeatureGroupDefinitionRecordConfiguration(FeaturesStorageOptions options)
    : IEntityTypeConfiguration<FeatureGroupDefinitionRecord>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<FeatureGroupDefinitionRecord> b)
    {
        b.ToTable(options.FeatureGroupDefinitionsTableName, options.Schema);
        b.TryConfigureExtraProperties();
        b.Property(x => x.Name).HasMaxLength(FeatureGroupDefinitionRecordConstants.NameMaxLength).IsRequired();
        b.Property(x => x.DisplayName)
            .HasMaxLength(FeatureGroupDefinitionRecordConstants.DisplayNameMaxLength)
            .IsRequired();
        b.HasIndex(x => new { x.Name }).IsUnique();
    }
}
