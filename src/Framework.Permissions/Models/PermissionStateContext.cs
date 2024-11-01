// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Permissions.Definitions;

namespace Framework.Permissions.Models;

public sealed class PermissionStateContext
{
    public required IServiceProvider ServiceProvider { get; set; }

    public required PermissionDefinition Permission { get; set; }
}
