// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Features.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Headless.Features;

/// <summary>EF Core entity type configuration for <see cref="FeatureValueRecord"/>.</summary>
/// <param name="options">Storage options supplying the table name and schema.</param>
internal sealed class FeatureValueRecordConfiguration(FeaturesStorageOptions options)
    : IEntityTypeConfiguration<FeatureValueRecord>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<FeatureValueRecord> b)
    {
        b.ToTable(options.FeatureValuesTableName, options.Schema);
        b.ConfigureHeadlessConvention();
        b.Property(x => x.Name).HasMaxLength(FeatureValueRecordConstants.NameMaxLength).IsRequired();
        b.Property(x => x.Value).HasMaxLength(FeatureValueRecordConstants.ValueMaxLength).IsRequired();
        b.Property(x => x.ProviderName).HasMaxLength(FeatureValueRecordConstants.ProviderNameMaxLength).IsRequired();
        b.Property(x => x.ProviderKey).HasMaxLength(FeatureValueRecordConstants.ProviderKeyMaxLength).IsRequired(false);

        b.HasIndex(x => new
            {
                x.Name,
                x.ProviderName,
                x.ProviderKey,
            })
            .IsUnique();

        b.HasIndex(x => new { x.ProviderName, x.ProviderKey });
    }
}
