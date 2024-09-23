// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Kernel.Checks;
using Microsoft.AspNetCore.Authorization;

namespace Framework.Permissions.PermissionRequirements;

public sealed class PermissionsRequirement(string[] permissionNames, bool requiresAll) : IAuthorizationRequirement
{
    public string[] PermissionNames { get; } = Argument.IsNotNull(permissionNames);

    public bool RequiresAll { get; } = requiresAll;

    public override string ToString()
    {
        return $"PermissionsRequirement: {string.Join(", ", PermissionNames)}";
    }
}
