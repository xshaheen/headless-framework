// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

namespace Framework.Permissions.Models;

public interface ICanAddChildPermission
{
    PermissionDefinition AddChild(string name, string? displayName = null, bool isEnabled = true);
}
