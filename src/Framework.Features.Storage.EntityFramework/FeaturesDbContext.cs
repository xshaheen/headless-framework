// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Features.Entities;
using Microsoft.EntityFrameworkCore;

namespace Framework.Features.Storage.EntityFramework;

[PublicAPI]
public sealed class FeaturesDbContext(DbContextOptions options) : DbContext(options)
{
    public static string DefaultSchema { get; set; } = "features";
    public static string DefaultFeatureValuesTableName { get; set; } = "FeatureValues";
    public static string DefaultFeatureDefinitionTableName { get; set; } = "FeatureDefinitions";
    public static string DefaultFeatureGroupDefinitionTableName { get; set; } = "FeatureGroupDefinitions";

    public required DbSet<FeatureValueRecord> FeatureValues { get; init; }

    public required DbSet<FeatureDefinitionRecord> FeatureDefinitions { get; init; }

    public required DbSet<FeatureGroupDefinitionRecord> FeatureGroupDefinitions { get; init; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<FeatureValueRecord>(b =>
        {
            b.ToTable(DefaultFeatureValuesTableName, DefaultSchema);
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
        });

        modelBuilder.Entity<FeatureGroupDefinitionRecord>(b =>
        {
            b.ToTable(DefaultFeatureGroupDefinitionTableName, DefaultSchema);
            b.TryConfigureExtraProperties();

            b.Property(x => x.Name).HasMaxLength(FeatureGroupDefinitionRecordConstants.NameMaxLength).IsRequired();

            b.Property(x => x.DisplayName)
                .HasMaxLength(FeatureGroupDefinitionRecordConstants.DisplayNameMaxLength)
                .IsRequired();

            b.HasIndex(x => new { x.Name }).IsUnique();
        });

        modelBuilder.Entity<FeatureDefinitionRecord>(b =>
        {
            b.ToTable(DefaultFeatureDefinitionTableName, DefaultSchema);
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
