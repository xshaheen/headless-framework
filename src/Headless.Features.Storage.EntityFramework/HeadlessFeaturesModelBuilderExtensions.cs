// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Options;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Microsoft.EntityFrameworkCore;

/// <summary>Extension members on <see cref="ModelBuilder"/> for registering Headless feature entity configurations.</summary>
[PublicAPI]
public static class HeadlessFeaturesModelBuilderExtensions
{
    extension(ModelBuilder modelBuilder)
    {
        /// <summary>
        /// Applies the Headless features entity configurations, resolving <see cref="FeaturesStorageOptions"/>
        /// from the <paramref name="context"/>'s service provider. Call from <c>OnModelCreating</c> with
        /// <c>modelBuilder.AddHeadlessFeatures(this)</c> to avoid injecting the options into the context.
        /// </summary>
        /// <param name="context">The <see cref="DbContext"/> whose service provider supplies <see cref="FeaturesStorageOptions"/>.</param>
        /// <returns>The same <see cref="ModelBuilder"/> instance for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="context"/> is <see langword="null"/>.</exception>
        public ModelBuilder AddHeadlessFeatures(DbContext context)
        {
            Argument.IsNotNull(modelBuilder);
            Argument.IsNotNull(context);

            var options = context.GetService<IOptions<FeaturesStorageOptions>>().Value;

            return modelBuilder.AddHeadlessFeatures(options);
        }

        /// <summary>Applies the Headless features entity configurations using the supplied <paramref name="options"/>.</summary>
        /// <param name="options">Storage options controlling table names and schema.</param>
        /// <returns>The same <see cref="ModelBuilder"/> instance for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
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
