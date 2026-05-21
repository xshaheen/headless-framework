// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Features.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Options;

namespace Headless.Features;

[PublicAPI]
public sealed class FeaturesDbContext(DbContextOptions options) : DbContext(options), IFeaturesDbContext
{
    public required DbSet<FeatureValueRecord> FeatureValues { get; init; }

    public required DbSet<FeatureDefinitionRecord> FeatureDefinitions { get; init; }

    public required DbSet<FeatureGroupDefinitionRecord> FeatureGroupDefinitions { get; init; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.AddFeaturesConfiguration(this);
    }
}

[PublicAPI]
public sealed class FeaturesStorageModelCacheKeyFactory : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime)
    {
        var options = context.GetService<IOptions<FeaturesStorageOptions>>().Value;

        return (
            context.GetType(),
            designTime,
            options.Schema,
            options.FeatureValuesTableName,
            options.FeatureDefinitionsTableName,
            options.FeatureGroupDefinitionsTableName
        );
    }
}
