// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Features.Entities;
using Microsoft.EntityFrameworkCore;

namespace Framework.Features;

public interface IFeaturesDbContext
{
    DbSet<FeatureValueRecord> FeatureValues { get; }

    DbSet<FeatureDefinitionRecord> FeatureDefinitions { get; }

    DbSet<FeatureGroupDefinitionRecord> FeatureGroupDefinitions { get; }
}
