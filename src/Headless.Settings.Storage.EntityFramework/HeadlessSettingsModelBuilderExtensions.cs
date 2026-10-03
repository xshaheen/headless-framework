// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Hosting.Initialization;
using Headless.Settings;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Options;

namespace Microsoft.EntityFrameworkCore;

/// <summary>Extension methods on <see cref="ModelBuilder"/> for registering Headless settings entities.</summary>
[PublicAPI]
public static class HeadlessSettingsModelBuilderExtensions
{
    extension(ModelBuilder modelBuilder)
    {
        /// <summary>
        /// Applies the Headless settings entity configurations, resolving <see cref="SettingsStorageOptions"/>
        /// from the <paramref name="context"/>'s service provider and the naming style from its database provider
        /// (snake_case on PostgreSQL, PascalCase elsewhere). Call from <c>OnModelCreating</c> with
        /// <c>modelBuilder.AddHeadlessSettings(this)</c> to avoid injecting the options into the context.
        /// </summary>
        /// <param name="context">
        /// The <see cref="DbContext"/> whose service provider is used to resolve
        /// <see cref="SettingsStorageOptions"/>.
        /// </param>
        /// <returns>The same <see cref="ModelBuilder"/> instance to allow chaining.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="context"/> is <see langword="null"/>.
        /// </exception>
        public ModelBuilder AddHeadlessSettings(DbContext context)
        {
            Argument.IsNotNull(modelBuilder);
            Argument.IsNotNull(context);

            var options = context.GetService<IOptions<SettingsStorageOptions>>().Value;
            var style = HeadlessStorageNaming.ForProvider(context.Database.ProviderName);

            return modelBuilder.AddHeadlessSettings(options, style);
        }

        /// <summary>
        /// Applies the Headless settings entity configurations using the supplied
        /// <paramref name="options"/> directly.
        /// </summary>
        /// <param name="options">Storage options that control table names and the schema.</param>
        /// <param name="style">
        /// The naming style of the database the model targets. It must match the database: the raw providers and
        /// <c>AddHeadlessSettings(DbContext)</c> use <see cref="StorageNamingStyle.SnakeCase"/> on
        /// PostgreSQL and <see cref="StorageNamingStyle.PascalCase"/> elsewhere; pass
        /// <c>HeadlessStorageNaming.ForProvider(Database.ProviderName)</c> to derive it.
        /// </param>
        /// <returns>The same <see cref="ModelBuilder"/> instance to allow chaining.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="options"/> is <see langword="null"/>.
        /// </exception>
        public ModelBuilder AddHeadlessSettings(SettingsStorageOptions options, StorageNamingStyle style)
        {
            Argument.IsNotNull(modelBuilder);
            Argument.IsNotNull(options);

            modelBuilder.ApplyConfiguration(new SettingValueRecordConfiguration(options, style));
            modelBuilder.ApplyConfiguration(new SettingDefinitionRecordConfiguration(options, style));

            return modelBuilder;
        }
    }
}
