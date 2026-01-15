// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Features.Entities;
using Microsoft.EntityFrameworkCore;

namespace Framework.Features;

[PublicAPI]
public static class FeaturesModelBuilderExtensions
{
    public static string DefaultFeatureValuesTableName { get; set; } = "FeatureValues";

    public static string DefaultFeatureDefinitionTableName { get; set; } = "FeatureDefinitions";

    public static string DefaultFeatureGroupDefinitionTableName { get; set; } = "FeatureGroupDefinitions";

    public static void AddFeaturesConfiguration(this ModelBuilder modelBuilder, string schema = "features")
    {
        modelBuilder.Entity<FeatureValueRecord>(b =>
        {
            b.ToTable(DefaultFeatureValuesTableName, schema);
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
            b.ToTable(DefaultFeatureGroupDefinitionTableName, schema);
            b.TryConfigureExtraProperties();

            b.Property(x => x.Name).HasMaxLength(FeatureGroupDefinitionRecordConstants.NameMaxLength).IsRequired();

            b.Property(x => x.DisplayName)
                .HasMaxLength(FeatureGroupDefinitionRecordConstants.DisplayNameMaxLength)
                .IsRequired();

            b.HasIndex(x => new { x.Name }).IsUnique();
        });

        modelBuilder.Entity<FeatureDefinitionRecord>(b =>
        {
            b.ToTable(DefaultFeatureDefinitionTableName, schema);
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
