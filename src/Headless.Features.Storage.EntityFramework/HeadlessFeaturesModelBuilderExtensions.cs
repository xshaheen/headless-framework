// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Features;
using Headless.Hosting.Initialization;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Options;

namespace Microsoft.EntityFrameworkCore;

/// <summary>Extension members on <see cref="ModelBuilder"/> for registering Headless feature entity configurations.</summary>
[PublicAPI]
public static class HeadlessFeaturesModelBuilderExtensions
{
    extension(ModelBuilder modelBuilder)
    {
        /// <summary>
        /// Applies the Headless features entity configurations, resolving <see cref="FeaturesStorageOptions"/>
        /// from the <paramref name="context"/>'s service provider and the naming style from its database provider
        /// (snake_case on PostgreSQL, PascalCase elsewhere). Call from <c>OnModelCreating</c> with
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
            var style = HeadlessStorageNaming.ForProvider(context.Database.ProviderName);

            return modelBuilder.AddHeadlessFeatures(options, style);
        }

        /// <summary>Applies the Headless features entity configurations using the supplied <paramref name="options"/>.</summary>
        /// <param name="options">Storage options controlling table names and schema.</param>
        /// <param name="style">
        /// The naming style of the database the model targets. It must match the database: the raw providers and
        /// <c>AddHeadlessFeatures(DbContext)</c> use <see cref="StorageNamingStyle.SnakeCase"/> on
        /// PostgreSQL and <see cref="StorageNamingStyle.PascalCase"/> elsewhere; pass
        /// <c>HeadlessStorageNaming.ForProvider(Database.ProviderName)</c> to derive it.
        /// </param>
        /// <returns>The same <see cref="ModelBuilder"/> instance for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
        public ModelBuilder AddHeadlessFeatures(FeaturesStorageOptions options, StorageNamingStyle style)
        {
            Argument.IsNotNull(modelBuilder);
            Argument.IsNotNull(options);

            modelBuilder.ApplyConfiguration(new FeatureValueRecordConfiguration(options, style));
            modelBuilder.ApplyConfiguration(new FeatureDefinitionRecordConfiguration(options, style));
            modelBuilder.ApplyConfiguration(new FeatureGroupDefinitionRecordConfiguration(options, style));

            return modelBuilder;
        }
    }
}
