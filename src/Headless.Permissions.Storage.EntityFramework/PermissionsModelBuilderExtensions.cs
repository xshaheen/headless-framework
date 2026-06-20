// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Options;

namespace Headless.Permissions;

[PublicAPI]
public static class PermissionsModelBuilderExtensions
{
    extension(ModelBuilder modelBuilder)
    {
        /// <summary>
        /// Applies the Headless permissions entity configurations, resolving <see cref="PermissionsStorageOptions"/>
        /// from the <paramref name="context"/>'s service provider. Call from <c>OnModelCreating</c> with
        /// <c>modelBuilder.AddHeadlessPermissions(this)</c> to avoid injecting the options into the context.
        /// </summary>
        public ModelBuilder AddHeadlessPermissions(DbContext context)
        {
            Argument.IsNotNull(modelBuilder);
            Argument.IsNotNull(context);

            var options = context.GetService<IOptions<PermissionsStorageOptions>>().Value;

            return modelBuilder.AddHeadlessPermissions(options);
        }

        public ModelBuilder AddHeadlessPermissions(PermissionsStorageOptions options)
        {
            Argument.IsNotNull(modelBuilder);
            Argument.IsNotNull(options);

            modelBuilder.ApplyConfiguration(new PermissionGrantRecordConfiguration(options));
            modelBuilder.ApplyConfiguration(new PermissionGroupDefinitionRecordConfiguration(options));
            modelBuilder.ApplyConfiguration(new PermissionDefinitionRecordConfiguration(options));

            return modelBuilder;
        }
    }
}
