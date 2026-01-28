// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Permissions.Entities;
using Microsoft.EntityFrameworkCore;

namespace Headless.Permissions.Storage.EntityFramework;

public interface IPermissionsDbContext
{
    DbSet<PermissionGrantRecord> PermissionGrants { get; }

    DbSet<PermissionDefinitionRecord> PermissionDefinitions { get; }

    DbSet<PermissionGroupDefinitionRecord> PermissionGroupDefinitions { get; }
}
