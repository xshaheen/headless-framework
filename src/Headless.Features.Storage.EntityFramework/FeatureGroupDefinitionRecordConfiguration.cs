// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Features.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Headless.Features;

internal sealed class FeatureGroupDefinitionRecordConfiguration(FeaturesStorageOptions options)
    : IEntityTypeConfiguration<FeatureGroupDefinitionRecord>
{
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
