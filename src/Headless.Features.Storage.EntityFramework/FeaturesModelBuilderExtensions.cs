// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Features.Entities;
using Microsoft.EntityFrameworkCore;

namespace Headless.Features.Storage.EntityFramework;

[PublicAPI]
public static class FeaturesModelBuilderExtensions
{
    public static void AddFeaturesConfiguration(this ModelBuilder modelBuilder, FeaturesStorageOptions options)
    {
        Argument.IsNotNull(options);

        modelBuilder.Entity<FeatureValueRecord>(b =>
        {
            b.ToTable(options.FeatureValuesTableName, options.Schema);
            b.Property(x => x.Name).HasMaxLength(FeatureValueRecordConstants.NameMaxLength).IsRequired();
            b.Property(x => x.Value).HasMaxLength(FeatureValueRecordConstants.ValueMaxLength).IsRequired();

            b.Property(x => x.ProviderName)
                .HasMaxLength(FeatureValueRecordConstants.ProviderNameMaxLength)
                .IsRequired();

            b.Property(x => x.ProviderKey)
                .HasMaxLength(FeatureValueRecordConstants.ProviderKeyMaxLength)
                .IsRequired(false);

            b.HasIndex(x => new
                {
                    x.Name,
                    x.ProviderName,
                    x.ProviderKey,
                })
                .IsUnique();

            b.HasIndex(x => new { x.ProviderName, x.ProviderKey });
        });

        modelBuilder.Entity<FeatureGroupDefinitionRecord>(b =>
        {
            b.ToTable(options.FeatureGroupDefinitionsTableName, options.Schema);
            b.TryConfigureExtraProperties();

            b.Property(x => x.Name).HasMaxLength(FeatureGroupDefinitionRecordConstants.NameMaxLength).IsRequired();

            b.Property(x => x.DisplayName)
                .HasMaxLength(FeatureGroupDefinitionRecordConstants.DisplayNameMaxLength)
                .IsRequired();

            b.HasIndex(x => new { x.Name }).IsUnique();
        });

        modelBuilder.Entity<FeatureDefinitionRecord>(b =>
        {
            b.ToTable(options.FeatureDefinitionsTableName, options.Schema);
            b.TryConfigureExtraProperties();

            b.Property(x => x.GroupName).HasMaxLength(FeatureDefinitionRecordConstants.NameMaxLength).IsRequired();
            b.Property(x => x.Name).HasMaxLength(FeatureDefinitionRecordConstants.NameMaxLength).IsRequired();
            b.Property(x => x.ParentName).HasMaxLength(FeatureDefinitionRecordConstants.NameMaxLength);

            b.Property(x => x.DisplayName)
                .HasMaxLength(FeatureDefinitionRecordConstants.DisplayNameMaxLength)
                .IsRequired();

            b.Property(x => x.Description).HasMaxLength(FeatureDefinitionRecordConstants.DescriptionMaxLength);
            b.Property(x => x.DefaultValue).HasMaxLength(FeatureDefinitionRecordConstants.DefaultValueMaxLength);
            b.Property(x => x.Providers).HasMaxLength(FeatureDefinitionRecordConstants.ProvidersMaxLength);

            b.HasIndex(x => new { x.Name }).IsUnique();
            b.HasIndex(x => new { x.GroupName });
        });
    }
}
