// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Permissions.Definitions;

namespace Framework.Permissions.Models;

public class PermissionStateContext
{
    public IServiceProvider ServiceProvider { get; set; } = default!;

    public PermissionDefinition Permission { get; set; } = default!;
}
