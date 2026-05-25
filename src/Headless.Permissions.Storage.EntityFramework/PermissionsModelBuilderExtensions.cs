// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Permissions.Entities;
using Microsoft.EntityFrameworkCore;

namespace Headless.Permissions;

[PublicAPI]
public static class PermissionsModelBuilderExtensions
{
    public static ModelBuilder AddHeadlessPermissions(this ModelBuilder modelBuilder, PermissionsStorageOptions options)
    {
        Argument.IsNotNull(modelBuilder);
        Argument.IsNotNull(options);

        modelBuilder.ApplyConfiguration(new PermissionGrantRecordConfiguration(options));
        modelBuilder.ApplyConfiguration(new PermissionGroupDefinitionRecordConfiguration(options));
        modelBuilder.ApplyConfiguration(new PermissionDefinitionRecordConfiguration(options));

        return modelBuilder;
    }
}
