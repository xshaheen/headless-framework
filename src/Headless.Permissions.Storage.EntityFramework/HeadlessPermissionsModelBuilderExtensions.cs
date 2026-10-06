// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Hosting;
using Headless.Permissions;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Options;

namespace Microsoft.EntityFrameworkCore;

[PublicAPI]
public static class HeadlessPermissionsModelBuilderExtensions
{
    extension(ModelBuilder modelBuilder)
    {
        /// <summary>
        /// Applies the Headless permissions entity configurations, resolving <see cref="PermissionsStorageOptions"/>
        /// from the <paramref name="context"/>'s service provider and the naming style from its database provider
        /// (snake_case on PostgreSQL, PascalCase elsewhere). Call from <c>OnModelCreating</c> with
        /// <c>modelBuilder.ConfigureHeadlessPermissions(this)</c> to avoid injecting the options into the context.
        /// </summary>
        /// <param name="context">The <see cref="DbContext"/> whose service provider supplies <see cref="PermissionsStorageOptions"/>.</param>
        /// <returns>The same <see cref="ModelBuilder"/> instance for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="context"/> is <see langword="null"/>.</exception>
        public ModelBuilder ConfigureHeadlessPermissions(DbContext context)
        {
            Argument.IsNotNull(modelBuilder);
            Argument.IsNotNull(context);

            var options = context.GetService<IOptions<PermissionsStorageOptions>>().Value;
            var style = HeadlessStorageNaming.ForProvider(context.Database.ProviderName);

            return modelBuilder.ConfigureHeadlessPermissions(options, style);
        }

        /// <summary>
        /// Applies the Headless permissions entity configurations using the supplied
        /// <paramref name="options"/>. Use this overload when you already hold a
        /// <see cref="PermissionsStorageOptions"/> instance; otherwise prefer the
        /// <c>ConfigureHeadlessPermissions(DbContext)</c> overload, which resolves options from the
        /// context's service provider.
        /// </summary>
        /// <param name="options">Storage options that drive the schema, table names, and column constraints applied to each entity.</param>
        /// <param name="style">
        /// The naming style of the database the model targets. It must match the database: the raw providers and
        /// <c>ConfigureHeadlessPermissions(DbContext)</c> use <see cref="StorageNamingStyle.SnakeCase"/> on
        /// PostgreSQL and <see cref="StorageNamingStyle.PascalCase"/> elsewhere; pass
        /// <c>HeadlessStorageNaming.ForProvider(Database.ProviderName)</c> to derive it.
        /// </param>
        /// <returns>The same <see cref="ModelBuilder"/> instance for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
        public ModelBuilder ConfigureHeadlessPermissions(PermissionsStorageOptions options, StorageNamingStyle style)
        {
            Argument.IsNotNull(modelBuilder);
            Argument.IsNotNull(options);

            modelBuilder.ApplyConfiguration(new PermissionGrantRecordConfiguration(options, style));
            modelBuilder.ApplyConfiguration(new PermissionGroupDefinitionRecordConfiguration(options, style));
            modelBuilder.ApplyConfiguration(new PermissionDefinitionRecordConfiguration(options, style));

            return modelBuilder;
        }
    }
}
