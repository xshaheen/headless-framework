// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Permissions.Entities;
using Microsoft.EntityFrameworkCore;

namespace Framework.Permissions;

public interface IPermissionsDbContext
{
    DbSet<PermissionGrantRecord> PermissionGrants { get; }

    DbSet<PermissionDefinitionRecord> PermissionDefinitions { get; }

    DbSet<PermissionGroupDefinitionRecord> PermissionGroupDefinitions { get; }
}
