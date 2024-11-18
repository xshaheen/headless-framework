// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Checks;

namespace Framework.Permissions.Models;

public sealed class PermissionGrant(string name, bool isGranted)
{
    public string Name { get; } = Argument.IsNotNull(name);

    public bool IsGranted { get; } = isGranted;
}
