// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Features.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Headless.Features;

/// <summary>EF Core entity type configuration for <see cref="FeatureDefinitionRecord"/>.</summary>
/// <param name="options">Storage options supplying the table name and schema.</param>
internal sealed class FeatureDefinitionRecordConfiguration(FeaturesStorageOptions options)
    : IEntityTypeConfiguration<FeatureDefinitionRecord>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<FeatureDefinitionRecord> b)
    {
        b.ToTable(options.FeatureDefinitionsTableName, options.Schema);
        b.TryConfigureExtraProperties();
        b.Property(x => x.GroupName).HasMaxLength(FeatureDefinitionRecordConstants.NameMaxLength).IsRequired();
        b.Property(x => x.Name).HasMaxLength(FeatureDefinitionRecordConstants.NameMaxLength).IsRequired();
        b.Property(x => x.ParentName).HasMaxLength(FeatureDefinitionRecordConstants.NameMaxLength);
        b.Property(x => x.DisplayName).HasMaxLength(FeatureDefinitionRecordConstants.DisplayNameMaxLength).IsRequired();
        b.Property(x => x.Description).HasMaxLength(FeatureDefinitionRecordConstants.DescriptionMaxLength);
        b.Property(x => x.DefaultValue).HasMaxLength(FeatureDefinitionRecordConstants.DefaultValueMaxLength);
        b.Property(x => x.Providers).HasMaxLength(FeatureDefinitionRecordConstants.ProvidersMaxLength);
        b.HasIndex(x => new { x.Name }).IsUnique();
        b.HasIndex(x => new { x.GroupName });
    }
}
