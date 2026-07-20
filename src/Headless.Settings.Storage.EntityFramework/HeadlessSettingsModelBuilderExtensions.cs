// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Options;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Microsoft.EntityFrameworkCore;

/// <summary>Extension methods on <see cref="ModelBuilder"/> for registering Headless settings entities.</summary>
[PublicAPI]
public static class HeadlessSettingsModelBuilderExtensions
{
    extension(ModelBuilder modelBuilder)
    {
        /// <summary>
        /// Applies the Headless settings entity configurations, resolving <see cref="SettingsStorageOptions"/>
        /// from the <paramref name="context"/>'s service provider. Call from <c>OnModelCreating</c> with
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

            return modelBuilder.AddHeadlessSettings(options);
        }

        /// <summary>
        /// Applies the Headless settings entity configurations using the supplied
        /// <paramref name="options"/> directly.
        /// </summary>
        /// <param name="options">Storage options that control table names and the schema.</param>
        /// <returns>The same <see cref="ModelBuilder"/> instance to allow chaining.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="options"/> is <see langword="null"/>.
        /// </exception>
        public ModelBuilder AddHeadlessSettings(SettingsStorageOptions options)
        {
            Argument.IsNotNull(modelBuilder);
            Argument.IsNotNull(options);

            modelBuilder.ApplyConfiguration(new SettingValueRecordConfiguration(options));
            modelBuilder.ApplyConfiguration(new SettingDefinitionRecordConfiguration(options));

            return modelBuilder;
        }
    }
}
