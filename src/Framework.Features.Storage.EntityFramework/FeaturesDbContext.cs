// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Features.Entities;
using Microsoft.EntityFrameworkCore;

namespace Framework.Features;

[PublicAPI]
public class FeaturesDbContext(DbContextOptions options) : DbContext(options), IFeaturesDbContext
{
    public required DbSet<FeatureValueRecord> FeatureValues { get; init; }

    public required DbSet<FeatureDefinitionRecord> FeatureDefinitions { get; init; }

    public required DbSet<FeatureGroupDefinitionRecord> FeatureGroupDefinitions { get; init; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.AddFeaturesConfiguration();
    }
}
