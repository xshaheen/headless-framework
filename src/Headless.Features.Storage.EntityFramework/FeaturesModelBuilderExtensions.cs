// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Features.Entities;
using Microsoft.EntityFrameworkCore;

namespace Headless.Features;

[PublicAPI]
public static class FeaturesModelBuilderExtensions
{
    public static ModelBuilder AddHeadlessFeatures(this ModelBuilder modelBuilder, FeaturesStorageOptions options)
    {
        Argument.IsNotNull(modelBuilder);
        Argument.IsNotNull(options);

        modelBuilder.ApplyConfiguration(new FeatureValueRecordConfiguration(options));
        modelBuilder.ApplyConfiguration(new FeatureDefinitionRecordConfiguration(options));
        modelBuilder.ApplyConfiguration(new FeatureGroupDefinitionRecordConfiguration(options));

        return modelBuilder;
    }
}
