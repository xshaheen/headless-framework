// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Features.Entities;
using Microsoft.EntityFrameworkCore;

namespace Headless.Features.Storage.EntityFramework;

public interface IFeaturesDbContext
{
    DbSet<FeatureValueRecord> FeatureValues { get; }

    DbSet<FeatureDefinitionRecord> FeatureDefinitions { get; }

    DbSet<FeatureGroupDefinitionRecord> FeatureGroupDefinitions { get; }
}
