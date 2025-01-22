// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Features.Entities;
using Microsoft.EntityFrameworkCore;

namespace Framework.Features;

public interface IFeaturesDbContext
{
    DbSet<FeatureValueRecord> FeatureValues { get; init; }

    DbSet<FeatureDefinitionRecord> FeatureDefinitions { get; init; }

    DbSet<FeatureGroupDefinitionRecord> FeatureGroupDefinitions { get; init; }
}
