// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using System.Security.Claims;
using Framework.Kernel.Checks;
using Framework.Permissions.Definitions;

namespace Framework.Permissions.Values;

public class PermissionValueCheckContext(PermissionDefinition permission, ClaimsPrincipal? principal)
{
    public PermissionDefinition Permission { get; } = Argument.IsNotNull(permission);

    public ClaimsPrincipal? Principal { get; } = principal;
}
