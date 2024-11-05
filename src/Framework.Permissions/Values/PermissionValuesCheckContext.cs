// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using System.Security.Claims;
using Framework.Kernel.Checks;
using Framework.Permissions.Models;

namespace Framework.Permissions.Values;

public sealed class PermissionValuesCheckContext(List<PermissionDefinition> permissions, ClaimsPrincipal? principal)
{
    public List<PermissionDefinition> Permissions { get; } = Argument.IsNotNull(permissions);

    public ClaimsPrincipal? Principal { get; } = principal;
}
