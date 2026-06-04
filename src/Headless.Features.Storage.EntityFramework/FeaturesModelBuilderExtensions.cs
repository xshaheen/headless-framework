// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Options;

namespace Headless.Features;

[PublicAPI]
public static class FeaturesModelBuilderExtensions
{
    extension(ModelBuilder modelBuilder)
    {
        /// <summary>
        /// Applies the Headless features entity configurations, resolving <see cref="FeaturesStorageOptions"/>
        /// from the <paramref name="context"/>'s service provider. Call from <c>OnModelCreating</c> with
        /// <c>modelBuilder.AddHeadlessFeatures(this)</c> to avoid injecting the options into the context.
        /// </summary>
        public ModelBuilder AddHeadlessFeatures(DbContext context)
        {
            Argument.IsNotNull(modelBuilder);
            Argument.IsNotNull(context);

            var options = context.GetService<IOptions<FeaturesStorageOptions>>().Value;

            return modelBuilder.AddHeadlessFeatures(options);
        }

        public ModelBuilder AddHeadlessFeatures(FeaturesStorageOptions options)
        {
            Argument.IsNotNull(modelBuilder);
            Argument.IsNotNull(options);

            modelBuilder.ApplyConfiguration(new FeatureValueRecordConfiguration(options));
            modelBuilder.ApplyConfiguration(new FeatureDefinitionRecordConfiguration(options));
            modelBuilder.ApplyConfiguration(new FeatureGroupDefinitionRecordConfiguration(options));

            return modelBuilder;
        }
    }
}
