// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Permissions.Entities;
using Microsoft.EntityFrameworkCore;

namespace Framework.Permissions;

public interface IPermissionsDbContext
{
    DbSet<PermissionGrantRecord> PermissionGrants { get; init; }

    DbSet<PermissionDefinitionRecord> PermissionDefinitions { get; init; }

    DbSet<PermissionGroupDefinitionRecord> PermissionGroupDefinitions { get; init; }
}
