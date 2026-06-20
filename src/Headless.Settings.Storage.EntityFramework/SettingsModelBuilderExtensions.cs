// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Options;

namespace Headless.Settings;

[PublicAPI]
public static class SettingsModelBuilderExtensions
{
    extension(ModelBuilder modelBuilder)
    {
        /// <summary>
        /// Applies the Headless settings entity configurations, resolving <see cref="SettingsStorageOptions"/>
        /// from the <paramref name="context"/>'s service provider. Call from <c>OnModelCreating</c> with
        /// <c>modelBuilder.AddHeadlessSettings(this)</c> to avoid injecting the options into the context.
        /// </summary>
        public ModelBuilder AddHeadlessSettings(DbContext context)
        {
            Argument.IsNotNull(modelBuilder);
            Argument.IsNotNull(context);

            var options = context.GetService<IOptions<SettingsStorageOptions>>().Value;

            return modelBuilder.AddHeadlessSettings(options);
        }

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
